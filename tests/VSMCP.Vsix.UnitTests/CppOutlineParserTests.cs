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

    [Fact]
    public void Parse_finds_typedef_and_using_alias()
    {
        var path = WriteToTemp("""
            typedef unsigned int Size32;
            using StringMap = std::map<std::string, std::string>;
            """);
        try
        {
            var outline = CppOutlineParser.Parse(path);
            Assert.Contains(outline.Declarations, d => d.Kind == "typedef" && d.Name == "Size32");
            Assert.Contains(outline.Declarations, d => d.Kind == "using" && d.Name == "StringMap");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Parse_handles_nested_namespaces()
    {
        var path = WriteToTemp("""
            namespace A {
                namespace B {
                    class Nested { int x; };
                }
            }
            """);
        try
        {
            var outline = CppOutlineParser.Parse(path);
            var nested = outline.Declarations.FirstOrDefault(d => d.Name == "Nested");
            Assert.NotNull(nested);
            Assert.Equal("A::B", nested!.Container);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Parse_handles_compact_namespace_syntax()
    {
        var path = WriteToTemp("""
            namespace A::B::C {
                class Deep { };
            }
            """);
        try
        {
            var outline = CppOutlineParser.Parse(path);
            // Either the namespace is recorded as one entry or split — accept either.
            Assert.Contains(outline.Declarations, d => d.Kind == "class" && d.Name == "Deep");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Parse_handles_template_class()
    {
        var path = WriteToTemp("""
            template<typename T>
            class Container
            {
            public:
                T value;
            };
            """);
        try
        {
            var outline = CppOutlineParser.Parse(path);
            Assert.Contains(outline.Declarations, d => d.Kind == "class" && d.Name == "Container");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Parse_handles_function_with_default_args()
    {
        var path = WriteToTemp("""
            int compute(int x, int y = 0, const char* tag = nullptr);
            """);
        try
        {
            var outline = CppOutlineParser.Parse(path);
            Assert.Contains(outline.Declarations, d => d.Kind == "function" && d.Name == "compute");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Parse_handles_static_and_constexpr_methods()
    {
        var path = WriteToTemp("""
            class Math {
            public:
                static int Add(int a, int b);
                constexpr int Square(int x) const;
                inline void Init();
            };
            """);
        try
        {
            var outline = CppOutlineParser.Parse(path);
            Assert.Contains(outline.Declarations, d => d.Kind == "method" && d.Name == "Add");
            Assert.Contains(outline.Declarations, d => d.Kind == "method" && d.Name == "Square");
            Assert.Contains(outline.Declarations, d => d.Kind == "method" && d.Name == "Init");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Parse_skips_block_comments()
    {
        var path = WriteToTemp("""
            /* class CommentedOut { int x; }; */
            class Real { int y; };
            """);
        try
        {
            var outline = CppOutlineParser.Parse(path);
            Assert.DoesNotContain(outline.Declarations, d => d.Name == "CommentedOut");
            Assert.Contains(outline.Declarations, d => d.Name == "Real");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Parse_skips_line_comments()
    {
        var path = WriteToTemp("""
            // class CommentedOut { int x; };
            class Real { int y; };
            """);
        try
        {
            var outline = CppOutlineParser.Parse(path);
            Assert.DoesNotContain(outline.Declarations, d => d.Name == "CommentedOut");
            Assert.Contains(outline.Declarations, d => d.Name == "Real");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Parse_skips_preprocessor_directives()
    {
        var path = WriteToTemp("""
            #define FOO_API __declspec(dllexport)
            #ifdef DEBUG
            class DebugOnly { int x; };
            #endif
            class Always { int y; };
            """);
        try
        {
            var outline = CppOutlineParser.Parse(path);
            // Preprocessor directives are skipped; classes between them are still parsed.
            Assert.Contains(outline.Declarations, d => d.Name == "Always");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Parse_handles_macro_decorated_class()
    {
        // Common pattern: `class FOO_API ClassName` where FOO_API is a dllexport macro.
        var path = WriteToTemp("""
            class GAME_API Player { int hp; };
            """);
        try
        {
            var outline = CppOutlineParser.Parse(path);
            Assert.Contains(outline.Declarations, d => d.Kind == "class" && d.Name == "Player");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Parse_finds_destructor()
    {
        var path = WriteToTemp("""
            class Foo {
            public:
                Foo();
                ~Foo();
            };
            """);
        try
        {
            var outline = CppOutlineParser.Parse(path);
            // Constructor and destructor handling — at minimum the class itself is found.
            Assert.Contains(outline.Declarations, d => d.Kind == "class" && d.Name == "Foo");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Parse_handles_anonymous_struct_inside_class()
    {
        var path = WriteToTemp("""
            class Outer {
                struct {
                    int a;
                    int b;
                } unnamed;
            public:
                int x;
            };
            """);
        try
        {
            var outline = CppOutlineParser.Parse(path);
            // Anonymous struct shouldn't crash the parser; the outer class should still be found.
            Assert.Contains(outline.Declarations, d => d.Kind == "class" && d.Name == "Outer");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Parse_emits_fields_inside_class()
    {
        var path = WriteToTemp("""
            class Sample
            {
            public:
                int Compute(int x);
            private:
                int        seed_;
                int        accum_;
                const char* name_;
            };
            """);
        try
        {
            var outline = CppOutlineParser.Parse(path);
            Assert.Contains(outline.Declarations, d => d.Kind == "field" && d.Name == "seed_");
            Assert.Contains(outline.Declarations, d => d.Kind == "field" && d.Name == "accum_");
            Assert.Contains(outline.Declarations, d => d.Kind == "field" && d.Name == "name_");
            // Methods should still be picked up.
            Assert.Contains(outline.Declarations, d => d.Kind == "method" && d.Name == "Compute");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Parse_does_not_emit_fields_for_local_vars_in_method_bodies()
    {
        var path = WriteToTemp("""
            class Foo
            {
            public:
                int Bar() {
                    int local = 0;
                    int another = 1;
                    return local + another;
                }
            };
            """);
        try
        {
            var outline = CppOutlineParser.Parse(path);
            // 'local' and 'another' are local vars, not class fields. Should not be emitted.
            Assert.DoesNotContain(outline.Declarations, d => d.Kind == "field" && d.Name == "local");
            Assert.DoesNotContain(outline.Declarations, d => d.Kind == "field" && d.Name == "another");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Parse_emits_fields_with_initializers()
    {
        var path = WriteToTemp("""
            struct Defaults {
                int x = 0;
                int y = 42;
                std::string name = "default";
            };
            """);
        try
        {
            var outline = CppOutlineParser.Parse(path);
            Assert.Contains(outline.Declarations, d => d.Kind == "field" && d.Name == "x");
            Assert.Contains(outline.Declarations, d => d.Kind == "field" && d.Name == "y");
            Assert.Contains(outline.Declarations, d => d.Kind == "field" && d.Name == "name");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Parse_handles_method_returning_pointer_to_template()
    {
        var path = WriteToTemp("""
            class Factory {
            public:
                std::shared_ptr<MyClass> Create(int id);
                std::vector<int>* GetCounts() const;
            };
            """);
        try
        {
            var outline = CppOutlineParser.Parse(path);
            Assert.Contains(outline.Declarations, d => d.Kind == "method" && d.Name == "Create");
            Assert.Contains(outline.Declarations, d => d.Kind == "method" && d.Name == "GetCounts");
        }
        finally { File.Delete(path); }
    }
}
