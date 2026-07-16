using System;
using VSMCP.Core;
using VSMCP.Shared;
using Xunit;

namespace VSMCP.Tests.Vsix;

/// <summary>
/// Locks the write-confinement boundary (audit through-line: data safety). The load-bearing case is
/// the sibling-prefix guard: a root of <c>C:\Repo</c> must NOT accept <c>C:\RepoEvil\...</c>, and a
/// <c>..</c> escape must resolve out of the root before the check.
/// </summary>
public class WriteScopePolicyTests
{
    [Theory]
    [InlineData(@"C:\Repo", @"C:\Repo\src\a.cs", true)]      // descendant
    [InlineData(@"C:\Repo", @"C:\Repo", true)]               // the root itself
    [InlineData(@"C:\Repo\", @"C:\Repo\a.cs", true)]         // trailing separator on root
    [InlineData(@"C:\Repo", @"c:\repo\A.CS", true)]          // case-insensitive
    [InlineData(@"C:\Repo", @"C:\RepoEvil\a.cs", false)]     // sibling-prefix bypass
    [InlineData(@"C:\Repo", @"C:\Other\a.cs", false)]        // unrelated tree
    [InlineData(@"C:\Repo", @"C:\Repo\..\Other\a.cs", false)] // .. escape resolves outside
    [InlineData(@"C:\Repo\sub", @"C:\Repo\sub\..\..\x.cs", false)]
    public void IsWithinRoot_enforces_boundary(string root, string candidate, bool expected)
        => Assert.Equal(expected, WriteScopePolicy.IsWithinRoot(root, candidate));

    [Fact]
    public void IsWithinAnyRoot_accepts_when_any_root_contains()
    {
        var roots = new[] { @"C:\A", @"C:\B" };
        Assert.True(WriteScopePolicy.IsWithinAnyRoot(roots, @"C:\B\deep\x.cs"));
        Assert.False(WriteScopePolicy.IsWithinAnyRoot(roots, @"C:\C\x.cs"));
    }

    [Fact]
    public void EnsureWithinAnyRoot_returns_full_path_when_inside()
    {
        var full = WriteScopePolicy.EnsureWithinAnyRoot(new[] { @"C:\Repo" }, @"C:\Repo\src\..\a.cs", "file.write");
        Assert.Equal(@"C:\Repo\a.cs", full);
    }

    [Fact]
    public void EnsureWithinAnyRoot_throws_WrongState_when_outside()
    {
        var ex = Assert.Throws<VsmcpException>(
            () => WriteScopePolicy.EnsureWithinAnyRoot(new[] { @"C:\Repo" }, @"C:\Windows\System32\evil.dll", "file.write"));
        Assert.Equal(ErrorCodes.WrongState, ex.Code);
        Assert.Contains("file.write", ex.Message);
    }

    [Fact]
    public void EnsureWithinAnyRoot_throws_on_empty_candidate()
        => Assert.Throws<VsmcpException>(
            () => WriteScopePolicy.EnsureWithinAnyRoot(new[] { @"C:\Repo" }, "", "file.write"));
}
