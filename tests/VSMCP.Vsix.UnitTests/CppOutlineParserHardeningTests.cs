using System;
using System.IO;
using System.Linq;
using VSMCP.Core;
using Xunit;

namespace VSMCP.Tests.Vsix;

/// <summary>
/// Regression tests for #132: the lexer is now string-aware. Braces, "//", and quotes that
/// live inside string / char / raw-string literals must not corrupt comment-stripping or
/// brace-depth tracking. Each case is constructed to FAIL against the pre-#132 parser, which
/// counted braces inside literals and treated "//" inside a string as a comment.
/// </summary>
public sealed class CppOutlineParserHardeningTests
{
    private static string WriteToTemp(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"vsmcp-harden-{Guid.NewGuid():N}.hpp");
        File.WriteAllText(path, content.Replace("\r\n", "\n"));
        return path;
    }

    [Fact]
    public void StringLiteral_with_closing_brace_does_not_pop_class_scope()
    {
        // Pre-fix: the '}' inside the string dropped braceDepth, popping class C, so Method
        // was emitted as a file-scope function instead of a method of C.
        var path = WriteToTemp("""
            class C {
                const char* close = "}";
                void Method();
            };
            """);
        try
        {
            var m = CppOutlineParser.Parse(path).Declarations.Single(d => d.Name == "Method");
            Assert.Equal("method", m.Kind);
            Assert.Equal("C", m.Container);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void CharLiteral_with_brace_does_not_corrupt_depth()
    {
        var path = WriteToTemp("""
            class C {
                char open = '{';
                void Method();
            };
            """);
        try
        {
            var m = CppOutlineParser.Parse(path).Declarations.Single(d => d.Name == "Method");
            Assert.Equal("method", m.Kind);
            Assert.Equal("C", m.Container);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Multiline_raw_string_with_braces_does_not_corrupt_scope()
    {
        // The raw string spans 3 lines and contains stray '}' that pre-fix would have counted,
        // collapsing every enclosing scope.
        var path = WriteToTemp(""""
            class C {
                const char* blob = R"(
                    } } } not real closers
                )";
                void Method();
            };
            """");
        try
        {
            var m = CppOutlineParser.Parse(path).Declarations.Single(d => d.Name == "Method");
            Assert.Equal("method", m.Kind);
            Assert.Equal("C", m.Container);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void RawString_with_double_slash_is_not_treated_as_comment()
    {
        // Pre-fix StripComments saw "//" inside the raw string and truncated the line.
        var path = WriteToTemp("""
            namespace ns {
                const char* url = R"(http://example.com/{x})";
                class AfterUrl { int v; };
            }
            """);
        try
        {
            var c = CppOutlineParser.Parse(path).Declarations.Single(d => d.Name == "AfterUrl");
            Assert.Equal("class", c.Kind);
            Assert.Equal("ns", c.Container);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void SingleLine_class_with_nested_default_template_arg_is_found()
    {
        // Pre-fix the template prefix used <[^>]+> and closed at the first '>', so the class
        // name was never reached when a default arg nested another template.
        var path = WriteToTemp("""
            template<class T, class A = std::allocator<T>> class Vec { int n; };
            """);
        try
        {
            Assert.Contains(CppOutlineParser.Parse(path).Declarations, d => d.Kind == "class" && d.Name == "Vec");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void BlockComment_with_brace_still_ignored()
    {
        // Guards the existing block-comment path through the rewritten Sanitize().
        var path = WriteToTemp("""
            class C {
                /* } stray brace in comment } */
                void Method();
            };
            """);
        try
        {
            var m = CppOutlineParser.Parse(path).Declarations.Single(d => d.Name == "Method");
            Assert.Equal("C", m.Container);
        }
        finally { File.Delete(path); }
    }
}
