using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VSMCP.Shared;

namespace VSMCP.Server;

/// <summary>
/// Runs DbgEng commands against a dump file by shelling out to cdb.exe -z -c "&lt;cmd&gt;; q".
/// Pure out-of-process; nothing here touches the VS host. Gated by <see cref="VsmcpConfig.AllowDbgEng"/>
/// at the tool-surface layer.
/// </summary>
public static class DbgEngInvoker
{
    private const int DefaultTimeoutMs = 120_000;
    private const int MinTimeoutMs = 5_000;
    private const int MaxTimeoutMs = 600_000;
    private const int OutputCapBytes = 1 * 1024 * 1024;

    public static async Task<DumpDbgEngResult> RunAsync(DumpDbgEngOptions options, CancellationToken ct)
    {
        if (options is null) throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(options.DumpPath))
            throw new ArgumentException("DumpPath is required.", nameof(options));
        if (string.IsNullOrWhiteSpace(options.Command))
            throw new ArgumentException("Command is required.", nameof(options));

        var dumpPath = Path.GetFullPath(options.DumpPath);
        if (!File.Exists(dumpPath))
            throw new FileNotFoundException($"Dump file not found: {dumpPath}", dumpPath);

        var cdbPath = ResolveCdbPath(options.CdbPath);
        var timeout = Math.Clamp(options.TimeoutMs ?? DefaultTimeoutMs, MinTimeoutMs, MaxTimeoutMs);

        // cdb's -c argument runs the given command script then we always append "q" so it exits.
        // User-supplied command is wrapped in ".block { ... }" to scope any local aliases.
        var commandScript = options.Command.TrimEnd(';', ' ', '\t', '\r', '\n') + "; q";

        var psi = new ProcessStartInfo
        {
            FileName = cdbPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("-z");
        psi.ArgumentList.Add(dumpPath);
        if (!string.IsNullOrWhiteSpace(options.SymbolPath))
        {
            psi.ArgumentList.Add("-y");
            psi.ArgumentList.Add(options.SymbolPath!);
        }
        psi.ArgumentList.Add("-logo");
        psi.ArgumentList.Add(Path.Combine(Path.GetTempPath(), "vsmcp-cdb-" + Guid.NewGuid().ToString("N") + ".log"));
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add(commandScript);

        var sw = Stopwatch.StartNew();
        using var proc = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        bool truncated = false;

        proc.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            if (stdout.Length >= OutputCapBytes) { truncated = true; return; }
            stdout.AppendLine(e.Data);
        };
        proc.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            if (stderr.Length >= OutputCapBytes) { truncated = true; return; }
            stderr.AppendLine(e.Data);
        };

        try { proc.Start(); }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to launch cdb.exe at '{cdbPath}': {ex.Message}", ex);
        }
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        bool timedOut = false;
        using (var cts = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            cts.CancelAfter(timeout);
            try
            {
                await proc.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                timedOut = !ct.IsCancellationRequested;
                try { proc.Kill(entireProcessTree: true); } catch { }
                try { await proc.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                if (!timedOut) throw;
            }
        }
        sw.Stop();

        var output = stdout.ToString();
        if (output.Length > OutputCapBytes)
        {
            output = output.Substring(0, OutputCapBytes);
            truncated = true;
        }

        return new DumpDbgEngResult
        {
            Command = options.Command,
            CdbPath = cdbPath,
            ExitCode = timedOut ? -1 : proc.ExitCode,
            ElapsedMs = sw.ElapsedMilliseconds,
            Output = output,
            Stderr = stderr.Length == 0 ? null : stderr.ToString(),
            Truncated = truncated,
            TimedOut = timedOut,
        };
    }

    private static string ResolveCdbPath(string? overridePath)
    {
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            var p = Path.GetFullPath(overridePath!);
            if (!File.Exists(p))
                throw new FileNotFoundException($"cdb.exe not found at override path: {p}", p);
            return p;
        }

        foreach (var candidate in CandidateCdbPaths())
        {
            if (File.Exists(candidate)) return candidate;
        }

        var cdbOnPath = FindOnPath("cdb.exe");
        if (cdbOnPath is not null) return cdbOnPath;

        throw new FileNotFoundException(
            "cdb.exe not found. Install the Windows SDK Debugging Tools (usually under %ProgramFiles(x86)%\\Windows Kits\\10\\Debuggers\\x64\\cdb.exe), or pass CdbPath explicitly.");
    }

    private static IEnumerable<string> CandidateCdbPaths()
    {
        var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        foreach (var root in new[] { pf86, pf })
        {
            if (string.IsNullOrEmpty(root)) continue;
            yield return Path.Combine(root, "Windows Kits", "10", "Debuggers", "x64", "cdb.exe");
            yield return Path.Combine(root, "Windows Kits", "10", "Debuggers", "x86", "cdb.exe");
            yield return Path.Combine(root, "Debugging Tools for Windows (x64)", "cdb.exe");
            yield return Path.Combine(root, "Debugging Tools for Windows (x86)", "cdb.exe");
        }
    }

    private static string? FindOnPath(string exe)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path)) return null;
        foreach (var dir in path.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            try
            {
                var candidate = Path.Combine(dir, exe);
                if (File.Exists(candidate)) return candidate;
            }
            catch { }
        }
        return null;
    }
}
