using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using VSMCP.Server;
using VSMCP.Shared;
using Xunit;

namespace VSMCP.Tests.Skills.E2E;

/// <summary>
/// DebugPerf skill e2e: run HotLoop in CPU-burn mode, profile ~6s via
/// <see cref="VsmcpTools.ProfilerStart"/>, and confirm BurnCpu tops the report.
/// Profiler runs in the server process; no VS interaction required here.
/// </summary>
[Collection(E2ECollection.Name)]
public sealed class DebugPerfSkillTests
{
    private readonly E2EFixture _f;
    public DebugPerfSkillTests(E2EFixture f) => _f = f;

    [SkippableFact]
    public async Task DebugPerf_HotLoop_cpu_profile_reports_BurnCpu_in_hot_list()
    {
        Skip.IfNot(E2EFixture.IsEnabled, E2EFixture.SkipReason);

        var hotLoopDir = Path.Combine(_f.FixturesRoot, "HotLoop");
        Skip.IfNot(Directory.Exists(hotLoopDir), $"Missing fixture: {hotLoopDir}");

        using var proc = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"run --project \"{hotLoopDir}\" -- cpu",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            }
        };
        proc.Start();
        try
        {
            // Wait until the .NET diagnostic port is up; `dotnet run` also compiles
            // on first start, so give it plenty of time.
            await Task.Delay(TimeSpan.FromSeconds(5));

            var started = await _f.Tools.ProfilerStart(proc.Id, ProfilerMode.CpuSampling);
            Assert.False(string.IsNullOrEmpty(started.SessionId));

            await Task.Delay(TimeSpan.FromSeconds(6));

            var stopped = await _f.Tools.ProfilerStop(started.SessionId);
            Assert.True(stopped.BytesWritten > 0, "profiler.stop produced an empty trace");

            var report = await _f.Tools.ProfilerReport(stopped.OutputPath, top: 25);
            Assert.True(report.TotalSamples > 0, "profiler.report returned zero samples");
            Assert.Contains(
                report.Hot,
                h => h.FunctionName.Contains("BurnCpu", StringComparison.OrdinalIgnoreCase));

            try { File.Delete(stopped.OutputPath); } catch { }
        }
        finally
        {
            try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { }
        }
    }
}
