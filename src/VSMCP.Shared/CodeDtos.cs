using System.Collections.Generic;

namespace VSMCP.Shared;

public sealed class CodePosition
{
    public string File { get; set; } = "";
    /// <summary>1-based line number.</summary>
    public int Line { get; set; }
    /// <summary>1-based column number.</summary>
    public int Column { get; set; }
}

public sealed class CodeSpan
{
    public string File { get; set; } = "";
    public int StartLine { get; set; }
    public int StartColumn { get; set; }
    public int EndLine { get; set; }
    public int EndColumn { get; set; }
    /// <summary>Optional snippet of the spanned text (trimmed/truncated).</summary>
    public string? Text { get; set; }
}

public sealed class CodeSymbol
{
    public string Name { get; set; } = "";
    /// <summary>Roslyn SymbolKind (Namespace, NamedType, Method, Property, Field, Event, Parameter, Local).</summary>
    public string Kind { get; set; } = "";
    /// <summary>Fully qualified name of the container (e.g. "Namespace.Class").</summary>
    public string? ContainerName { get; set; }
    public string? Signature { get; set; }
    public CodeSpan? Location { get; set; }
    public List<CodeSymbol> Children { get; set; } = new();
}

public sealed class SymbolsResult
{
    public string File { get; set; } = "";
    public List<CodeSymbol> Symbols { get; set; } = new();
    public string? Language { get; set; }
}

public sealed class LocationListResult
{
    public List<CodeSpan> Locations { get; set; } = new();
    public CodeSymbol? Symbol { get; set; }
}

public sealed class ReferencesResult
{
    public CodeSymbol? Symbol { get; set; }
    public List<CodeSpan> Definitions { get; set; } = new();
    public List<CodeSpan> References { get; set; } = new();
    public int TotalReferences { get; set; }
    public bool Truncated { get; set; }
}

public enum CodeDiagnosticSeverity
{
    Hidden = 0,
    Info = 1,
    Warning = 2,
    Error = 3,
}

public sealed class CodeDiagnosticInfo
{
    public string Id { get; set; } = "";
    public CodeDiagnosticSeverity Severity { get; set; }
    public string Message { get; set; } = "";
    public string? Category { get; set; }
    public CodeSpan? Location { get; set; }
}

public sealed class DiagnosticsResult
{
    public List<CodeDiagnosticInfo> Diagnostics { get; set; } = new();
    public int TotalDiagnostics { get; set; }
    public bool Truncated { get; set; }
    /// <summary>Files that contributed — useful when scope is the whole solution.</summary>
    public List<string> FilesScanned { get; set; } = new();
}

public sealed class QuickInfoResult
{
    public string? Signature { get; set; }
    public string? Documentation { get; set; }
    public string? Kind { get; set; }
    public CodeSymbol? Symbol { get; set; }
}
