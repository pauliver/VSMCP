using System;
using System.Threading.Tasks;
using VSMCP.Shared;
using Xunit;

namespace VSMCP.Tests.Skills.E2E;

/// <summary>
/// Deferred E2E coverage for the #123 project.* context-free tools, brought to the audit
/// standard (#125-#146 parity). Opt-in via VSMCP_E2E=1. Read-only tools (investigate_symbol,
/// call_graph) assert real output against the open VSMCP solution; the mutating tools
/// (replace_member, add_member, add_member_to_subclasses) exercise ONLY guard / not-found /
/// empty-batch paths, so NO real source is modified.
///
/// Errors cross the named pipe as StreamJsonRpc RemoteInvocationException (not the typed
/// VsmcpException), so the throw assertions use ThrowsAnyAsync + a message substring.
/// </summary>
[Collection(E2ECollection.Name)]
public sealed class ProjectToolsE2ETests
{
    private readonly E2EFixture _f;
    public ProjectToolsE2ETests(E2EFixture f) => _f = f;

    private static string Missing(string prefix) => prefix + Guid.NewGuid().ToString("N");

    [SkippableFact]
    public async Task InvestigateSymbol_resolves_a_known_class()
    {
        Skip.IfNot(E2EFixture.IsEnabled, E2EFixture.SkipReason);
        var rpc = await _f.ConnectAsync();
        Skip.IfNot((await rpc.GetStatusAsync()).SolutionOpen, "Solution not open.");

        var r = await rpc.ProjectInvestigateSymbolAsync("RpcTarget", "csharp");
        Assert.Equal("RpcTarget", r.Name);
        Assert.Equal("csharp", r.Kind);
        Assert.True(r.UsageCount >= 0);
    }

    [SkippableFact]
    public async Task InvestigateSymbol_unknown_symbol_reports_not_found()
    {
        Skip.IfNot(E2EFixture.IsEnabled, E2EFixture.SkipReason);
        var rpc = await _f.ConnectAsync();
        Skip.IfNot((await rpc.GetStatusAsync()).SolutionOpen, "Solution not open.");

        var ex = await Assert.ThrowsAnyAsync<Exception>(
            () => rpc.ProjectInvestigateSymbolAsync(Missing("ZzNoSuchSymbol_"), "csharp"));
        Assert.Contains("Could not find symbol", ex.Message);
    }

    [SkippableFact]
    public async Task CallGraph_returns_root_for_a_known_method()
    {
        Skip.IfNot(E2EFixture.IsEnabled, E2EFixture.SkipReason);
        var rpc = await _f.ConnectAsync();
        Skip.IfNot((await rpc.GetStatusAsync()).SolutionOpen, "Solution not open.");

        var r = await rpc.ProjectCallGraphAsync("RpcTarget", "ProjectCallGraphAsync", 1, "csharp");
        Assert.NotNull(r.Root);
        Assert.Equal("RpcTarget.ProjectCallGraphAsync", r.Root!.Name);
        // CalledBy is a heuristic placeholder; do NOT require it to be populated.
    }

    [SkippableFact]
    public async Task ReplaceMember_unknown_class_guards_without_mutating()
    {
        Skip.IfNot(E2EFixture.IsEnabled, E2EFixture.SkipReason);
        var rpc = await _f.ConnectAsync();
        Skip.IfNot((await rpc.GetStatusAsync()).SolutionOpen, "Solution not open.");

        var ex = await Assert.ThrowsAnyAsync<Exception>(
            () => rpc.ProjectReplaceMemberAsync(Missing("ZzNoSuchClass_"), "Nope", "void Nope(){}", "csharp"));
        Assert.Contains("Could not find class", ex.Message);
    }

    [SkippableFact]
    public async Task AddMember_unknown_class_guards_without_mutating()
    {
        Skip.IfNot(E2EFixture.IsEnabled, E2EFixture.SkipReason);
        var rpc = await _f.ConnectAsync();
        Skip.IfNot((await rpc.GetStatusAsync()).SolutionOpen, "Solution not open.");

        var ex = await Assert.ThrowsAnyAsync<Exception>(
            () => rpc.ProjectAddMemberAsync(Missing("ZzNoSuchClass_"), "void X(){}", null, "csharp"));
        Assert.Contains("Could not find class", ex.Message);
    }

    [SkippableFact]
    public async Task AddMemberToSubclasses_no_derived_types_is_empty_batch()
    {
        Skip.IfNot(E2EFixture.IsEnabled, E2EFixture.SkipReason);
        var rpc = await _f.ConnectAsync();
        Skip.IfNot((await rpc.GetStatusAsync()).SolutionOpen, "Solution not open.");

        var r = await rpc.ProjectAddMemberToSubclassesAsync(Missing("ZzNoSuchBase_"), "void X(){}", "csharp");
        Assert.Equal(0, r.Total);
        Assert.Equal(0, r.Succeeded);
        Assert.Equal(0, r.Failed);
        Assert.Empty(r.Items);
    }
}
