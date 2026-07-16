using VSMCP.Core;
using Xunit;

namespace VSMCP.Tests.Vsix;

/// <summary>
/// Locks the C++ member-end scan that drives cpp_replace_member / cpp_move_*. The load-bearing case
/// is a brace inside a string literal: the old naive counter miscounted it and returned an arbitrary
/// line, silently corrupting the file. Balancing over Sanitize()d text fixes that.
/// </summary>
public class CppMemberBoundaryTests
{
    [Fact]
    public void Brace_inside_string_literal_does_not_close_the_member()
    {
        var lines = new[]
        {
            "void f() {",
            "    const char* s = \"}\";", // this } must NOT close the body
            "    g();",
            "}",
            "int next;",
        };
        Assert.Equal(3, CppMemberBoundary.FindEndLine(lines, 0));
    }

    [Fact]
    public void Brace_inside_block_comment_is_ignored()
    {
        var lines = new[]
        {
            "struct S {",
            "    /* } not a real close */",
            "    int x;",
            "};",
        };
        Assert.Equal(3, CppMemberBoundary.FindEndLine(lines, 0));
    }

    [Fact]
    public void Semicolon_terminates_a_bodyless_declaration()
        => Assert.Equal(0, CppMemberBoundary.FindEndLine(new[] { "int x;" }, 0));

    [Fact]
    public void Unbalanced_returns_negative_one()
        => Assert.Equal(-1, CppMemberBoundary.FindEndLine(new[] { "void f() {", "    g();" }, 0));
}
