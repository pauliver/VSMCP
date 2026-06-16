using VSMCP.Core;
using Xunit;

namespace VSMCP.Tests.Vsix;

/// <summary>Covers compiler-flag extraction for compile_commands ingestion (#135).</summary>
public class CompilerFlagExtractorTests
{
    [Fact]
    public void Clang_attached_and_separate_includes()
    {
        var f = CompilerFlagExtractor.ExtractFromCommand("clang++ -Iinclude -I /opt/h -isystem /usr/inc -c a.cpp");
        Assert.Contains("include", f.Includes);
        Assert.Contains("/opt/h", f.Includes);
        Assert.Contains("/usr/inc", f.SystemIncludes);
    }

    [Fact]
    public void Defines_and_std_clang()
    {
        var f = CompilerFlagExtractor.ExtractFromCommand("g++ -DNDEBUG -D VERSION=2 -std=c++20 a.cpp");
        Assert.Contains("NDEBUG", f.Defines);
        Assert.Contains("VERSION=2", f.Defines);
        Assert.Equal("c++20", f.Std);
    }

    [Fact]
    public void Msvc_spellings()
    {
        var f = CompilerFlagExtractor.ExtractFromCommand("cl.exe /Iinc /DWIN32 /std:c++17 /c a.cpp");
        Assert.Contains("inc", f.Includes);
        Assert.Contains("WIN32", f.Defines);
        Assert.Equal("c++17", f.Std);
    }

    [Fact]
    public void Tokenize_respects_quotes()
    {
        var t = CompilerFlagExtractor.Tokenize("clang -I \"C:/Program Files/inc\" a.cpp");
        Assert.Contains("C:/Program Files/inc", t);
        var f = CompilerFlagExtractor.Extract(t);
        Assert.Contains("C:/Program Files/inc", f.Includes);
    }

    [Fact]
    public void Empty_command_is_safe()
    {
        var f = CompilerFlagExtractor.ExtractFromCommand("");
        Assert.Empty(f.Includes);
        Assert.Null(f.Std);
    }
}
