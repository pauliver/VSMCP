using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using VSMCP.Shared;
using Xunit;

namespace VSMCP.Tests.Skills.E2E;

/// <summary>
/// Destructive-then-revert E2E coverage for the mutation surface, following the C++ tests'
/// discipline: every test mutates ONLY temp copies under tests/Skills/.e2e-tmp/ (inside the
/// repo, so the write-scope policy allows them) and deletes them in a finally.
/// </summary>
[Collection(E2ECollection.Name)]
public sealed class MutationSurfaceE2ETests
{
    private readonly E2EFixture _f;
    public MutationSurfaceE2ETests(E2EFixture f) => _f = f;

    private string MakeTempDir()
    {
        var dir = Path.Combine(_f.FixturesRoot, ".e2e-tmp", Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [SkippableFact]
    public async Task FileMoveMany_dryRun_reports_plan_and_real_move_roundtrips()
    {
        Skip.IfNot(E2EFixture.IsEnabled, E2EFixture.SkipReason);
        var rpc = await _f.ConnectAsync();
        var dir = MakeTempDir();
        try
        {
            var from = Path.Combine(dir, "a.txt");
            var to = Path.Combine(dir, "sub", "b.txt");
            File.WriteAllText(from, "mutation-surface e2e");

            // Dry run: nothing moves.
            var plan = await rpc.FileMoveManyAsync(
                new[] { new FileMovePair { From = from, To = to } }, updateProject: false, dryRun: true);
            Assert.True(File.Exists(from));
            Assert.False(File.Exists(to));
            Assert.Equal(1, plan.Total);
            Assert.Equal(0, plan.MovedCount);

            // Real move, then move back.
            var moved = await rpc.FileMoveManyAsync(
                new[] { new FileMovePair { From = from, To = to } }, updateProject: false, dryRun: false);
            Assert.Equal(1, moved.MovedCount);
            Assert.Equal(0, moved.ErrorCount);
            Assert.False(File.Exists(from));
            Assert.Equal("mutation-surface e2e", File.ReadAllText(to));

            var back = await rpc.FileMoveManyAsync(
                new[] { new FileMovePair { From = to, To = from } }, updateProject: false, dryRun: false);
            Assert.Equal(1, back.MovedCount);
            Assert.True(File.Exists(from));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [SkippableFact]
    public async Task FileMoveMany_rejects_destination_outside_write_roots()
    {
        Skip.IfNot(E2EFixture.IsEnabled, E2EFixture.SkipReason);
        var rpc = await _f.ConnectAsync();
        var dir = MakeTempDir();
        try
        {
            var from = Path.Combine(dir, "escape.txt");
            File.WriteAllText(from, "should never leave the repo");
            var to = Path.Combine(Path.GetTempPath(), "vsmcp-e2e-escape-" + Guid.NewGuid().ToString("N") + ".txt");

            var ex = await Assert.ThrowsAnyAsync<Exception>(() => rpc.FileMoveManyAsync(
                new[] { new FileMovePair { From = from, To = to } }, updateProject: false, dryRun: false));
            Assert.Contains("outside the allowed write roots", ex.Message);
            Assert.True(File.Exists(from));   // fail-fast: source untouched
            Assert.False(File.Exists(to));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [SkippableFact]
    public async Task CppReplaceMember_on_temp_copy_replaces_body_and_outline_stays_parseable()
    {
        Skip.IfNot(E2EFixture.IsEnabled, E2EFixture.SkipReason);
        var rpc = await _f.ConnectAsync();

        var source = Path.Combine(_f.FixturesRoot, "Cpp", "MiniRenderer", "Renderer.h");
        Skip.If(!File.Exists(source), "MiniRenderer fixture not found.");

        var dir = MakeTempDir();
        try
        {
            var copy = Path.Combine(dir, "Renderer.h");
            File.Copy(source, copy);

            var outline = await rpc.CppOutlineAsync(copy);
            var cls = outline.Declarations.FirstOrDefault(d =>
                string.Equals(d.Kind, "class", StringComparison.OrdinalIgnoreCase));
            Skip.If(cls is null, "No class declaration found in the fixture copy.");

            // Find a member with a body we can rewrite.
            var member = await rpc.CppClassMembersAsync(copy, cls!.Name);
            var target = member.Members.FirstOrDefault(m => !string.IsNullOrEmpty(m.Name));
            Skip.If(target is null, "No members found on the fixture class.");

            var read = await rpc.CppReadMemberAsync(copy, cls.Name, target!.Name);
            Skip.If(string.IsNullOrEmpty(read.Content), "Member body unreadable.");

            var replaced = await rpc.CppReplaceMemberAsync(copy, cls.Name, target.Name, read.Content);
            Assert.True(replaced.Replaced);

            // The file must still parse to the same class afterwards (no corruption).
            var after = await rpc.CppOutlineAsync(copy);
            Assert.Contains(after.Declarations, d => d.Name == cls.Name);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }
}
