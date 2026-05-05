using System.Collections.Generic;

namespace VSMCP.Shared;

/// <summary>One declaration found by cpp_outline. Kind is namespace/class/struct/union/enum/typedef/using/function/method.</summary>
public sealed class CppDecl
{
    public string Kind { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>Containing namespace + class chain joined with '::', or null at file scope.</summary>
    public string? Container { get; set; }
    public int Line { get; set; }
    /// <summary>Signature line (declaration text, trimmed).</summary>
    public string Signature { get; set; } = "";
}

public sealed class CppOutlineResult
{
    public string File { get; set; } = "";
    public List<CppDecl> Declarations { get; set; } = new();
    public int Total { get; set; }
    public bool Truncated { get; set; }
}

public sealed class CppMembersResult
{
    public string File { get; set; } = "";
    public string ClassName { get; set; } = "";
    public List<CppDecl> Members { get; set; } = new();
}

/// <summary>One declaration in a multi-file aggregation result. Like CppDecl but with a File field.</summary>
public sealed class CppFileDecl
{
    public string Kind { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Container { get; set; }
    public string File { get; set; } = "";
    public int Line { get; set; }
    public string Signature { get; set; } = "";
}

public sealed class CppClassesResult
{
    public List<CppFileDecl> Classes { get; set; } = new();
    public int Total { get; set; }
    public bool Truncated { get; set; }
}

public sealed class CppFindSymbolResult
{
    public List<CppFileDecl> Matches { get; set; } = new();
    public int Total { get; set; }
    public bool Truncated { get; set; }
}

public sealed class CppReadMemberResult
{
    public string File { get; set; } = "";
    public string ClassName { get; set; } = "";
    public string MemberName { get; set; } = "";
    public int StartLine { get; set; }
    public int EndLine { get; set; }
    public string Content { get; set; } = "";
    public string? Signature { get; set; }
}

public sealed class CppOrganizeIncludesResult
{
    public string File { get; set; } = "";
    public bool Changed { get; set; }
    public int IncludesCounted { get; set; }
    public int Duplicates { get; set; }
    public string? Diff { get; set; }
}

public sealed class CppSymbolSummaryEntry
{
    public CppFileDecl Decl { get; set; } = new();
    public string? Type { get; set; }
    public string? BriefComment { get; set; }
}

public sealed class CppSymbolSummaryResult
{
    public string Symbol { get; set; } = "";
    public List<CppSymbolSummaryEntry> Entries { get; set; } = new();
    public int Total { get; set; }
}

public sealed class CppReplaceMemberResult
{
    public string File { get; set; } = "";
    public string ClassName { get; set; } = "";
    public string MemberName { get; set; } = "";
    public bool Replaced { get; set; }
    public int StartLine { get; set; }
    public int EndLine { get; set; }
}

public sealed class CppGenerateCtorResult
{
    public string File { get; set; } = "";
    public string ClassName { get; set; } = "";
    public bool Inserted { get; set; }
    public int InsertedAtLine { get; set; }
    public string Code { get; set; } = "";
}

public sealed class CppOverrideMemberResult
{
    public string File { get; set; } = "";
    public string ClassName { get; set; } = "";
    public string MethodName { get; set; } = "";
    public bool Inserted { get; set; }
    public int InsertedAtLine { get; set; }
    public string Code { get; set; } = "";
}

public sealed class CppInvestigateStats
{
    public string? Kind { get; set; }
    public string? Type { get; set; }
    public bool IsVirtual { get; set; }
    public bool IsStatic { get; set; }
    public bool IsConst { get; set; }
}

public sealed class CppInvestigateResult
{
    public CppFileDecl? Symbol { get; set; }
    public string? QuickInfoType { get; set; }
    public string? BriefComment { get; set; }
    public string? Body { get; set; }
    public CppInvestigateStats Stats { get; set; } = new();
    public List<CppLocation> Calls { get; set; } = new();
    public int TotalCalls { get; set; }
}

public sealed class CppOutlineEntry
{
    public string File { get; set; } = "";
    public CppOutlineResult? Outline { get; set; }
    public string? Error { get; set; }
}

public sealed class CppOutlineManyResult
{
    public List<CppOutlineEntry> Entries { get; set; } = new();
}

public sealed class CppInheritanceNode
{
    public string Name { get; set; } = "";
    public string? Access { get; set; }      // public/private/protected as declared
    public string? File { get; set; }
    public int Line { get; set; }
    public List<CppInheritanceNode> Bases { get; set; } = new();
}

public sealed class CppInheritanceResult
{
    public string ClassName { get; set; } = "";
    public CppInheritanceNode? Tree { get; set; }
}

public sealed class CppGenerateEqualityResult
{
    public string File { get; set; } = "";
    public string ClassName { get; set; } = "";
    public bool Inserted { get; set; }
    public int InsertedAtLine { get; set; }
    public string Code { get; set; } = "";
    public int FieldsCompared { get; set; }
}

public sealed class CppImplementInterfaceResult
{
    public string DerivedFile { get; set; } = "";
    public string DerivedClass { get; set; } = "";
    public string BaseClass { get; set; } = "";
    public List<string> InsertedMethods { get; set; } = new();
    public List<string> SkippedAlreadyOverridden { get; set; } = new();
    public int InsertedAtLine { get; set; }
    public string Code { get; set; } = "";
}

public sealed class CppScaffoldFileResult
{
    public string HeaderPath { get; set; } = "";
    public string? CppPath { get; set; }
    public bool Created { get; set; }
}

public sealed class CppIncludeSuggestion
{
    public string Header { get; set; } = "";
    public string SymbolKind { get; set; } = "";
    public int Line { get; set; }
}

public sealed class CppSuggestIncludesResult
{
    public string Symbol { get; set; } = "";
    public List<CppIncludeSuggestion> Suggestions { get; set; } = new();
}

public sealed class CppRenameSolutionResult
{
    public string OldName { get; set; } = "";
    public string NewName { get; set; } = "";
    public List<CppLocation> EditedLocations { get; set; } = new();
    public int FilesEdited { get; set; }
    public int TotalReferences { get; set; }
}

public sealed class CppMoveTypeResult
{
    public string SourceFile { get; set; } = "";
    public string TargetFile { get; set; } = "";
    public string TypeName { get; set; } = "";
    public bool Moved { get; set; }
    public int StartLine { get; set; }
    public int EndLine { get; set; }
    public string? Note { get; set; }
}

public sealed class CppMoveMethodResult
{
    public string SourceFile { get; set; } = "";
    public string TargetFile { get; set; } = "";
    public string ClassName { get; set; } = "";
    public string MethodName { get; set; } = "";
    public bool Moved { get; set; }
    public int StartLine { get; set; }
    public int EndLine { get; set; }
    public string? Note { get; set; }
}

public sealed class CppAnalyzerStatusResult
{
    public bool Spawned { get; set; }
    public int? ProcessId { get; set; }
    public bool Alive { get; set; }
    public int? ExitCode { get; set; }
    public string? LogPath { get; set; }
    public List<string> RecentLog { get; set; } = new();
    public string? LastError { get; set; }
}

// C++ Extensions

public sealed class HeaderLookupResult
{
    public CodeSpan? Header { get; set; }
    public string Type { get; set; } = "";
}

public sealed class IncludeChainItem
{
    public string File { get; set; } = "";
    public int Line { get; set; }
    public string Type { get; set; } = "";
    /// <summary>
    /// True when File is a real path on disk. False means resolution failed (no project
    /// include roots were known); File then contains the literal include text from the source.
    /// </summary>
    public bool Resolved { get; set; }
}

public sealed class IncludeChainResult
{
    public List<IncludeChainItem> Chain { get; set; } = new();
}

public sealed class MacroDefinition
{
    public CodeSpan? Location { get; set; }
    public string Expansion { get; set; } = "";
}

public sealed class MacroResult
{
    public MacroDefinition Definition { get; set; } = new();
    public List<CodeSpan> Users { get; set; } = new();
}

public sealed class PreprocessResult
{
    public string Source { get; set; } = "";
    public List<LineMapItem> LineMap { get; set; } = new();
}

public sealed class LineMapItem
{
    public int SourceLine { get; set; }
    public int PreprocLine { get; set; }
}

public sealed class ApiReferenceResult
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string Declaration { get; set; } = "";
    public string? Documentation { get; set; }
    public string? HeaderFile { get; set; }
}

public sealed class GeneratedFileInfo
{
    public string GeneratedFile { get; set; } = "";
    public string GeneratedFrom { get; set; } = "";
    public List<LineMapItem> LineMap { get; set; } = new();
}
