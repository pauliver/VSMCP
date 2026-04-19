using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VSMCP.Shared;
using Xunit;

namespace VSMCP.Tests.Skills.E2E;

/// <summary>
/// Debug skill e2e: set a line breakpoint in HelloCrash, launch, verify we
/// stop in Break mode and that frame.locals reports <c>s == null</c>.
/// </summary>
[Collection(E2ECollection.Name)]
public sealed class DebugSkillTests
{
    private readonly E2EFixture _f;
    public DebugSkillTests(E2EFixture f) => _f = f;

    [SkippableFact]
    public async Task Debug_HelloCrash_nre_breakpoint_reports_null_local()
    {
        Skip.IfNot(E2EFixture.IsEnabled, E2EFixture.SkipReason);

        var rpc = await _f.ConnectAsync();
        var status = await rpc.GetStatusAsync();
        Skip.IfNot(status.SolutionOpen, "Open HelloCrash.sln in VS before running this test.");

        var program = Path.Combine(_f.FixturesRoot, "HelloCrash", "Program.cs");
        Skip.IfNot(File.Exists(program), $"Expected fixture source at {program}.");

        var openBrace = FindLineContaining(program, "CrashWithNullDeref") + 2;

        var bp = await rpc.BreakpointSetAsync(new BreakpointSetOptions
        {
            Kind = BreakpointKind.Line,
            File = program,
            Line = openBrace,
        });
        Assert.False(string.IsNullOrEmpty(bp.Id));

        try
        {
            await rpc.DebugLaunchAsync(new DebugLaunchOptions());
            var mode = await WaitForModeAsync(rpc, DebugMode.Break, TimeSpan.FromSeconds(30));
            Assert.Equal(DebugMode.Break, mode);

            var locals = await rpc.FrameLocalsAsync(threadId: null, frameIndex: 0, expandDepth: 0);
            var s = locals.Variables.FirstOrDefault(v => v.Name == "s");
            Assert.NotNull(s);
            Assert.Contains("null", s!.Value ?? "", StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { await rpc.DebugStopAsync(); } catch { }
            try { await rpc.BreakpointRemoveAsync(bp.Id); } catch { }
        }
    }

    private static int FindLineContaining(string path, string needle)
    {
        var lines = File.ReadAllLines(path);
        for (int i = 0; i < lines.Length; i++)
            if (lines[i].Contains(needle, StringComparison.Ordinal)) return i + 1;
        throw new InvalidOperationException($"'{needle}' not found in {path}.");
    }

    internal static async Task<DebugMode> WaitForModeAsync(IVsmcpRpc rpc, DebugMode expected, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var info = await rpc.DebugStateAsync();
            if (info.Mode == expected) return info.Mode;
            await Task.Delay(250);
        }
        var last = await rpc.DebugStateAsync();
        return last.Mode;
    }
}
