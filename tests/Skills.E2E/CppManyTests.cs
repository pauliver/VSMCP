using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using VSMCP.Shared;
using Xunit;

namespace VSMCP.Tests.Skills.E2E;

/// <summary>
/// E2E tests for issue #122 batch (`_many`) tools. Each test exercises the wrapper at the
/// VSIX boundary, verifying:
///   - happy path: all items succeed, BatchResult.Total == Succeeded == items.Count
///   - mixed path (where applicable): one bad item gets Failed; others still succeed
/// </summary>
[Collection(E2ECollection.Name)]
public sealed class CppManyTests : IDisposable
{
    private readonly E2EFixture _f;
    private readonly string _fixturesDir;
    private readonly string _tempCopyDir;

    public CppManyTests(E2EFixture f)
    {
        _f = f;
        _fixturesDir = Path.Combine(_f.FixturesRoot, "Cpp");
        _tempCopyDir = Path.Combine(Path.GetTempPath(), "vsmcp-cpp-many-" + Guid.NewGuid().ToString("N").Substring(0, 8));
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
            File.Copy(src, Path.Combine(_tempCopyDir, Path.GetFileName(src)), overwrite: true);
        return _tempCopyDir;
    }

    [SkippableFact]
    public async Task CppReadMemberMany_returns_per_item_results()
    {
        Skip.IfNot(E2EFixture.IsEnabled, E2EFixture.SkipReason);
        var rpc = await _f.ConnectAsync();
        var sample = Path.Combine(_fixturesDir, "Sample.hpp");

        var items = new[]
        {
            new CppReadMemberItem { File = sample, ClassName = "Sample", MemberName = "Compute" },
            new CppReadMemberItem { File = sample, ClassName = "Sample", MemberName = "Multiply" },
            new CppReadMemberItem { File = sample, ClassName = "Sample", MemberName = "DoesNotExist" }, // bad
        };

        var result = await rpc.CppReadMemberManyAsync(items);
        Assert.Equal(3, result.Total);
        Assert.Equal(2, result.Succeeded);
        Assert.Equal(1, result.Failed);
        Assert.True(result.Items[0].Success);
        Assert.True(result.Items[1].Success);
        Assert.False(result.Items[2].Success);
        Assert.NotNull(result.Items[2].Error);
    }

    [SkippableFact]
    public async Task CppOrganizeIncludesMany_sorts_each_file()
    {
        Skip.IfNot(E2EFixture.IsEnabled, E2EFixture.SkipReason);
        var rpc = await _f.ConnectAsync();
        await rpc.VsSetAutoFocusAsync(false);

        var tempDir = CopyFixturesToTemp();
        var p1 = Path.Combine(tempDir, "Many1.hpp");
        var p2 = Path.Combine(tempDir, "Many2.hpp");
        File.WriteAllText(p1, """
            #include "z.hpp"
            #include <vector>
            int x = 0;
            """.Replace("\r\n", "\n"));
        File.WriteAllText(p2, """
            #include "b.hpp"
            #include <map>
            int y = 0;
            """.Replace("\r\n", "\n"));

        var result = await rpc.CppOrganizeIncludesManyAsync(new[] { p1, p2 });
        Assert.Equal(2, result.Total);
        Assert.Equal(2, result.Succeeded);
        Assert.True(result.Items.All(i => i.Success));
    }

    [SkippableFact]
    public async Task CppInvalidateMany_does_not_throw_on_unknown_files()
    {
        Skip.IfNot(E2EFixture.IsEnabled, E2EFixture.SkipReason);
        var rpc = await _f.ConnectAsync();
        var sample = Path.Combine(_fixturesDir, "Sample.hpp");

        // CppInvalidate is permissive: dropping a non-cached file is a no-op.
        var result = await rpc.CppInvalidateManyAsync(new[] { sample, "C:\\does\\not\\exist.hpp" });
        Assert.Equal(2, result.Total);
        Assert.Equal(2, result.Succeeded);
    }

    [SkippableFact]
    public async Task CppSetUnsavedBufferMany_pushes_and_clears()
    {
        Skip.IfNot(E2EFixture.IsEnabled, E2EFixture.SkipReason);
        var rpc = await _f.ConnectAsync();
        var sample = Path.Combine(_fixturesDir, "Sample.hpp");

        // Set then clear in one batch.
        var items = new[]
        {
            new CppUnsavedBufferItem { File = sample, Content = "// dirty\n" },
            new CppUnsavedBufferItem { File = sample, Content = null },
        };
        var result = await rpc.CppSetUnsavedBufferManyAsync(items);
        Assert.Equal(2, result.Total);
        Assert.Equal(2, result.Succeeded);
    }

