using System.Collections.Generic;

namespace VSMCP.Shared;

/// <summary>One source location (file + 1-based line + 1-based column).</summary>
public sealed class CppLocation
{
    public string File { get; set; } = "";
    public int Line { get; set; }
    public int Column { get; set; }
    /// <summary>Optional human-readable context — usually the matched line.</summary>
    public string? Snippet { get; set; }
}

public sealed class CppLocationResult
{
    public CppLocation? Location { get; set; }
    public string? Spelling { get; set; }
    public string? Kind { get; set; }
}

public sealed class CppLocationListResult
{
    public List<CppLocation> Locations { get; set; } = new();
    public int Total { get; set; }
    public string? Spelling { get; set; }
    public string? Kind { get; set; }
}

public sealed class CppQuickInfoResult
{
    public string? Spelling { get; set; }
    public string? Kind { get; set; }
    public string? Type { get; set; }
    public string? DisplayName { get; set; }
    public CppLocation? DeclarationLocation { get; set; }
    public string? BriefComment { get; set; }
}

public enum CppDiagnosticSeverity
{
    Ignored = 0,
    Note = 1,
    Warning = 2,
    Error = 3,
    Fatal = 4,
}

public sealed class CppDiagnostic
{
    public CppDiagnosticSeverity Severity { get; set; }
    public string Message { get; set; } = "";
    public CppLocation? Location { get; set; }
    public string? Category { get; set; }
}

public sealed class CppDiagnosticsResult
{
    public string File { get; set; } = "";
    public List<CppDiagnostic> Diagnostics { get; set; } = new();
    /// <summary>True if the parser produced any diagnostics with severity >= Error.</summary>
    public bool HasErrors { get; set; }
}
