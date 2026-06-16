using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using StreamJsonRpc;
using VSMCP.Core;
using VSMCP.Shared;

namespace VSMCP.Vsix;

/// <summary>
/// Owns the per-VS lifecycle of the out-of-process VSMCP.CppAnalyzer sidecar.
/// Spawns the analyzer EXE on first use, connects to it over a per-VS pipe
/// (`VSMCP.Cpp.&lt;vsPid&gt;`), and exposes the <see cref="IVsmcpCppRpc"/>
/// proxy. Singleton-per-process so the connection survives across MCP-pipe
/// reconnects (RpcTarget is recreated on each reconnect; the analyzer should
/// not be).
/// </summary>
internal static class CppAnalyzerHost
{
    private static readonly object s_lock = new();
    private static Task<IVsmcpCppRpc>? s_connectTask;
    private static Process? s_analyzerProc;
    private static NamedPipeClientStream? s_stream;
    private static JsonRpc? s_rpc;
    // Job Object holds spawned analyzer processes. When devenv.exe exits, the OS releases
    // the job handle and force-kills every assigned process — even on a hard crash. The
    // job is created lazily and reused across re-spawns so there's never an orphan.
    private static JobObjectKillOnClose? s_killJob;
    private static string? s_logPath;
    private static readonly System.Collections.Generic.LinkedList<string> s_recentLog = new();
    private const int RecentLogCap = 200;
    private static string? s_lastError;
    // Bounds respawns so a sidecar that crashes on every start can't spin forever (#131).
    private static readonly SidecarRestartPolicy s_restart = new();

    /// <summary>Process-wide log path for the sidecar's stderr. Null until the first spawn.</summary>
    public static string? LogPath { get { lock (s_lock) return s_logPath; } }

    public static System.Collections.Generic.IReadOnlyList<string> SnapshotRecent(int max)
    {
        lock (s_lock)
        {
            var n = System.Math.Min(max, s_recentLog.Count);
            var list = new System.Collections.Generic.List<string>(n);
            var node = s_recentLog.Last;
            while (node is not null && list.Count < n) { list.Insert(0, node.Value); node = node.Previous; }
            return list;
        }
    }

    public static (Process? proc, string? logPath, string? lastError, bool spawnAttempted) Probe()
    {
        lock (s_lock) return (s_analyzerProc, s_logPath, s_lastError, s_connectTask is not null);
    }

    private static void AppendLog(string line)
    {
        lock (s_lock)
        {
            s_recentLog.AddLast(line);
            while (s_recentLog.Count > RecentLogCap) s_recentLog.RemoveFirst();
            try
            {
                if (!string.IsNullOrEmpty(s_logPath))
                    File.AppendAllText(s_logPath, line + Environment.NewLine);
            }
            catch { /* best-effort */ }
        }
    }

    public static Task<IVsmcpCppRpc> GetProxyAsync(CancellationToken ct)
    {
        lock (s_lock)
        {
            // Reuse a live (or still-connecting) session. A faulted/cancelled cached task means
            // the previous connect failed or the sidecar died — drop it and reconnect, so a
            // single transient failure doesn't permanently wedge every cpp_* tool (#131).
            if (s_connectTask is not null && !s_connectTask.IsFaulted && !s_connectTask.IsCanceled)
                return s_connectTask;
            s_connectTask = ConnectAsync(ct);
            return s_connectTask;
        }
    }