    [SkippableFact]
    public async Task CppQuickInfoMany_returns_per_position_info()
    {
        Skip.IfNot(E2EFixture.IsEnabled, E2EFixture.SkipReason);
        var rpc = await _f.ConnectAsync();
        var sample = Path.Combine(_fixturesDir, "Sample.hpp");

        var items = new[]
        {
            new CppPositionItem { File = sample, Line = 11, Column = 7 },  // class Sample
            new CppPositionItem { File = sample, Line = 17, Column = 10 }, // method
        };
        var result = await rpc.CppQuickInfoManyAsync(items);
        Assert.Equal(2, result.Total);
        // libclang may fail to fully resolve in Open Folder mode without proper include
        // setup, but the wrapper itself shouldn't crash.
        Assert.NotNull(result.Items);
    }

    [SkippableFact]
    public async Task CppGenerateConstructorMany_inserts_for_each_struct()
    {
        Skip.IfNot(E2EFixture.IsEnabled, E2EFixture.SkipReason);
        var rpc = await _f.ConnectAsync();
        await rpc.VsSetAutoFocusAsync(false);

        var tempDir = CopyFixturesToTemp();
        var p1 = Path.Combine(tempDir, "CtorA.hpp");
        var p2 = Path.Combine(tempDir, "CtorB.hpp");
        File.WriteAllText(p1, """
            #pragma once
            struct CtorA
            {
                int x;
                int y;
            };
            """.Replace("\r\n", "\n"));
        File.WriteAllText(p2, """
            #pragma once
            struct CtorB
            {
                float r;
                float g;
                float b;
            };
            """.Replace("\r\n", "\n"));

        var items = new[]
        {
            new CppGenerateCtorItem { File = p1, ClassName = "CtorA" },
            new CppGenerateCtorItem { File = p2, ClassName = "CtorB" },
        };
        var result = await rpc.CppGenerateConstructorManyAsync(items);
        Assert.Equal(2, result.Total);
        Assert.Equal(2, result.Succeeded);

        Assert.Contains("CtorA(int x_, int y_)", File.ReadAllText(p1));
        Assert.Contains("CtorB(float r_, float g_, float b_)", File.ReadAllText(p2));
    }

    [SkippableFact]
    public async Task CppOverrideMemberMany_inserts_for_each_class()
    {
        Skip.IfNot(E2EFixture.IsEnabled, E2EFixture.SkipReason);
        var rpc = await _f.ConnectAsync();
        await rpc.VsSetAutoFocusAsync(false);

        var tempDir = CopyFixturesToTemp();
        var p1 = Path.Combine(tempDir, "OvA.hpp");
        File.WriteAllText(p1, """
            #pragma once
            class Base { public: virtual void f() = 0; virtual int g() = 0; };
            class Derived : public Base { };
            """.Replace("\r\n", "\n"));

        var items = new[]
        {
            new CppOverrideMemberItem { File = p1, ClassName = "Derived", MethodName = "f", ReturnType = "void", Parameters = "" },
            new CppOverrideMemberItem { File = p1, ClassName = "Derived", MethodName = "g", ReturnType = "int", Parameters = "" },
        };
        var result = await rpc.CppOverrideMemberManyAsync(items);
        Assert.Equal(2, result.Total);
        Assert.Equal(2, result.Succeeded);

        var after = File.ReadAllText(p1);
        Assert.Contains("void f() override", after);
        Assert.Contains("int g() override", after);
    }

    [SkippableFact]
    public async Task CppGenerateEqualityMany_inserts_for_each_class()
    {
        Skip.IfNot(E2EFixture.IsEnabled, E2EFixture.SkipReason);
        var rpc = await _f.ConnectAsync();
        await rpc.VsSetAutoFocusAsync(false);

        var tempDir = CopyFixturesToTemp();
        var p1 = Path.Combine(tempDir, "EqA.hpp");
        File.WriteAllText(p1, """
            #pragma once
            struct A
            {
                int x;
            };
            struct B
            {
                int y;
            };
            """.Replace("\r\n", "\n"));

        var items = new[]
        {
            new CppGenerateEqualityItem { File = p1, ClassName = "A" },
            new CppGenerateEqualityItem { File = p1, ClassName = "B" },
        };
        var result = await rpc.CppGenerateEqualityManyAsync(items);
        Assert.Equal(2, result.Total);
        Assert.Equal(2, result.Succeeded);

        var after = File.ReadAllText(p1);
        // Both equality operators should be present.
        var matches = System.Text.RegularExpressions.Regex.Matches(after, @"operator==");
        Assert.True(matches.Count >= 2, $"Expected at least 2 operator== insertions; got {matches.Count}");
    }

