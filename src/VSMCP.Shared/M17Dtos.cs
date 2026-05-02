using System.Collections.Generic;

namespace VSMCP.Shared;

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
