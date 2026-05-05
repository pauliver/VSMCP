using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using VSMCP.Shared;
using Xunit;

namespace VSMCP.Tests.Skills.E2E;

/// <summary>
/// E2E tests for the C++ tooling surface. Opt-in via <c>VSMCP_E2E=1</c>.
/// Fixtures live under <c>tests/Skills/Cpp/</c> and follow a copy-to-temp
/// discipline for any destructive test so the originals stay clean.
/// </summary>
[Collection(E2ECollection.Name)]
public sealed class CppSkillTests : IDisposable
{
    private readonly E2EFixture _f;
    private readonly string _fixturesDir;
    private readonly string _tempCopyDir;

    public CppSkillTests(E2EFixture f)
    {
        _f = f;
        _fixturesDir = Path.Combine(_f.FixturesRoot, "Cpp");
        _tempCopyDir = Path.Combine(Path.GetTempPath(), "vsmcp-cpp-tests-" + Guid.NewGuid().ToString("N").Substring(0, 8));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempCopyDir))
        {
            try { Directory.Delete(_tempCopyDir, recursive: true); } catch { }
        }
    }

    private string CopyFixturesToTemp()
    {
        Directory.CreateDirectory(_tempCopyDir);
        foreach (var src in Directory.GetFiles(_fixturesDir))
        {
            File.Copy(src, Path.Combine(_tempCopyDir, Path.GetFileName(src)), overwrite: true);
        }
        return _tempCopyDir;
    }

    // -------- Read-side tests (use the originals; non-destructive) --------

    [SkippableFact]
    public async Task CppOutline_returns_expected_types()
    {
        Skip.IfNot(E2EFixture.IsEnabled, E2EFixture.SkipReason);
        var rpc = await _f.ConnectAsync();
        var sampleHpp = Path.Combine(_fixturesDir, "Sample.hpp");

        var outline = await rpc.CppOutlineAsync(sampleHpp);

        Assert.Contains(outline.Declarations, d => d.Kind == "class" && d.Name == "Sample");
        Assert.Contains(outline.Declarations, d => d.Kind == "struct" && d.Name == "Point");
        Assert.Contains(outline.Declarations, d => d.Kind == "enum" && d.Name == "Color");
        Assert.Contains(outline.Declarations, d => d.Kind == "method" && d.Name == "Compute");
    }

    [SkippableFact]
    public async Task CppClassMembers_lists_public_methods_and_private_fields()
    {
        Skip.IfNot(E2EFixture.IsEnabled, E2EFixture.SkipReason);
        var rpc = await _f.ConnectAsync();
        var sampleHpp = Path.Combine(_fixturesDir, "Sample.hpp");

        var members = await rpc.CppClassMembersAsync(sampleHpp, "Sample");
        Assert.Contains(members.Members, m => m.Name == "Compute");
        Assert.Contains(members.Members, m => m.Name == "Reset");
        Assert.Contains(members.Members, m => m.Name == "Multiply");
    }

    [SkippableFact]
    public async Task CppFindSymbol_locates_named_struct()
    {
        Skip.IfNot(E2EFixture.IsEnabled, E2EFixture.SkipReason);
        var rpc = await _f.ConnectAsync();

        var hits = await rpc.CppFindSymbolAsync("Point", "struct", 10);
        Assert.NotEmpty(hits.Matches);
        Assert.Contains(hits.Matches, m => m.Name == "Point");
    }

    [SkippableFact]
    public async Task CppInheritance_walks_to_base_class()
    {
        Skip.IfNot(E2EFixture.IsEnabled, E2EFixture.SkipReason);
        var rpc = await _f.ConnectAsync();
        var sampleHpp = Path.Combine(_fixturesDir, "Sample.hpp");

        var tree = await rpc.CppInheritanceAsync(sampleHpp, "Sample", maxDepth: 3);
        Assert.NotNull(tree.Tree);
        Assert.Equal("Sample", tree.Tree!.Name);
        Assert.NotEmpty(tree.Tree.Bases);
        Assert.Contains(tree.Tree.Bases, b => b.Name == "SampleBase");
    }

    [SkippableFact]
    public async Task CppReadMember_returns_method_body_only()
    {
        Skip.IfNot(E2EFixture.IsEnabled, E2EFixture.SkipReason);
        var rpc = await _f.ConnectAsync();
        var sampleHpp = Path.Combine(_fixturesDir, "Sample.hpp");

        var result = await rpc.CppReadMemberAsync(sampleHpp, "Sample", "Multiply");
        Assert.True(result.StartLine > 0);
        Assert.True(result.EndLine >= result.StartLine);
        Assert.Contains("Multiply", result.Content);
    }

    // -------- Destructive tests — copy fixtures to temp first --------

    [SkippableFact]
    public async Task CppOrganizeIncludes_sorts_and_dedupes()
    {
        Skip.IfNot(E2EFixture.IsEnabled, E2EFixture.SkipReason);
        var rpc = await _f.ConnectAsync();

        // Build a temp file with shuffled, duplicated includes.
        var tempDir = CopyFixturesToTemp();
        var path = Path.Combine(tempDir, "DisorderedIncludes.hpp");
        File.WriteAllText(path, """
            #include "foo.hpp"
            #include <vector>
            #include "bar.hpp"
            #include <string>
            #include "foo.hpp"
            #include <map>

            int x = 0;
            """.Replace("\r\n", "\n"));

        var result = await rpc.CppOrganizeIncludesAsync(path);
        Assert.True(result.Changed);
        Assert.Equal(6, result.IncludesCounted);
        Assert.Equal(1, result.Duplicates);

        var after = File.ReadAllText(path);
        // System includes (<>) should come before quoted includes ("").
        var systemIdx = after.IndexOf("#include <map>", StringComparison.Ordinal);
        var quotedIdx = after.IndexOf("#include \"bar.hpp\"", StringComparison.Ordinal);
        Assert.True(systemIdx < quotedIdx, "System includes should come first");
        // Duplicate foo.hpp should appear once.
        Assert.Equal(1, System.Text.RegularExpressions.Regex.Matches(after, @"#include ""foo\.hpp""").Count);
    }

    [SkippableFact]
    public async Task CppGenerateConstructor_inserts_ctor_with_member_init_list()
    {
        Skip.IfNot(E2EFixture.IsEnabled, E2EFixture.SkipReason);
        var rpc = await _f.ConnectAsync();

        // Fresh struct fixture so the test is hermetic.
        var tempDir = CopyFixturesToTemp();
        var path = Path.Combine(tempDir, "NeedsCtor.hpp");
        File.WriteAllText(path, """
            #pragma once

            struct NeedsCtor
            {
                int x;
                int y;
                const char* name;
            };
            """.Replace("\r\n", "\n"));

        var result = await rpc.CppGenerateConstructorAsync(path, "NeedsCtor", null);
        Assert.True(result.Inserted);

        var after = File.ReadAllText(path);
        Assert.Contains("NeedsCtor(int x_, int y_, const char* name_)", after);
        Assert.Contains("x(x_)", after);
        Assert.Contains("y(y_)", after);
        Assert.Contains("name(name_)", after);
    }

    [SkippableFact]
    public async Task CppOverrideMember_inserts_override_stub()
    {
        Skip.IfNot(E2EFixture.IsEnabled, E2EFixture.SkipReason);
        var rpc = await _f.ConnectAsync();

        var tempDir = CopyFixturesToTemp();
        var path = Path.Combine(tempDir, "Derived.hpp");
        File.WriteAllText(path, """
            #pragma once
            class Base { public: virtual void doIt() = 0; };
            class Derived : public Base
            {
            };
            """.Replace("\r\n", "\n"));

        var result = await rpc.CppOverrideMemberAsync(path, "Derived", "doIt", "void", "");
        Assert.True(result.Inserted);

        var after = File.ReadAllText(path);
        Assert.Contains("void doIt() override", after);
    }
}