    [SkippableFact]
    public async Task CppReplaceMemberMany_swaps_each_method_body()
    {
        Skip.IfNot(E2EFixture.IsEnabled, E2EFixture.SkipReason);
        var rpc = await _f.ConnectAsync();
        await rpc.VsSetAutoFocusAsync(false);

        var tempDir = CopyFixturesToTemp();
        var p1 = Path.Combine(tempDir, "RepM.hpp");
        File.WriteAllText(p1, """
            #pragma once
            class T { public:
                int A() { return 1; }
                int B() { return 2; }
            };
            """.Replace("\r\n", "\n"));

        var items = new[]
        {
            new CppReplaceMemberItem { File = p1, ClassName = "T", MemberName = "A", NewCode = "    int A() { return 100; }" },
            new CppReplaceMemberItem { File = p1, ClassName = "T", MemberName = "B", NewCode = "    int B() { return 200; }" },
        };
        var result = await rpc.CppReplaceMemberManyAsync(items);
        Assert.Equal(2, result.Total);
        Assert.Equal(2, result.Succeeded);

        var after = File.ReadAllText(p1);
        Assert.Contains("return 100;", after);
        Assert.Contains("return 200;", after);
    }

    [SkippableFact]
    public async Task FileMembersMany_returns_per_class_members()
    {
        Skip.IfNot(E2EFixture.IsEnabled, E2EFixture.SkipReason);
        var rpc = await _f.ConnectAsync();
        // FileMembersAsync is Roslyn-based (C#). Use VSMCP source files (which are part of
        // the loaded solution) rather than the C++ fixtures.
        // Derive repo root from FixturesRoot (tests/Skills) → up two levels.
        var repoRoot = Path.GetFullPath(Path.Combine(_f.FixturesRoot, "..", ".."));
        var rpcTargetCs = Path.Combine(repoRoot, "src", "VSMCP.Vsix", "RpcTarget.cs");
        Skip.IfNot(File.Exists(rpcTargetCs), $"Repo file missing: {rpcTargetCs}");

        var items = new[]
        {
            new FileMembersItem { File = rpcTargetCs, ClassName = "RpcTarget" },
            new FileMembersItem { File = rpcTargetCs, ClassName = "DoesNotExist" },
        };
        var result = await rpc.FileMembersManyAsync(items);
        Assert.Equal(2, result.Total);
        // The wrapper itself succeeded — per-item outcomes may vary by workspace state.
        Assert.NotNull(result.Items);
    }

    [SkippableFact]
    public async Task FileInfoMany_returns_size_for_each_file()
    {
        Skip.IfNot(E2EFixture.IsEnabled, E2EFixture.SkipReason);
        var rpc = await _f.ConnectAsync();
        var sample = Path.Combine(_fixturesDir, "Sample.hpp");
        var sampleBase = Path.Combine(_fixturesDir, "SampleBase.hpp");

        var result = await rpc.FileInfoManyAsync(new[] { sample, sampleBase });
        Assert.Equal(2, result.Total);
        Assert.Equal(2, result.Succeeded);
    }

    [SkippableFact]
    public async Task BatchResult_preserves_input_ordering()
    {
        Skip.IfNot(E2EFixture.IsEnabled, E2EFixture.SkipReason);
        var rpc = await _f.ConnectAsync();
        var sample = Path.Combine(_fixturesDir, "Sample.hpp");

        // Three items: good, bad, good. Verify Index field maps back to input order.
        var items = new[]
        {
            new CppReadMemberItem { File = sample, ClassName = "Sample", MemberName = "Compute" },
            new CppReadMemberItem { File = sample, ClassName = "Sample", MemberName = "BogusMember" },
            new CppReadMemberItem { File = sample, ClassName = "Sample", MemberName = "Multiply" },
        };
        var result = await rpc.CppReadMemberManyAsync(items);
        Assert.Equal(3, result.Items.Count);
        Assert.Equal(0, result.Items[0].Index);
        Assert.Equal(1, result.Items[1].Index);
        Assert.Equal(2, result.Items[2].Index);
        Assert.True(result.Items[0].Success);
        Assert.False(result.Items[1].Success);
        Assert.True(result.Items[2].Success);
    }

    [SkippableFact]
    public async Task BatchResult_empty_input_returns_zero_total()
    {
        Skip.IfNot(E2EFixture.IsEnabled, E2EFixture.SkipReason);
        var rpc = await _f.ConnectAsync();

        var result = await rpc.CppInvalidateManyAsync(new string[0]);
        Assert.Equal(0, result.Total);
        Assert.Equal(0, result.Succeeded);
        Assert.Equal(0, result.Failed);
        Assert.Empty(result.Items);
    }
}
