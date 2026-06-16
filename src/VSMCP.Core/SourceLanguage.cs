using System;

namespace VSMCP.Core;

/// <summary>Source language inferred from a file path's extension (#123 audit extract).</summary>
public enum SourceLanguage
{
    Unknown = 0,
    CSharp,
    Cpp
}

/// <summary>
/// Extension-based C#/C++ detection extracted from the project.* tools (RpcTarget.ProjectEdit.cs),
/// which used culture-default <c>string.EndsWith(suffix)</c>:
///   * SUFFIX match (not Path.GetExtension) so "foo.test.cs" -> CSharp and ".cs" -> CSharp.
///   * CASE-SENSITIVE for ASCII: "Foo.CPP"/"Bar.CS" -> Unknown (matches the original; deliberately
///     NOT switched to case-insensitive -- other detection sites differ and this is the
///     conservative choice for the #123 migration).
///   * cpp set = { .h .cpp .hpp .cc .cxx } (NO .c, NO .hxx/.hh). csharp set = { .cs }.
/// Uses StringComparison.Ordinal (same convention as CompilerFlagExtractor). This matches the
/// original culture-default EndsWith for every well-formed file path; it intentionally diverges
/// ONLY when a suffix is followed by a Unicode collation-ignorable codepoint (e.g. a trailing
/// zero-width space) -- culture-aware EndsWith would still match, ordinal does not. Such paths do
/// not occur for real Roslyn/DTE file locations, and ordinal fails CLOSED there (-> Unknown ->
/// the existing "Could not auto-detect language" NotFound), which is the safer behavior.
/// </summary>
public static class SourceLanguageDetector
{
    // Order/content mirrors RpcTarget.ProjectEdit.cs exactly.
    private static readonly string[] CppSuffixes = { ".h", ".cpp", ".hpp", ".cc", ".cxx" };

    /// <summary>True when <paramref name="path"/> ends with a project.* C++ extension (ordinal, case-sensitive).</summary>
    public static bool IsCpp(string? path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        foreach (var suffix in CppSuffixes)
        {
            if (path!.EndsWith(suffix, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    /// <summary>True when <paramref name="path"/> ends with ".cs" (ordinal, case-sensitive).</summary>
    public static bool IsCSharp(string? path) =>
        !string.IsNullOrEmpty(path) && path!.EndsWith(".cs", StringComparison.Ordinal);

    /// <summary>
    /// Resolve the binary language. C# is checked first to match the ordering in
    /// ProjectAddMemberToSubclassesAsync; ordering is otherwise cosmetic because no extension is
    /// both .cs and a cpp suffix. Neither -> <see cref="SourceLanguage.Unknown"/>, which the
    /// callers map to the existing "Could not auto-detect language" NotFound fall-through.
    /// </summary>
    public static SourceLanguage FromPath(string? path)
    {
        if (IsCSharp(path)) return SourceLanguage.CSharp;
        if (IsCpp(path)) return SourceLanguage.Cpp;
        return SourceLanguage.Unknown;
    }
}
