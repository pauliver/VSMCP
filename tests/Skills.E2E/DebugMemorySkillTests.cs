using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using VSMCP.Shared;
using Xunit;

namespace VSMCP.Tests.Skills.E2E;

/// <summary>
/// DebugMemory skill e2e: run HotLoop in alloc-churn mode and sample
/// <c>counters.get</c> — working set + managed heap should be non-trivial.
/// </summary>
[Collection(E2ECollection.Name)]
public sealed class DebugMemorySkillTests
{
    private readonly E2EFixture _f;
    public DebugMemorySkillTests(E2EFixture f) => _f = f;

    [SkippableFact]
    public async Task DebugMemory_HotLoop_alloc_counters_reflect_live_process()
    {
        Skip.IfNot(E2EFixture.IsEnabled, E2EFixture.SkipReason);

        var rpc = await _f.ConnectAsync();

        var hotLoopDir = Path.Combine(_f.FixturesRoot, "HotLoop");
        Skip.IfNot(Directory.Exists(hotLoopDir), $"Missing fixture: {hotLoopDir}");

        using var proc = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"run --project \"{hotLoopDir}\" -- alloc",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            }
        };
        proc.Start();
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(3));

            var snap = await rpc.CountersGetAsync(proc.Id, sampleMs: 1000);
            Assert.Equal(proc.Id, snap.Pid);
            Assert.True(snap.WorkingSetBytes > 1_000_000,
                $"expected non-trivial working set, got {snap.WorkingSetBytes}");
        }
        finally
        {
            try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { }
        }
    }
}