    private static async Task<IVsmcpCppRpc> ConnectAsync(CancellationToken ct)
    {
        // Circuit-breaker: refuse to respawn if the sidecar has been crashing in a tight loop.
        long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        bool allowed;
        lock (s_lock) allowed = s_restart.TryRegisterStart(nowMs);
        if (!allowed)
        {
            var msg = "CppAnalyzer crashed repeatedly and was not restarted (circuit breaker open). " +
                      $"See the sidecar log: {s_logPath ?? "<none>"}.";
            s_lastError = msg;
            AppendLog("[breaker] " + msg);
            throw new VsmcpException(ErrorCodes.InteropFault, msg);
        }

        var vsPid = Process.GetCurrentProcess().Id;
        var pipeName = PipeNaming.ForCppAnalyzer(vsPid);

        var exePath = ResolveAnalyzerExe();
        if (exePath is null || !File.Exists(exePath))
            throw new VsmcpException(ErrorCodes.Unsupported,
                $"CppAnalyzer executable not found. Expected at: {exePath ?? "<resolution failed>"}.");

        // Initialize the sidecar log file under %LOCALAPPDATA%\VSMCP\logs\.
        try
        {
            var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VSMCP", "logs");
            Directory.CreateDirectory(logDir);
            s_logPath = Path.Combine(logDir, $"cppanalyzer.{vsPid}.log");
            // Truncate / start fresh on a new spawn.
            File.WriteAllText(s_logPath, $"--- CppAnalyzer spawn {DateTime.UtcNow:O} (vsPid={vsPid}) ---{Environment.NewLine}");
        }
        catch (Exception ex) { s_lastError = $"log-init failed: {ex.Message}"; }

        // Spawn the analyzer.
        var psi = new ProcessStartInfo(exePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            WorkingDirectory = Path.GetDirectoryName(exePath),
            Arguments = $"--vs-pid {vsPid}",
        };
        var proc = Process.Start(psi)
            ?? throw new VsmcpException(ErrorCodes.InteropFault, "Failed to start CppAnalyzer process.");
        s_analyzerProc = proc;

        // Bind to a kill-on-close Job Object so the sidecar can't outlive devenv.exe,
        // even on a hard crash where Dispose() never runs.
        try
        {
            lock (s_lock) { s_killJob ??= new JobObjectKillOnClose(); }
            if (s_killJob is { IsValid: true })
            {
                if (!s_killJob.Assign(proc))
                    AppendLog($"[job] AssignProcessToJobObject failed (lastErr={Marshal.GetLastWin32Error()})");
                else
                    AppendLog("[job] sidecar bound to kill-on-close Job Object");
            }
            else
            {
                AppendLog("[job] CreateJobObject failed; sidecar may orphan if VS crashes");
            }
        }
        catch (Exception ex)
        {
            AppendLog($"[job] exception binding to job: {ex.Message}");
        }

        // Background-pump stderr + stdout into the rolling log so failures aren't silent.
        proc.ErrorDataReceived += (_, e) => { if (e.Data is not null) AppendLog($"[stderr] {e.Data}"); };
        proc.OutputDataReceived += (_, e) => { if (e.Data is not null) AppendLog($"[stdout] {e.Data}"); };
        proc.BeginErrorReadLine();
        proc.BeginOutputReadLine();
        proc.Exited += (_, _) =>
        {
            try { AppendLog($"[exited] code={proc.ExitCode}"); } catch { }
        };
        proc.EnableRaisingEvents = true;
        AppendLog($"[spawn] pid={proc.Id} exe={exePath}");

        // Connect to the analyzer's pipe (it took 60s for client connect; we have time).
        var stream = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            await stream.ConnectAsync(15_000, ct).ConfigureAwait(false);
        }
        catch
        {
            try { proc.Kill(); } catch { }
            throw new VsmcpException(ErrorCodes.InteropFault,
                "Failed to connect to CppAnalyzer pipe within 15s. Process likely failed to start.");
        }
        s_stream = stream;

        var rpc = new JsonRpc(new HeaderDelimitedMessageHandler(stream))
        {
            ExceptionStrategy = ExceptionProcessing.ISerializable,
        };
        var proxy = rpc.Attach<IVsmcpCppRpc>();
        rpc.StartListening();
        s_rpc = rpc;

        // Watchdog: when this connection drops (sidecar exit/crash/pipe break), clear the
        // cached session so the next GetProxyAsync transparently respawns (#131). Guard with
        // a reference check so a stale completion can't clobber a newer connection.
        _ = rpc.Completion.ContinueWith(_ =>
        {
            lock (s_lock)
            {
                if (ReferenceEquals(s_rpc, rpc))
                {
                    AppendLog("[watchdog] sidecar connection ended; will respawn on next call");
                    s_connectTask = null;
                    s_rpc = null;
                }
            }
        }, TaskScheduler.Default);

        // Smoke ping so the caller fails fast on a broken sidecar.
        try { await proxy.PingAsync(ct).ConfigureAwait(false); }
        catch (Exception ex)
        {
            try { proc.Kill(); } catch { }
            throw new VsmcpException(ErrorCodes.InteropFault,
                $"CppAnalyzer ping failed: {ex.Message}");
        }

        return proxy;
    }

    private static string? ResolveAnalyzerExe()
    {
        // VSIX install dir; analyzer ships in CppAnalyzer/ subfolder.
        var vsixDir = Path.GetDirectoryName(typeof(CppAnalyzerHost).Assembly.Location);
        if (string.IsNullOrEmpty(vsixDir)) return null;
        return Path.Combine(vsixDir, "CppAnalyzer", "vsmcp-cppanalyzer.exe");
    }

    /// <summary>Reset the connection (next call respawns). For recovery from sidecar crashes.</summary>
    public static void Reset()
    {
        lock (s_lock)
        {
            try { s_rpc?.Dispose(); } catch { }
            try { s_stream?.Dispose(); } catch { }
            try { s_analyzerProc?.Kill(); } catch { }
            s_rpc = null;
            s_stream = null;
            s_analyzerProc = null;
            s_connectTask = null;
        }
    }
}
