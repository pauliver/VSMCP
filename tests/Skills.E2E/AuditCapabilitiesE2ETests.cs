using System;
using System.IO;
using System.Threading.Tasks;
using VSMCP.Shared;
using Xunit;

namespace VSMCP.Tests.Skills.E2E;

/// <summary>
/// E2E coverage for the capability tools added in the 2026-06 audit arc (#134-#141).
/// Opt-in via VSMCP_E2E=1. compile_flags + apply_patch are self-contained (temp files) and
/// make strong assertions; the Roslyn/VS-coupled tools are smoke-tested against the open
/// VSMCP solution and skip when prerequisites (solution/debug session) aren't met.
/// </summary>
[Collection(E2ECollection.Name)]
public sealed class AuditCapabilitiesE2ETests
{
    private readonly E2EFixture _f;
    public AuditCapabilitiesE2ETests(E2EFixture f) => _f = f;

    // ---- #135 compile_flags (server-local; no VS needed) ----

    [SkippableFact]
    public async Task CompileFlags_resolves_from_compile_commands()
    {
        Skip.IfNot(E2EFixture.IsEnabled, E2EFixture.SkipReason);
        var dir = Path.Combine(Path.GetTempPath(), "vsmcp-e2e-cc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var src = Path.Combine(dir, "a.cpp");
            File.WriteAllText(src, "int main(){}");
            var cc = Path.Combine(dir, "compile_commands.json");
            File.WriteAllText(cc, $@"[{{ ""directory"":""{dir.Replace("\\", "/")}"", ""file"":""a.cpp"", ""command"":""clang++ -Iinc -DX -std=c++17 -c a.cpp"" }}]");

            var r = await _f.Tools.CppCompileFlags(cc, src);
            Assert.True(r.Found);
            Assert.Equal("c++17", r.Std);
            Assert.Contains("X", r.Defines);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    // ---- #137 apply_patch (writes a temp file via rpc; no solution needed) ----

    [SkippableFact]
    public async Task ApplyPatch_edits_a_file_on_disk()
    {
        Skip.IfNot(E2EFixture.IsEnabled, E2EFixture.SkipReason);
        var rpc = await _f.ConnectAsync();
        var file = Path.Combine(Path.GetTempPath(), "vsmcp-e2e-patch-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(file, "line1\nline2\nline3\n");
        try
        {
            var diff = $"--- a/{Path.GetFileName(file)}\n+++ b/{Path.GetFileName(file)}\n@@ -2,1 +2,1 @@\n-line2\n+LINE2\n";
            // Use an absolute path in the patch so it resolves without a solution.
            diff = diff.Replace(Path.GetFileName(file), file.Replace('\\', '/'));

            var dry = await rpc.EditApplyPatchAsync(diff, dryRun: true);
            Assert.True(dry.Success);
            Assert.Contains("LINE2", dry.Files[0].PreviewText);
            Assert.Equal("line1\nline2\nline3\n", File.ReadAllText(file)); // unchanged on dry run

            var applied = await rpc.EditApplyPatchAsync(diff, dryRun: false);
            Assert.True(applied.Success);
            Assert.Contains("LINE2", File.ReadAllText(file));
        }
        finally { try { File.Delete(file); } catch { } }
    }

    // ---- #141 cpp index (against the open solution) ----

    [SkippableFact]
    public async Task CppIndex_finds_a_known_symbol()
    {
        Skip.IfNot(E2EFixture.IsEnabled, E2EFixture.SkipReason);
        var rpc = await _f.ConnectAsync();
        var status = await rpc.GetStatusAsync();
        Skip.IfNot(status.SolutionOpen, "Solution not open.");

        var stats = await rpc.CppIndexRebuildAsync();
        Skip.If(stats.Files == 0, "No C++ files in the open solution.");
        var hits = await rpc.CppIndexFindAsync("", null, 5);
        Assert.NotEmpty(hits.Entries);
    }

    // ---- #138/#139 Roslyn read-side smoke tests (open solution) ----

    [SkippableFact]
    public async Task TypeSurface_and_Complete_and_CallHierarchy_do_not_throw()
    {
        Skip.IfNot(E2EFixture.IsEnabled, E2EFixture.SkipReason);
        var rpc = await _f.ConnectAsync();
        var status = await rpc.GetStatusAsync();
        Skip.IfNot(status.SolutionOpen, "Solution not open.");

        // Resolve a real symbol's location for a guaranteed-valid position in a loaded document
        // (more robust than a filename filter, whose match semantics + index readiness vary).
        var sym = await rpc.CodeFindSymbolAsync("RpcTarget", null, 5);
        Skip.If(sym.Matches.Count == 0 || sym.Matches[0].Location == null, "RpcTarget not resolved (workspace not loaded).");
        var loc = sym.Matches[0].Location!;
        var pos = new CodePosition { File = loc.File, Line = loc.StartLine, Column = loc.StartColumn };

        // None should throw; results may be empty depending on the exact position.
        _ = await rpc.CodeCompleteAsync(pos, 20);
        _ = await rpc.CodeSignatureHelpAsync(pos);
        _ = await rpc.CodeCallHierarchyAsync(pos, "callers", 50);
        _ = await rpc.CodeTypeSurfaceAsync(pos, 50);
        _ = await rpc.CodeListFixesAsync(pos);
    }

    // ---- #136 format (idempotence check; opt-in destructive) ----

    [SkippableFact]
    public async Task Format_is_idempotent_on_a_temp_doc()
    {
        Skip.IfNot(E2EFixture.IsEnabled, E2EFixture.SkipReason);
        // Format requires a file in a loaded project; skip unless the harness designates a
        // scratch file. Kept as documentation of intended behavior for a manual run.
        Skip.If(true, "Format E2E needs a scratch C# file in the loaded project; run manually.");
        await Task.CompletedTask;
    }

    // ---- #140 set_variable (requires a break-mode debug session) ----

    [SkippableFact]
    public async Task SetVariable_requires_break_mode()
    {
        Skip.IfNot(E2EFixture.IsEnabled, E2EFixture.SkipReason);
        var rpc = await _f.ConnectAsync();
        var dbg = await rpc.DebugStateAsync();
        Skip.IfNot(dbg.Mode == DebugMode.Break, "No break-mode debug session.");
        // With a session at a frame that has an int local 'i', this would set it:
        var r = await rpc.DebugSetVariableAsync("i", "42");
        Assert.True(r.Success);
    }
}
