using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using StreamJsonRpc;
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
    private static string? s_logPath;
    private static readonly System.Collections.Generic.LinkedList<string> s_recentLog = new();
    private const int RecentLogCap = 200;
    private static string? s_lastError;

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
            if (s_connectTask is not null) return s_connectTask;
            s_connectTask = ConnectAsync(ct);
            return s_connectTask;
        }
    }

    private static async Task<IVsmcpCppRpc> ConnectAsync(CancellationToken ct)
    {
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
