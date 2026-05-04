using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using VSMCP.Shared;

namespace VSMCP.Vsix;

internal sealed partial class RpcTarget
{
    // -------- Test integration via vstest.console.exe --------

    public async Task<TestDiscoveryResult> TestDiscoverAsync(
        string? projectId, CancellationToken cancellationToken = default)
    {
        var assemblies = await ResolveTestAssembliesAsync(projectId, cancellationToken).ConfigureAwait(false);
        if (assemblies.Count == 0) return new TestDiscoveryResult();

        var vstest = await ResolveVsTestConsoleAsync(cancellationToken).ConfigureAwait(false);
        var args = "/ListTests " + string.Join(" ", assemblies.Select(a => $"\"{a}\""));
        var output = await RunProcessAsync(vstest, args, cancellationToken).ConfigureAwait(false);

        var result = new TestDiscoveryResult();
        // vstest.console emits test names line-by-line under "The following Tests are available:".
        bool inList = false;
        foreach (var line in output.Split('\n'))
        {
            var t = line.Trim();
            if (t.StartsWith("The following Tests are available", StringComparison.Ordinal)) { inList = true; continue; }
            if (!inList) continue;
            if (string.IsNullOrEmpty(t)) continue;
            if (!char.IsLetter(t[0]) && t[0] != '_') break; // end of list block
            result.Tests.Add(new TestCase
            {
                FullyQualifiedName = t,
                DisplayName = t.Split('.').Last(),
            });
        }
        result.Total = result.Tests.Count;
        return result;
    }

    public async Task<TestRunResult> TestRunAsync(
        string? filter, string? projectId, string? configuration,
        CancellationToken cancellationToken = default)
    {
        var assemblies = await ResolveTestAssembliesAsync(projectId, cancellationToken).ConfigureAwait(false);
        if (assemblies.Count == 0)
            return new TestRunResult { RunId = Guid.NewGuid().ToString("N"), Output = "No test assemblies resolved." };

        var vstest = await ResolveVsTestConsoleAsync(cancellationToken).ConfigureAwait(false);
        var args = string.Join(" ", assemblies.Select(a => $"\"{a}\""));
        if (!string.IsNullOrEmpty(filter)) args += $" /TestCaseFilter:\"{filter}\"";
        var output = await RunProcessAsync(vstest, args, cancellationToken).ConfigureAwait(false);

        var result = new TestRunResult { RunId = Guid.NewGuid().ToString("N"), Output = output };

        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.TrimEnd();
            if (line.StartsWith("Passed", StringComparison.OrdinalIgnoreCase)
                && line.Length > 6 && (line[6] == ' ' || line[6] == '!'))
            {
                result.Passed++;
                result.Results.Add(new TestResultItem { FullyQualifiedName = line.Substring(7).Trim(), Outcome = TestOutcome.Passed });
            }
            else if (line.StartsWith("Failed", StringComparison.OrdinalIgnoreCase)
                && line.Length > 6 && (line[6] == ' ' || line[6] == '!'))
            {
                result.Failed++;
                result.Results.Add(new TestResultItem { FullyQualifiedName = line.Substring(7).Trim(), Outcome = TestOutcome.Failed });
            }
            else if (line.StartsWith("Skipped", StringComparison.OrdinalIgnoreCase)
                && line.Length > 7 && line[7] == ' ')
            {
                result.Skipped++;
                result.Results.Add(new TestResultItem { FullyQualifiedName = line.Substring(8).Trim(), Outcome = TestOutcome.Skipped });
            }
        }
        return result;
    }

    private async Task<List<string>> ResolveTestAssembliesAsync(string? projectId, CancellationToken ct)
    {
        await _jtf.SwitchToMainThreadAsync(ct);
        if (await _package.GetServiceAsync(typeof(EnvDTE.DTE)) is not EnvDTE80.DTE2 dte)
            throw new VsmcpException(ErrorCodes.InteropFault, "DTE service unavailable.");

        var solution = dte.Solution
            ?? throw new VsmcpException(ErrorCodes.WrongState, "No solution open.");
        var assemblies = new List<string>();

        foreach (var p in VsHelpers.EnumerateProjects(solution))
        {
            ct.ThrowIfCancellationRequested();
            if (projectId is not null
                && !string.Equals(p.UniqueName, projectId, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(p.Name, projectId, StringComparison.OrdinalIgnoreCase))
                continue;

            string? full = null;
            try { full = p.FullName; } catch { }
            if (string.IsNullOrEmpty(full)) continue;

            string? outputType = null, asmName = null;
            try { outputType = p.Properties?.Item("OutputType")?.Value?.ToString(); } catch { }
            try { asmName = p.Properties?.Item("AssemblyName")?.Value?.ToString(); } catch { }
            asmName ??= p.Name;

            string? cfg = null, plat = null;
            try
            {
                var ac = p.ConfigurationManager?.ActiveConfiguration;
                cfg = ac?.ConfigurationName;
                plat = ac?.PlatformName?.Replace(" ", "");
            }
            catch { }

            var projectDir = Path.GetDirectoryName(full)!;
            var candidates = new List<string>();
            if (!string.IsNullOrEmpty(cfg))
                candidates.Add(Path.Combine(projectDir, "bin", cfg, asmName + ".dll"));
            candidates.Add(Path.Combine(projectDir, "bin", "Debug", asmName + ".dll"));
            candidates.Add(Path.Combine(projectDir, "bin", "Release", asmName + ".dll"));
            // .NET SDK style: bin/<cfg>/<tfm>/...
            foreach (var dir in Directory.Exists(Path.Combine(projectDir, "bin"))
                ? Directory.EnumerateDirectories(Path.Combine(projectDir, "bin"), "*", SearchOption.AllDirectories)
                : Enumerable.Empty<string>())
                candidates.Add(Path.Combine(dir, asmName + ".dll"));

            var hit = candidates.FirstOrDefault(File.Exists);
            if (hit is not null) assemblies.Add(hit);
        }
        return assemblies;
    }

    private async Task<string> ResolveVsTestConsoleAsync(CancellationToken ct)
    {
        await _jtf.SwitchToMainThreadAsync(ct);
        if (await _package.GetServiceAsync(typeof(SVsShell)) is IVsShell shell)
        {
            shell.GetProperty((int)__VSSPROPID.VSSPROPID_InstallDirectory, out var idObj);
            if (idObj is string installDir)
            {
                var p = Path.Combine(installDir, "..", "..", "Common7", "IDE", "CommonExtensions",
                    "Microsoft", "TestWindow", "vstest.console.exe");
                if (File.Exists(p)) return Path.GetFullPath(p);
            }
        }
        // Fallback: PATH lookup.
        return "vstest.console.exe";
    }

    private static Task<string> RunProcessAsync(string exe, string args, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<string>();
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
            var sb = new System.Text.StringBuilder();
            proc.OutputDataReceived += (_, e) => { if (e.Data is not null) sb.AppendLine(e.Data); };
            proc.ErrorDataReceived  += (_, e) => { if (e.Data is not null) sb.AppendLine(e.Data); };
            proc.Exited += (_, _) => tcs.TrySetResult(sb.ToString());
            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
            ct.Register(() => { try { if (!proc.HasExited) proc.Kill(); } catch { } });
        }
        catch (Exception ex) { tcs.SetException(new VsmcpException(ErrorCodes.InteropFault, $"Process start failed: {ex.Message}")); }
        return tcs.Task;
    }
}
