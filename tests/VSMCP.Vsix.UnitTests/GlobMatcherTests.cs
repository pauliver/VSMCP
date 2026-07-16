using VSMCP.Core;
using Xunit;

namespace VSMCP.Tests.Vsix;

/// <summary>
/// Locks the glob semantics shared by file.list / file.glob / search.text (extracted from
/// RpcTarget.FileDiscovery). The key fix: a bare pattern ("*.cs", "Foo.cs") matches by FILE NAME
/// anywhere in the tree — file.list/search.text previously matched the full path only and returned
/// empty for bare patterns.
/// </summary>
public class GlobMatcherTests
{
    [Theory]
    // bare patterns match by file name in any subdirectory (the file.list/search.text fix)
    [InlineData(@"P:\repo\src\sub\Foo.cs", "*.cs", true)]
    [InlineData(@"P:\repo\src\sub\Foo.cs", "Foo.cs", true)]
    [InlineData("src/sub/Foo.cs", "*.cs", true)]
    [InlineData(@"P:\repo\src\sub\Foo.txt", "*.cs", false)]
    // recursive ** crosses separators on the full path
    [InlineData(@"P:\repo\src\Foo.cs", "**/*.cs", true)]
    [InlineData("a/b/c/Bar.cs", "**/*.cs", true)]
    // single * does NOT cross separators in a path-anchored pattern
    [InlineData("a/b/Foo.cs", "a/*.cs", false)]
    [InlineData("a/Foo.cs", "a/*.cs", true)]
    // {alt} alternation
    [InlineData("x/Foo.ts", "*.{ts,tsx}", true)]
    [InlineData("x/Foo.tsx", "*.{ts,tsx}", true)]
    [InlineData("x/Foo.cs", "*.{ts,tsx}", false)]
    // ? single non-separator char
    [InlineData("x/Fo.cs", "F?.cs", true)]
    [InlineData("x/Foo.cs", "F?.cs", false)]
    public void MatchesPathOrName_matches_expected(string path, string pattern, bool expected)
        => Assert.Equal(expected, GlobMatcher.MatchesPathOrName(path, pattern));

    [Fact]
    public void ToRegex_is_anchored_and_case_insensitive()
    {
        var rx = GlobMatcher.ToRegex("*.cs");
        Assert.Matches(rx, "Foo.CS");          // case-insensitive
        Assert.Matches(rx, "Foo.cs");
        Assert.DoesNotMatch(rx, "a/Foo.cs");   // anchored: bare * does not cross separators
    }

    [Fact]
    public void MatchesPathOrName_precompiled_overload_matches_string_overload()
    {
        // Scanners must compile once and reuse this overload (per-file recompile is quadratic-ish
        // on UE-sized solutions). It must agree with the convenience string overload.
        var rx = GlobMatcher.ToRegex("*.cs");
        foreach (var p in new[] { @"P:\repo\src\Foo.cs", "a/b/Bar.cs", "x/Foo.txt" })
            Assert.Equal(GlobMatcher.MatchesPathOrName(p, "*.cs"), GlobMatcher.MatchesPathOrName(p, rx));
    }
}
