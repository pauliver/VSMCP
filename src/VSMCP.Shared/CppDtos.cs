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
