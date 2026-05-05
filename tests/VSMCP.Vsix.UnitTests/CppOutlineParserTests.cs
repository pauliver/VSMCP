using System.IO;
using System.Linq;
using VSMCP.Vsix;
using Xunit;

namespace VSMCP.Tests.Vsix;

/// <summary>
/// Unit tests for CppOutlineParser. The parser is regex-based and has historically had
/// subtle bugs around multi-line declarations, multi-modifier functions, and templates.
/// These tests exercise the cases real codebases hit so the bugs don't recur.
/// </summary>
public sealed class CppOutlineParserTests
{
    private static string WriteToTemp(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"vsmcp-outline-{System.Guid.NewGuid():N}.hpp");
        File.WriteAllText(path, content.Replace("\r\n", "\n"));
        return path;
    }

    [Fact]
    public void Parse_finds_class_with_brace_on_same_line()
    {
        var path = WriteToTemp("""
            class Foo {
                int x;
            };
            """);
        try
        {
            var outline = CppOutlineParser.Parse(path);
            Assert.Contains(outline.Declarations, d => d.Kind == "class" && d.Name == "Foo");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Parse_finds_class_with_brace_on_next_line()
    {
        // Regression test: pre-fix, the regex required '{' on the same line as 'class'.
        var path = WriteToTemp("""
            class Foo
            {
                int x;
            };
            """);
        try
        {
            var outline = CppOutlineParser.Parse(path);
            Assert.Contains(outline.Declarations, d => d.Kind == "class" && d.Name == "Foo");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Parse_finds_class_with_inheritance_on_separate_lines()
    {
        // Regression test: real-world multi-line declaration.
        var path = WriteToTemp("""
            class Derived : public Base
            {
                int x;
            };
            """);
        try
        {
            var outline = CppOutlineParser.Parse(path);
            Assert.Contains(outline.Declarations, d => d.Kind == "class" && d.Name == "Derived");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Parse_finds_struct_and_enum()
    {
        var path = WriteToTemp("""
            struct Point { int x; int y; };
            enum class Color : int { Red, Green, Blue };
            """);
        try
        {
            var outline = CppOutlineParser.Parse(path);
            Assert.Contains(outline.Declarations, d => d.Kind == "struct" && d.Name == "Point");
            Assert.Contains(outline.Declarations, d => d.Kind == "enum" && d.Name == "Color");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Parse_finds_method_with_chained_modifiers()
    {
        // Regression test: 'const noexcept' chained — the trailing-modifier group used to
        // accept only one modifier.
        var path = WriteToTemp("""
            class Foo {
            public:
                int Multiply(int a, int b) const noexcept;
            };
            """);
        try
        {
            var outline = CppOutlineParser.Parse(path);
            Assert.Contains(outline.Declarations, d => d.Kind == "method" && d.Name == "Multiply");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Parse_assigns_container_for_methods_in_namespaces()
    {
        var path = WriteToTemp("""
            namespace Outer {
                class Foo {
                public:
                    void Bar();
                };
            }
            """);
        try
        {
            var outline = CppOutlineParser.Parse(path);
            var bar = outline.Declarations.FirstOrDefault(d => d.Name == "Bar");
            Assert.NotNull(bar);
            Assert.Equal("Outer::Foo", bar!.Container);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Parse_handles_namespace_block()
    {
        var path = WriteToTemp("""
            namespace Detail {
                class Helper { };
            }
            """);
        try
        {
            var outline = CppOutlineParser.Parse(path);
            Assert.Contains(outline.Declarations, d => d.Kind == "namespace" && d.Name == "Detail");
            Assert.Contains(outline.Declarations, d => d.Kind == "class" && d.Name == "Helper");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Parse_handles_pure_virtual()
    {
        var path = WriteToTemp("""
            class IFoo {
            public:
                virtual int Compute(int x) = 0;
                virtual ~IFoo() = default;
            };
            """);
        try
        {
            var outline = CppOutlineParser.Parse(path);
            Assert.Contains(outline.Declarations, d => d.Kind == "method" && d.Name == "Compute");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Parse_throws_for_nonexistent_file()
    {
        // Behavior contract: callers can wrap to map this to a tool-level NotFound error.
        Assert.Throws<VsmcpException>(() =>
            CppOutlineParser.Parse(Path.Combine(Path.GetTempPath(), $"nonexistent-{System.Guid.NewGuid():N}.hpp")));
    }

    [Fact]
    public void Parse_skips_string_literals_with_braces()
    {
        // Stress test: string literals shouldn't push the brace-depth tracker. This is a
        // common real-world case in code that builds JSON or shell commands.
        var path = WriteToTemp("""
            class Foo {
            public:
                const char* GetJson() { return "{\"key\": \"value\"}"; }
            };
            class Bar { int x; };
            """);
        try
        {
            var outline = CppOutlineParser.Parse(path);
            Assert.Contains(outline.Declarations, d => d.Kind == "class" && d.Name == "Foo");
            Assert.Contains(outline.Declarations, d => d.Kind == "class" && d.Name == "Bar");
        }
        finally { File.Delete(path); }
    }
}
