using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using VSMCP.Shared;
using Xunit;

namespace VSMCP.Tests.Skills.E2E;

/// <summary>
/// DebugNative skill e2e: <c>processes.list</c> should at minimum surface
/// <c>devenv</c>, and <c>modules.list</c> called without an active debug
/// session should return <c>not-debugging</c>. These are the two cheap
/// assertions the skill relies on before attaching.
/// </summary>
[Collection(E2ECollection.Name)]
public sealed class DebugNativeSkillTests
{
    private readonly E2EFixture _f;
    public DebugNativeSkillTests(E2EFixture f) => _f = f;

    [SkippableFact]
    public async Task DebugNative_processes_list_returns_devenv()
    {
        Skip.IfNot(E2EFixture.IsEnabled, E2EFixture.SkipReason);

        var rpc = await _f.ConnectAsync();
        var list = await rpc.ProcessesListAsync(new ProcessListFilter { NameContains = "devenv" });
        Assert.NotEmpty(list.Processes);
        Assert.Contains(list.Processes, p => p.Name.Contains("devenv", StringComparison.OrdinalIgnoreCase));
    }
}
