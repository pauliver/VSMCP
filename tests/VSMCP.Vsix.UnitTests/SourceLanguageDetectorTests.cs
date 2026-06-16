using VSMCP.Core;
using Xunit;

namespace VSMCP.Tests.Vsix;

/// <summary>
/// Locks the behavior-preserving extension detection extracted from the project.* tools
/// (#123 audit parity): suffix-based (string.EndsWith, NOT Path.GetExtension),
/// ordinal/case-SENSITIVE, cpp set {.h,.cpp,.hpp,.cc,.cxx}, csharp {.cs}. A future switch to
/// Path.GetExtension or case-insensitivity would change which edit path project.* takes — these
/// tests make that regression loud.
/// </summary>
public class SourceLanguageDetectorTests
{
    [Theory]
    [InlineData("a.h", SourceLanguage.Cpp)]
    [InlineData("a.cpp", SourceLanguage.Cpp)]
    [InlineData("a.hpp", SourceLanguage.Cpp)]
    [InlineData("a.cc", SourceLanguage.Cpp)]
    [InlineData("a.cxx", SourceLanguage.Cpp)]
    [InlineData("a.cs", SourceLanguage.CSharp)]
    [InlineData("a.txt", SourceLanguage.Unknown)]
    [InlineData("a.csproj", SourceLanguage.Unknown)]   // does not end with .cs
    [InlineData("a.cshtml", SourceLanguage.Unknown)]   // does not end with .cs
    [InlineData("A.CPP", SourceLanguage.Unknown)]      // case-sensitive: preserved behavior
    [InlineData("A.CS", SourceLanguage.Unknown)]       // case-sensitive: preserved behavior
    [InlineData("a.Hpp", SourceLanguage.Unknown)]      // mixed case does not match
    [InlineData("a.CXX", SourceLanguage.Unknown)]      // case-sensitive
    [InlineData("Makefile", SourceLanguage.Unknown)]   // no extension
    [InlineData("foo.test.cs", SourceLanguage.CSharp)] // suffix match on trailing .cs
    [InlineData("foo.cpp.bak", SourceLanguage.Unknown)]// ends with .bak
    [InlineData("foo.generated.hpp", SourceLanguage.Cpp)]
    [InlineData(@"C:\src\proj\Models\Order.cs", SourceLanguage.CSharp)]
    [InlineData("/home/u/proj/render/Mesh.cpp", SourceLanguage.Cpp)]
    [InlineData(@"C:\a.b.c\noext", SourceLanguage.Unknown)] // dots only in directory
    [InlineData(".cs", SourceLanguage.CSharp)]         // bare suffix matches EndsWith
    [InlineData(".c", SourceLanguage.Unknown)]         // .c deliberately NOT in cpp set
    [InlineData("a.hxx", SourceLanguage.Unknown)]      // .hxx deliberately NOT in cpp set
    [InlineData("a.hh", SourceLanguage.Unknown)]       // .hh deliberately NOT in cpp set
    public void FromPath_maps_extension_to_language(string path, SourceLanguage expected)
        => Assert.Equal(expected, SourceLanguageDetector.FromPath(path));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]   // IsNullOrEmpty (not whitespace) -> falls through to Unknown
    public void FromPath_null_or_blank_is_Unknown(string? path)
        => Assert.Equal(SourceLanguage.Unknown, SourceLanguageDetector.FromPath(path));

    [Fact]
    public void IsCpp_matches_cpp_suffixes_only()
    {
        Assert.True(SourceLanguageDetector.IsCpp("a.cpp"));
        Assert.False(SourceLanguageDetector.IsCpp("a.cs"));
        Assert.False(SourceLanguageDetector.IsCpp(null));
        Assert.False(SourceLanguageDetector.IsCpp("A.CPP"));   // case-sensitive
    }

    [Fact]
    public void IsCSharp_matches_dot_cs_only()
    {
        Assert.True(SourceLanguageDetector.IsCSharp("a.cs"));
        Assert.False(SourceLanguageDetector.IsCSharp(null));
        Assert.False(SourceLanguageDetector.IsCSharp("A.CS"));     // case-sensitive
        Assert.False(SourceLanguageDetector.IsCSharp("a.csproj")); // not .cs
    }

    [Fact]
    public void FromPath_csharp_and_cpp_resolve_independently()
    {
        // No extension is simultaneously .cs and a cpp suffix; .cs always resolves CSharp.
        Assert.Equal(SourceLanguage.CSharp, SourceLanguageDetector.FromPath("x.cs"));
        Assert.Equal(SourceLanguage.Cpp, SourceLanguageDetector.FromPath("x.cpp"));
    }

    [Fact]
    public void FromPath_trailing_ignorable_codepoint_is_Unknown()
    {
        // INTENTIONAL ordinal divergence from the original culture-default EndsWith: a suffix
        // followed by a Unicode collation-ignorable codepoint (U+200B zero-width space) does NOT
        // match under Ordinal, whereas culture-aware EndsWith WOULD have matched it. Ordinal fails
        // closed (-> Unknown), the safer behavior. Real Roslyn/DTE file paths never carry these;
        // this locks the deliberate choice so it can't be silently reverted to culture-default.
        Assert.Equal(SourceLanguage.Unknown, SourceLanguageDetector.FromPath("a.cs​"));
        Assert.Equal(SourceLanguage.Unknown, SourceLanguageDetector.FromPath("a.cpp​"));
    }
}
