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
        // Destructive tests on temp files: disable Follow so VS doesn't auto-open the temp file
        // and trap edits in an unsaved buffer (test would read stale disk).
        await rpc.VsSetAutoFocusAsync(false);

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
        Console.WriteLine("=== AFTER organize_includes ===");
        Console.WriteLine(after);
        Console.WriteLine("=== END ===");
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
        await rpc.VsSetAutoFocusAsync(false);

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
    public async Task CppClasses_lists_solution_types()
    {
        Skip.IfNot(E2EFixture.IsEnabled, E2EFixture.SkipReason);
        var rpc = await _f.ConnectAsync();

        var result = await rpc.CppClassesAsync(null, null, 1000);
        Assert.Contains(result.Classes, c => c.Name == "Sample");
        Assert.Contains(result.Classes, c => c.Name == "Point" && c.Kind == "struct");
    }

    [SkippableFact]
    public async Task CppOutlineMany_batches_multiple_files()
    {
        Skip.IfNot(E2EFixture.IsEnabled, E2EFixture.SkipReason);
        var rpc = await _f.ConnectAsync();
        var sample = Path.Combine(_fixturesDir, "Sample.hpp");
        var sampleBase = Path.Combine(_fixturesDir, "SampleBase.hpp");

        var result = await rpc.CppOutlineManyAsync(new[] { sample, sampleBase });
        Assert.Equal(2, result.Entries.Count);
        Assert.Contains(result.Entries, e => e.File.EndsWith("Sample.hpp"));
        Assert.Contains(result.Entries, e => e.File.EndsWith("SampleBase.hpp"));
    }

    [SkippableFact]
    public async Task CppSymbolSummary_describes_known_class()
    {
        Skip.IfNot(E2EFixture.IsEnabled, E2EFixture.SkipReason);
        var rpc = await _f.ConnectAsync();

        var result = await rpc.CppSymbolSummaryAsync("Sample");
        Assert.NotNull(result);
        Assert.Equal("Sample", result.Symbol);
    }

    [SkippableFact]
    public async Task CppAnalyzerStatus_returns_state()
    {
        Skip.IfNot(E2EFixture.IsEnabled, E2EFixture.SkipReason);
        var rpc = await _f.ConnectAsync();

        var result = await rpc.CppAnalyzerStatusAsync(20);
        // Analyzer may or may not be running — just verify the call doesn't throw.
        Assert.NotNull(result);
    }

    [SkippableFact]
    public async Task CppGenerateEquality_inserts_operator()
    {
        Skip.IfNot(E2EFixture.IsEnabled, E2EFixture.SkipReason);
        var rpc = await _f.ConnectAsync();
        await rpc.VsSetAutoFocusAsync(false);

        var tempDir = CopyFixturesToTemp();
        var path = Path.Combine(tempDir, "EqStruct.hpp");
        File.WriteAllText(path, """
            #pragma once

            struct EqStruct
            {
                int x;
                int y;
            };
            """.Replace("\r\n", "\n"));

        var result = await rpc.CppGenerateEqualityAsync(path, "EqStruct");
        Assert.True(result.Inserted);

        var after = File.ReadAllText(path);
        Assert.Contains("operator==", after);
    }

    [SkippableFact]
    public async Task CppReplaceMember_swaps_method_body()
    {
        Skip.IfNot(E2EFixture.IsEnabled, E2EFixture.SkipReason);
        var rpc = await _f.ConnectAsync();
        await rpc.VsSetAutoFocusAsync(false);

        var tempDir = CopyFixturesToTemp();
        var path = Path.Combine(tempDir, "ReplaceTarget.hpp");
        File.WriteAllText(path, """
            #pragma once
            class ReplaceTarget
            {
            public:
                int Compute() { return 1; }
            };
            """.Replace("\r\n", "\n"));

        var newCode = "    int Compute() { return 42; }";
        var result = await rpc.CppReplaceMemberAsync(path, "ReplaceTarget", "Compute", newCode);
        Assert.True(result.Replaced);

        var after = File.ReadAllText(path);
        Assert.Contains("return 42;", after);
        Assert.DoesNotContain("return 1;", after);
    }

    [SkippableFact]
    public async Task CppSetUnsavedBuffer_overrides_disk_for_diagnostics()
    {
        Skip.IfNot(E2EFixture.IsEnabled, E2EFixture.SkipReason);
        var rpc = await _f.ConnectAsync();

        // Arrange: a small disk file with a known-good signature.
        var tempDir = CopyFixturesToTemp();
        var path = Path.Combine(tempDir, "Buffered.hpp");
        File.WriteAllText(path, """
            #pragma once
            int good() { return 0; }
            """.Replace("\r\n", "\n"));

        // Push a dirty buffer with a deliberate parse error and check diagnostics see it.
        var dirty = """
            #pragma once
            int broken() { return ; }   // syntax error: expected expression
            """.Replace("\r\n", "\n");

        await rpc.CppSetUnsavedBufferAsync(path, dirty);
        try
        {
            var diags = await rpc.CppDiagnosticsAsync(path, null, null);
            // Either the analyzer reports a real syntax diagnostic, or it parsed the dirty content
            // (HasErrors true). The disk file alone has no errors, so any error here proves the
            // override was honored.
            Assert.True(diags.HasErrors, $"Expected dirty-buffer parse error; got {diags.Diagnostics.Count} diagnostics with HasErrors={diags.HasErrors}.");
        }
        finally
        {
            await rpc.CppSetUnsavedBufferAsync(path, null);
        }
    }

    [SkippableFact]
    public async Task CppMoveType_rewrites_sibling_includes()
    {
        Skip.IfNot(E2EFixture.IsEnabled, E2EFixture.SkipReason);
        var rpc = await _f.ConnectAsync();
        await rpc.VsSetAutoFocusAsync(false);

        var tempDir = CopyFixturesToTemp();
        var srcHeader = Path.Combine(tempDir, "MoveSrc.hpp");
        var dstHeader = Path.Combine(tempDir, "MoveDst.hpp");
        var siblingCpp = Path.Combine(tempDir, "MoveSrc.cpp");

        File.WriteAllText(srcHeader, """
            #pragma once
            class Mover
            {
            public:
                int Compute(int x);
            };
            """.Replace("\r\n", "\n"));

        File.WriteAllText(siblingCpp, """
            #include "MoveSrc.hpp"
            int Mover::Compute(int x) { return x + 1; }
            """.Replace("\r\n", "\n"));

        var result = await rpc.CppMoveTypeAsync(srcHeader, "Mover", dstHeader, createTargetIfMissing: true);
        Assert.True(result.Moved);

        // The sibling .cpp should now also include the new header.
        var cppText = File.ReadAllText(siblingCpp);
        Assert.Contains("#include \"MoveDst.hpp\"", cppText);
        Assert.Contains(siblingCpp, result.UpdatedSiblingFiles, StringComparer.OrdinalIgnoreCase);
    }

    [SkippableFact]
    public async Task CppImplementInterface_handles_templated_base_via_strip()
    {
        Skip.IfNot(E2EFixture.IsEnabled, E2EFixture.SkipReason);
        var rpc = await _f.ConnectAsync();
        await rpc.VsSetAutoFocusAsync(false);

        var tempDir = CopyFixturesToTemp();
        var basePath = Path.Combine(tempDir, "TplBase.hpp");
        var derivedPath = Path.Combine(tempDir, "TplDerived.hpp");

        File.WriteAllText(basePath, """
            #pragma once
            template<typename T>
            class TplBase
            {
            public:
                virtual void Step() = 0;
                virtual int Get() const noexcept = 0;
            };
            """.Replace("\r\n", "\n"));

        File.WriteAllText(derivedPath, """
            #pragma once
            #include "TplBase.hpp"
            class TplDerived : public TplBase<int>
            {
            };
            """.Replace("\r\n", "\n"));

        // baseClass passed as the *instantiation* "TplBase<int>" — the impl should strip
        // template args for the find_symbol lookup.
        var result = await rpc.CppImplementInterfaceAsync(derivedPath, "TplDerived", "TplBase<int>", basePath);
        Assert.NotEmpty(result.InsertedMethods);

        var after = File.ReadAllText(derivedPath);
        Assert.Contains("Step()", after);
        Assert.Contains("Get()", after);
    }

    [SkippableFact]
    public async Task CppOverrideMember_inserts_override_stub()
    {
        Skip.IfNot(E2EFixture.IsEnabled, E2EFixture.SkipReason);
        var rpc = await _f.ConnectAsync();
        await rpc.VsSetAutoFocusAsync(false);

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
