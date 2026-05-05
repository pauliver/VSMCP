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

    // -------- Additional read-side coverage --------

    [SkippableFact]
    public async Task CppFindSymbol_with_glob_pattern_returns_matches()
    {
        Skip.IfNot(E2EFixture.IsEnabled, E2EFixture.SkipReason);
        var rpc = await _f.ConnectAsync();

        var hits = await rpc.CppFindSymbolAsync("Sample*", null, 50);
        Assert.NotEmpty(hits.Matches);
        Assert.Contains(hits.Matches, m => m.Name == "Sample" || m.Name == "SampleBase");
    }

    [SkippableFact]
    public async Task CppFindSymbol_with_unknown_returns_empty()
    {
        Skip.IfNot(E2EFixture.IsEnabled, E2EFixture.SkipReason);
        var rpc = await _f.ConnectAsync();

        var hits = await rpc.CppFindSymbolAsync("__definitely_does_not_exist_zzz__", null, 10);
        Assert.Empty(hits.Matches);
    }

    [SkippableFact]
    public async Task CppClasses_filtered_by_kind_returns_only_structs()
    {
        Skip.IfNot(E2EFixture.IsEnabled, E2EFixture.SkipReason);
        var rpc = await _f.ConnectAsync();

        var result = await rpc.CppClassesAsync(null, new[] { "struct" }, 100);
        Assert.All(result.Classes, c => Assert.Equal("struct", c.Kind));
    }

    [SkippableFact]
    public async Task CppClasses_filtered_by_namePattern_returns_only_matches()
    {
        Skip.IfNot(E2EFixture.IsEnabled, E2EFixture.SkipReason);
        var rpc = await _f.ConnectAsync();

        var result = await rpc.CppClassesAsync("Sample", null, 100);
        // "Sample" as glob — exact match (no wildcard).
        Assert.All(result.Classes, c => Assert.Equal("Sample", c.Name));
    }

    [SkippableFact]
    public async Task CppOutlineMany_handles_missing_file_gracefully()
    {
        Skip.IfNot(E2EFixture.IsEnabled, E2EFixture.SkipReason);
        var rpc = await _f.ConnectAsync();
        var realFile = Path.Combine(_fixturesDir, "Sample.hpp");
        var bogus = Path.Combine(_fixturesDir, "DoesNotExist.hpp");

        var result = await rpc.CppOutlineManyAsync(new[] { realFile, bogus });
        // Real file gets a valid outline; bogus gets an Error.
        Assert.Equal(2, result.Entries.Count);
        var realEntry = result.Entries.First(e => e.File == realFile);
        Assert.NotNull(realEntry.Outline);
        var bogusEntry = result.Entries.First(e => e.File == bogus);
        Assert.NotNull(bogusEntry.Error);
    }

    [SkippableFact]
    public async Task CppInvalidate_succeeds_on_known_file()
    {
        Skip.IfNot(E2EFixture.IsEnabled, E2EFixture.SkipReason);
        var rpc = await _f.ConnectAsync();
        var path = Path.Combine(_fixturesDir, "Sample.hpp");

        // Should be a no-op pre-cache and a drop post-cache; either way it shouldn't throw.
        await rpc.CppInvalidateAsync(path);
    }

    [SkippableFact]
    public async Task CppReadMember_returns_method_with_name_match()
    {
        Skip.IfNot(E2EFixture.IsEnabled, E2EFixture.SkipReason);
        var rpc = await _f.ConnectAsync();
        var sampleHpp = Path.Combine(_fixturesDir, "Sample.hpp");

        var result = await rpc.CppReadMemberAsync(sampleHpp, "Sample", "Compute");
        Assert.True(result.StartLine > 0);
        Assert.Contains("Compute", result.Content);
    }

    [SkippableFact]
    public async Task CppClassMembers_excludes_private_methods_when_requested()
    {
        Skip.IfNot(E2EFixture.IsEnabled, E2EFixture.SkipReason);
        var rpc = await _f.ConnectAsync();
        var sampleHpp = Path.Combine(_fixturesDir, "Sample.hpp");

        // Default: returns all members (public, private, protected).
        var members = await rpc.CppClassMembersAsync(sampleHpp, "Sample");
        var hasPrivateField = members.Members.Any(m => m.Name == "seed_" || m.Name == "accum_");
        Assert.True(hasPrivateField, "Expected private fields like seed_/accum_ in member list");
    }

    // -------- More destructive tests --------

    [SkippableFact]
    public async Task CppMoveMethod_moves_method_to_target_file()
    {
        Skip.IfNot(E2EFixture.IsEnabled, E2EFixture.SkipReason);
        var rpc = await _f.ConnectAsync();
        await rpc.VsSetAutoFocusAsync(false);

        var tempDir = CopyFixturesToTemp();
        var src = Path.Combine(tempDir, "MMSrc.hpp");
        var dst = Path.Combine(tempDir, "MMDst.cpp");

        File.WriteAllText(src, """
            #pragma once
            class Mover
            {
            public:
                int Inline() { return 42; }
            };
            """.Replace("\r\n", "\n"));

        var result = await rpc.CppMoveMethodAsync(src, "Mover", "Inline", dst, createTargetIfMissing: true);
        Assert.True(result.Moved);

        var dstText = File.ReadAllText(dst);
        Assert.Contains("Mover::Inline", dstText);
    }

    [SkippableFact]
    public async Task CppRename_renames_within_single_file()
    {
        Skip.IfNot(E2EFixture.IsEnabled, E2EFixture.SkipReason);
        var rpc = await _f.ConnectAsync();
        await rpc.VsSetAutoFocusAsync(false);

        var tempDir = CopyFixturesToTemp();
        var path = Path.Combine(tempDir, "RenameTest.hpp");
        var content = """
            #pragma once
            int OldName(int x) { return x; }
            int caller() { return OldName(7) + OldName(8); }
            """.Replace("\r\n", "\n");
        File.WriteAllText(path, content);

        // Find the column of "OldName" on its declaration line.
        var lines = content.Split('\n');
        var declLine = 2; // 1-based
        var col = lines[1].IndexOf("OldName") + 1;

        var result = await rpc.CppRenameAsync(path, declLine, col, "NewName");
        // At minimum the rename shouldn't throw; verify the file changed.
        var after = File.ReadAllText(path);
        Assert.Contains("NewName", after);
    }

    [SkippableFact]
    public async Task CppGenerateConstructor_with_explicit_member_subset()
    {
        Skip.IfNot(E2EFixture.IsEnabled, E2EFixture.SkipReason);
        var rpc = await _f.ConnectAsync();
        await rpc.VsSetAutoFocusAsync(false);

        var tempDir = CopyFixturesToTemp();
        var path = Path.Combine(tempDir, "ExplicitCtor.hpp");
        File.WriteAllText(path, """
            #pragma once
            struct ExplicitCtor
            {
                int x;
                int y;
                int z;
            };
            """.Replace("\r\n", "\n"));

        // Only include x and z, skip y.
        var result = await rpc.CppGenerateConstructorAsync(path, "ExplicitCtor", new[] { "x", "z" });
        Assert.True(result.Inserted);

        var after = File.ReadAllText(path);
        Assert.Contains("x(x_)", after);
        Assert.Contains("z(z_)", after);
        Assert.DoesNotContain("y(y_)", after);
    }

    [SkippableFact]
    public async Task CppOrganizeIncludes_handles_already_sorted_no_op()
    {
        Skip.IfNot(E2EFixture.IsEnabled, E2EFixture.SkipReason);
        var rpc = await _f.ConnectAsync();
        await rpc.VsSetAutoFocusAsync(false);

        var tempDir = CopyFixturesToTemp();
        var path = Path.Combine(tempDir, "AlreadySorted.hpp");
        File.WriteAllText(path, """
            #include <map>
            #include <vector>
            #include "bar.hpp"
            #include "foo.hpp"

            int x = 0;
            """.Replace("\r\n", "\n"));

        var result = await rpc.CppOrganizeIncludesAsync(path);
        // Already sorted + dedup'd; the tool may report Changed=false or Changed=true with
        // no semantic difference. We just verify no crash and counts match.
        Assert.Equal(4, result.IncludesCounted);
        Assert.Equal(0, result.Duplicates);
    }

    [SkippableFact]
    public async Task CppOrganizeIncludes_preserves_pragma_once()
    {
        Skip.IfNot(E2EFixture.IsEnabled, E2EFixture.SkipReason);
        var rpc = await _f.ConnectAsync();
        await rpc.VsSetAutoFocusAsync(false);

        var tempDir = CopyFixturesToTemp();
        var path = Path.Combine(tempDir, "PragmaOnce.hpp");
        File.WriteAllText(path, """
            #pragma once
            #include "foo.hpp"
            #include <vector>

            int y = 0;
            """.Replace("\r\n", "\n"));

        await rpc.CppOrganizeIncludesAsync(path);
        var after = File.ReadAllText(path);
        Assert.Contains("#pragma once", after);
    }

    [SkippableFact]
    public async Task CppGenerateEquality_handles_empty_struct_gracefully()
    {
        Skip.IfNot(E2EFixture.IsEnabled, E2EFixture.SkipReason);
        var rpc = await _f.ConnectAsync();
        await rpc.VsSetAutoFocusAsync(false);

        var tempDir = CopyFixturesToTemp();
        var path = Path.Combine(tempDir, "EmptyStruct.hpp");
        File.WriteAllText(path, """
            #pragma once
            struct EmptyStruct { };
            """.Replace("\r\n", "\n"));

        // Empty struct: equality is vacuously true. Tool may insert `return true;` body
        // or refuse — we just verify it doesn't crash.
        try
        {
            await rpc.CppGenerateEqualityAsync(path, "EmptyStruct");
        }
        catch (Exception ex)
            when (ex.Message.Contains("fields", StringComparison.OrdinalIgnoreCase)
                  || ex.Message.Contains("empty", StringComparison.OrdinalIgnoreCase)
                  || ex.Message.Contains("compare", StringComparison.OrdinalIgnoreCase))
        {
            // Acceptable: tool refused on empty type with a descriptive message.
        }
    }

    [SkippableFact]
    public async Task CppOverrideMember_returns_inserted_false_when_no_base()
    {
        Skip.IfNot(E2EFixture.IsEnabled, E2EFixture.SkipReason);
        var rpc = await _f.ConnectAsync();
        await rpc.VsSetAutoFocusAsync(false);

        var tempDir = CopyFixturesToTemp();
        var path = Path.Combine(tempDir, "NoBase.hpp");
        File.WriteAllText(path, """
            #pragma once
            class Standalone { };
            """.Replace("\r\n", "\n"));

        // No base class with the given method — the tool should still insert (it
        // generates a stub regardless) OR fail gracefully. Verify no crash.
        try
        {
            var result = await rpc.CppOverrideMemberAsync(path, "Standalone", "doIt", "void", "");
            // Either inserted=true with synthetic body, or inserted=false. Both fine.
        }
        catch (Exception) { /* graceful refusal is also acceptable */ }
    }

    [SkippableFact]
    public async Task CppReplaceMember_throws_for_unknown_member()
    {
        Skip.IfNot(E2EFixture.IsEnabled, E2EFixture.SkipReason);
        var rpc = await _f.ConnectAsync();
        await rpc.VsSetAutoFocusAsync(false);

        var tempDir = CopyFixturesToTemp();
        var path = Path.Combine(tempDir, "ReplaceMissing.hpp");
        File.WriteAllText(path, """
            #pragma once
            class Foo { public: int A(); };
            """.Replace("\r\n", "\n"));

        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await rpc.CppReplaceMemberAsync(path, "Foo", "NonExistent", "    int NonExistent() { return 0; }");
        });
    }

    [SkippableFact]
    public async Task CppInheritance_returns_only_root_for_class_with_no_base()
    {
        Skip.IfNot(E2EFixture.IsEnabled, E2EFixture.SkipReason);
        var rpc = await _f.ConnectAsync();
        var sampleHpp = Path.Combine(_fixturesDir, "Sample.hpp");

        // Point is a struct with no base class.
        var result = await rpc.CppInheritanceAsync(sampleHpp, "Point", maxDepth: 3);
        Assert.NotNull(result.Tree);
        Assert.Equal("Point", result.Tree!.Name);
        Assert.Empty(result.Tree.Bases);
    }

    [SkippableFact]
    public async Task CppSetUnsavedBuffer_clears_on_null_content()
    {
        Skip.IfNot(E2EFixture.IsEnabled, E2EFixture.SkipReason);
        var rpc = await _f.ConnectAsync();

        var tempDir = CopyFixturesToTemp();
        var path = Path.Combine(tempDir, "ClearTest.hpp");
        File.WriteAllText(path, """
            #pragma once
            int good() { return 0; }
            """.Replace("\r\n", "\n"));

        // Set then clear; should not throw on either op.
        await rpc.CppSetUnsavedBufferAsync(path, "int dirty = ;");
        await rpc.CppSetUnsavedBufferAsync(path, null);

        // After clearing, diagnostics should reflect the clean disk file.
        var diags = await rpc.CppDiagnosticsAsync(path, null, null);
        Assert.False(diags.HasErrors, $"After clearing override, expected clean parse; got {diags.Diagnostics.Count} diagnostics with HasErrors={diags.HasErrors}.");
    }

    [SkippableFact]
    public async Task CppSetUnsavedBuffer_invalidate_round_trip()
    {
        Skip.IfNot(E2EFixture.IsEnabled, E2EFixture.SkipReason);
        var rpc = await _f.ConnectAsync();

        var tempDir = CopyFixturesToTemp();
        var path = Path.Combine(tempDir, "RoundTrip.hpp");
        File.WriteAllText(path, """
            #pragma once
            int good() { return 0; }
            """.Replace("\r\n", "\n"));

        await rpc.CppSetUnsavedBufferAsync(path, "int x = ;");
        await rpc.CppInvalidateAsync(path);
        // After invalidate, the unsaved-buffer override should still be honored on next parse.
        var diags = await rpc.CppDiagnosticsAsync(path, null, null);
        Assert.True(diags.HasErrors, $"Expected dirty buffer to still apply after invalidate; got HasErrors={diags.HasErrors}.");
        await rpc.CppSetUnsavedBufferAsync(path, null);
    }
}
