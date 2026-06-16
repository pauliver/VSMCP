using VSMCP.Core;
using VSMCP.Shared;
using Xunit;

namespace VSMCP.Tests.Vsix;

/// <summary>Covers the solution-wide C++ symbol index (#141).</summary>
public class CppSymbolIndexTests
{
    private static CppDecl D(string name, string kind, string? container = null, int line = 1)
        => new() { Name = name, Kind = kind, Container = container, Line = line };

    [Fact]
    public void Add_and_find_by_substring_case_insensitive()
    {
        var idx = new CppSymbolIndex();
        idx.AddOrReplaceFile("a.h", new[] { D("Renderer", "class"), D("draw", "method", "Renderer") });
        var hits = idx.Find("rend", null, 10, out var truncated);
        Assert.Single(hits);
        Assert.Equal("Renderer", hits[0].Name);
        Assert.Equal("a.h", hits[0].File);
        Assert.False(truncated);
    }

    [Fact]
    public void Kind_filter_applies()
    {
        var idx = new CppSymbolIndex();
        idx.AddOrReplaceFile("a.h", new[] { D("Foo", "class"), D("Foo", "function") });
        var hits = idx.Find("Foo", "function", 10, out _);
        Assert.Single(hits);
        Assert.Equal("function", hits[0].Kind);
    }

    [Fact]
    public void Replacing_a_file_updates_symbol_count()
    {
        var idx = new CppSymbolIndex();
        idx.AddOrReplaceFile("a.h", new[] { D("A", "class"), D("B", "class") });
        Assert.Equal(2, idx.SymbolCount);
        idx.AddOrReplaceFile("a.h", new[] { D("A", "class") });
        Assert.Equal(1, idx.SymbolCount);
        Assert.Equal(1, idx.FileCount);
    }

    [Fact]
    public void Remove_file_drops_its_symbols()
    {
        var idx = new CppSymbolIndex();
        idx.AddOrReplaceFile("a.h", new[] { D("A", "class") });
        idx.AddOrReplaceFile("b.h", new[] { D("B", "class") });
        Assert.True(idx.RemoveFile("a.h"));
        Assert.Equal(1, idx.FileCount);
        Assert.Empty(idx.Find("A", null, 10, out _));
    }

    [Fact]
    public void Truncates_at_max()
    {
        var idx = new CppSymbolIndex();
        idx.AddOrReplaceFile("a.h", new[] { D("X1", "class"), D("X2", "class"), D("X3", "class") });
        var hits = idx.Find("X", null, 2, out var truncated);
        Assert.Equal(2, hits.Count);
        Assert.True(truncated);
    }

    [Fact]
    public void Empty_name_matches_all()
    {
        var idx = new CppSymbolIndex();
        idx.AddOrReplaceFile("a.h", new[] { D("A", "class"), D("B", "enum") });
        Assert.Equal(2, idx.Find("", null, 10, out _).Count);
    }
}
