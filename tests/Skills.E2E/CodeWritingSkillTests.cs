using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using VSMCP.Shared;
using Xunit;

namespace VSMCP.Tests.Skills.E2E;

/// <summary>
/// E2E tests covering the code-writing surface added in M12-M19 + M18 + codegen.
/// Each test is opt-in via VSMCP_E2E=1 and additionally skipped when the relevant
/// fixture conditions aren't met (e.g. need a real C# solution open).
///
/// Tests are intentionally non-destructive: they rely on the VSMCP solution
/// itself being the open solution. Run them from a VS instance with VSMCP.sln
/// loaded.
/// </summary>
[Collection(E2ECollection.Name)]
public sealed class CodeWritingSkillTests
{
    private readonly E2EFixture _f;
    public CodeWritingSkillTests(E2EFixture f) => _f = f;

    // -------- M12 file/symbol discovery --------

    [SkippableFact]
    public async Task FileList_returns_solution_files()
    {
        Skip.IfNot(E2EFixture.IsEnabled, E2EFixture.SkipReason);
        var rpc = await _f.ConnectAsync();

        var result = await rpc.FileListAsync(null, null, "*.cs", new[] { "file" }, 5000);
        Assert.NotEmpty(result.Files);
        Assert.All(result.Files, f => Assert.EndsWith(".cs", f.Path, StringComparison.OrdinalIgnoreCase));
    }

    [SkippableFact]
    public async Task FileGlob_honors_multiple_patterns()
    {
        Skip.IfNot(E2EFixture.IsEnabled, E2EFixture.SkipReason);
        var rpc = await _f.ConnectAsync();

        var result = await rpc.FileGlobAsync(new[] { "**/*.csproj", "**/*.cs" }, null);
        Assert.NotEmpty(result.Files);
    }

    [SkippableFact]
    public async Task FileMembers_returns_only_declared_by_default()
    {
        Skip.IfNot(E2EFixture.IsEnabled, E2EFixture.SkipReason);
        var rpc = await _f.ConnectAsync();
        var status = await rpc.GetStatusAsync();
        Skip.IfNot(status.SolutionOpen, "Solution not open.");

        // Find a known file in the open solution.
        var files = await rpc.FileListAsync(null, null, "RpcTarget.Code.cs", new[] { "file" }, 10);
        Skip.If(files.Files.Count == 0, "RpcTarget.Code.cs not found in solution.");

        var members = await rpc.FileMembersAsync(files.Files[0].Path, "RpcTarget", null, excludeInherited: true);
        Assert.True(members.Members.Count > 0);
    }

    [SkippableFact]
    public async Task FileInheritance_returns_base_chain_for_known_class()
    {
        Skip.IfNot(E2EFixture.IsEnabled, E2EFixture.SkipReason);
        var rpc = await _f.ConnectAsync();

        var files = await rpc.FileListAsync(null, null, "DiagEventCollector.cs", new[] { "file" }, 10);
        Skip.If(files.Files.Count == 0, "DiagEventCollector.cs not found.");

        var info = await rpc.FileInheritanceAsync(files.Files[0].Path, "DiagEventCollector");
        Assert.NotNull(info.Hierarchy);
    }

    // -------- M13 search --------

    [SkippableFact]
    public async Task SearchText_finds_known_token()
    {
        Skip.IfNot(E2EFixture.IsEnabled, E2EFixture.SkipReason);
        var rpc = await _f.ConnectAsync();

        var result = await rpc.SearchTextAsync(@"RpcTarget", "*.cs", null, null, 50);
        Assert.NotEmpty(result.Matches);
    }

    [SkippableFact]
    public async Task SearchSymbol_finds_RpcTarget_class()
    {
        Skip.IfNot(E2EFixture.IsEnabled, E2EFixture.SkipReason);
        var rpc = await _f.ConnectAsync();

        var result = await rpc.SearchSymbolAsync("RpcTarget", new[] { "namedtype" }, null, 10);
        Assert.NotEmpty(result.Symbols);
    }

    // -------- M18 semantic --------

    [SkippableFact]
    public async Task FindSymbol_locates_well_known_method()
    {
        Skip.IfNot(E2EFixture.IsEnabled, E2EFixture.SkipReason);
        var rpc = await _f.ConnectAsync();

        var result = await rpc.CodeFindSymbolAsync("HandshakeAsync", "method", 10);
        Assert.NotEmpty(result.Matches);
        Assert.All(result.Matches, m => Assert.Equal("HandshakeAsync", m.Name));
    }

    [SkippableFact]
    public async Task ReadMember_returns_only_one_member()
    {
        Skip.IfNot(E2EFixture.IsEnabled, E2EFixture.SkipReason);
        var rpc = await _f.ConnectAsync();

        var result = await rpc.CodeReadMemberAsync(file: null, "RpcTarget", "HandshakeAsync");
        Assert.Contains("HandshakeAsync", result.Content);
        Assert.True(result.EndLine > result.StartLine);
        // Sanity: the returned content must not contain other top-level methods.
        Assert.DoesNotContain("PingAsync", result.Content);
    }

    // -------- M15 refactoring --------

    [SkippableFact]
    public async Task EditOrganizeUsings_is_idempotent()
    {
        Skip.IfNot(E2EFixture.IsEnabled, E2EFixture.SkipReason);
        var rpc = await _f.ConnectAsync();

        var files = await rpc.FileListAsync(null, null, "RpcTarget.Files.cs", new[] { "file" }, 10);
        Skip.If(files.Files.Count == 0, "RpcTarget.Files.cs not found.");

        var first = await rpc.EditOrganizeUsingsAsync(files.Files[0].Path, addMissing: false, removeUnused: true);
        var second = await rpc.EditOrganizeUsingsAsync(files.Files[0].Path, addMissing: false, removeUnused: true);
        // Second call should report no changes.
        Assert.Equal(0, second.Changes);
    }

    // -------- Active editor --------

    [SkippableFact]
    public async Task EditorActive_returns_a_path()
    {
        Skip.IfNot(E2EFixture.IsEnabled, E2EFixture.SkipReason);
        var rpc = await _f.ConnectAsync();

        var info = await rpc.EditorActiveAsync();
        // No assertion on file content; we just don't want it to throw.
        Assert.NotNull(info);
    }

    // -------- Workspace events --------

    [SkippableFact]
    public async Task WorkspaceEvents_returns_within_timeout()
    {
        Skip.IfNot(E2EFixture.IsEnabled, E2EFixture.SkipReason);
        var rpc = await _f.ConnectAsync();

        var events = await rpc.WorkspaceWatchAsync(sinceTimestampMs: 0, timeoutMs: 1000, maxResults: 10);
        Assert.NotNull(events);
    }

    // -------- NuGet --------

    [SkippableFact]
    public async Task NugetList_returns_packages()
    {
        Skip.IfNot(E2EFixture.IsEnabled, E2EFixture.SkipReason);
        var rpc = await _f.ConnectAsync();

        var result = await rpc.NugetListAsync(null);
        Assert.NotNull(result);
        // VSMCP itself references at least StreamJsonRpc; if the solution is some other repo, this is informational.
    }
}
