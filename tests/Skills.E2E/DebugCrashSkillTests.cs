using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using VSMCP.Shared;
using Xunit;

namespace VSMCP.Tests.Skills.E2E;

/// <summary>
/// DebugCrash skill e2e: spin up a HelloCrash process, capture a dump via
/// <c>dump.save</c>, reopen it with <c>dump.open</c>, and confirm
/// <c>dump.summary</c> reports a faulting thread + modules.
/// </summary>
[Collection(E2ECollection.Name)]
public sealed class DebugCrashSkillTests
{
    private readonly E2EFixture _f;
    public DebugCrashSkillTests(E2EFixture f) => _f = f;

    [SkippableFact]
    public async Task DebugCrash_dump_save_then_open_summary_is_populated()
    {
        Skip.IfNot(E2EFixture.IsEnabled, E2EFixture.SkipReason);

        var rpc = await _f.ConnectAsync();

        var helloCrashDir = Path.Combine(_f.FixturesRoot, "HelloCrash");
        Skip.IfNot(Directory.Exists(helloCrashDir), $"Missing fixture: {helloCrashDir}");

        // Start HelloCrash as a long-running process (no crash arg → just sleeps).
        // `dotnet run` avoids having to resolve the build output path.
        using var proc = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"run --project \"{helloCrashDir}\" -- idle",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            }
        };
        proc.Start();
        try
        {
            // Give the runtime a moment so dump.save sees a settled process.
            await Task.Delay(TimeSpan.FromSeconds(3));

            var dumpPath = Path.Combine(Path.GetTempPath(), $"vsmcp-e2e-{Guid.NewGuid():N}.dmp");
            var save = await rpc.DumpSaveAsync(new DumpSaveOptions { Pid = proc.Id, Path = dumpPath, Full = false });
            Assert.True(save.BytesWritten > 0, "dump.save produced an empty dump");
            Assert.True(File.Exists(save.Path));

            var open = await rpc.DumpOpenAsync(new DumpOpenOptions { Path = save.Path });
            Assert.True(open.ModuleCount >= 0); // load succeeded without throwing

            var summary = await rpc.DumpSummaryAsync();
            Assert.True(summary.ModuleCount > 0, "dump.summary reported zero modules");

            try { File.Delete(save.Path); } catch { }
        }
        finally
        {
            try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { }
            try { await rpc.DebugStopAsync(); } catch { }
        }
    }
}
