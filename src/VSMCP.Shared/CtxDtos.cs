using System.Collections.Generic;

namespace VSMCP.Shared;

// -------- Context-efficiency primitives (issues #72-#89) --------

/// <summary>Path interning map. Used by responses with many file references; values are absolute paths.</summary>
public sealed class PathTable
{
    public Dictionary<int, string> Paths { get; set; } = new();
}

/// <summary>How verbosely to display Roslyn symbols (issue #79).</summary>
public enum SymbolDisplayMode
{
    /// <summary>Member name + bare parameter type names. e.g. <c>Add(User)</c>.</summary>
    Minimal = 0,
    /// <summary>Containing-type-qualified, simple parameter names. e.g. <c>List.Add(User)</c>.</summary>
    Qualified = 1,
    /// <summary>Full Roslyn ToDisplayString. Current default behavior.</summary>
    Full = 2,
}

// -------- #75 Aggregated diagnostics --------

public sealed class CompactDiagnostic
{
    public string Id { get; set; } = "";
    public CodeDiagnosticSeverity Severity { get; set; }
    public int Line { get; set; }
    public int Column { get; set; }
    /// <summary>The first quoted identifier in the diagnostic message, when present (e.g. 'Foo' from CS0246).</summary>
    public string? Identifier { get; set; }
    /// <summary>First 80 chars of the message; full message available via verbose mode.</summary>
    public string? MessageBrief { get; set; }
}

public sealed class FileDiagnostics
{
    public List<CompactDiagnostic> Errors { get; set; } = new();
    public List<CompactDiagnostic> Warnings { get; set; } = new();
}

public sealed class GroupedDiagnosticsResult
{
    public Dictionary<string, FileDiagnostics> Files { get; set; } = new();
    public int TotalErrors { get; set; }
    public int TotalWarnings { get; set; }
    public bool Truncated { get; set; }
}

// -------- #76 build.summary --------

public sealed class BuildProjectSummary
{
    public string Name { get; set; } = "";
    public string Status { get; set; } = ""; // Succeeded / Failed / Canceled / NotBuilt
    public int Errors { get; set; }
    public int Warnings { get; set; }
    public CompactDiagnostic? FirstError { get; set; }
    public string? FirstErrorFile { get; set; }
}

public sealed class BuildSummaryResult
{
    public string BuildId { get; set; } = "";
    public string Outcome { get; set; } = "";
    public List<BuildProjectSummary> Projects { get; set; } = new();
    public int TotalErrors { get; set; }
    public int TotalWarnings { get; set; }
    public long DurationMs { get; set; }
}

// -------- #86 test.run summary --------

public sealed class TestSummaryResult
{
    public string RunId { get; set; } = "";
    public int Passed { get; set; }
    public int Failed { get; set; }
    public int Skipped { get; set; }
    public double DurationMs { get; set; }
    public List<TestResultItem> Failures { get; set; } = new();
    public string? OutputTail { get; set; }
}

// -------- #72 outline --------

public sealed class FileOutlineResult
{
    public string File { get; set; } = "";
    public string ContentHash { get; set; } = "";
    public List<string> Lines { get; set; } = new();
}

// -------- #73 file.read_if_changed --------

public sealed class FileReadIfChangedResult
{
    public string Path { get; set; } = "";
    public bool Unchanged { get; set; }
    public string ContentHash { get; set; } = "";
    public FileReadResult? Result { get; set; }
}

// -------- #88 code.diff --------

public sealed class DiffHunk
{
    public int StartLine { get; set; }
    public List<string> RemovedLines { get; set; } = new();
    public List<string> AddedLines { get; set; } = new();
}

public sealed class CodeDiffResult
{
    public string File { get; set; } = "";
    public string FromHash { get; set; } = "";
    public string ToHash { get; set; } = "";
    public List<DiffHunk> Hunks { get; set; } = new();
}

// -------- #81 file.info --------

public sealed class FileInfoResult
{
    public string Path { get; set; } = "";
    public string Language { get; set; } = "";
    public string Encoding { get; set; } = "";
    public int LineCount { get; set; }
    public long ByteSize { get; set; }
    public string ContentHash { get; set; } = "";
    public string? Project { get; set; }
    public string? Namespace { get; set; }
    public bool IsTest { get; set; }
    public bool IsGenerated { get; set; }
    public int OutlineDepth { get; set; }
    public bool OpenInEditor { get; set; }
    public bool HasUnsavedChanges { get; set; }
}

// -------- #83 code.symbol_summary --------

public sealed class SymbolSummaryResult
{
    public SymbolMatch Symbol { get; set; } = new();
    public List<string> Calls { get; set; } = new();
    public List<string> Touches { get; set; } = new();
    public List<string> Throws { get; set; } = new();
    public int LineCount { get; set; }
    public int Cyclomatic { get; set; }
    public bool IsAsync { get; set; }
    public int Awaits { get; set; }
    public string? Returns { get; set; }
}

// -------- #74 code.investigate --------

public sealed class InvestigateCallEntry
{
    public string Symbol { get; set; } = "";
    public CodeSpan? Location { get; set; }
}

public sealed class InvestigateStats
{
    public int ReferenceCount { get; set; }
    public bool IsAsync { get; set; }
    public bool IsStatic { get; set; }
    public bool IsAbstract { get; set; }
    public bool IsVirtual { get; set; }
}

public sealed class InvestigateResult
{
    public SymbolMatch Symbol { get; set; } = new();
    public string? Body { get; set; }
    public List<InvestigateCallEntry> Calls { get; set; } = new();
    public List<InvestigateCallEntry> CallsOut { get; set; } = new();
    public List<InvestigateCallEntry> DerivedOrOverrides { get; set; } = new();
    public List<InvestigateCallEntry> Tests { get; set; } = new();
    public InvestigateStats Stats { get; set; } = new();
}

// -------- #77 verified mutations --------

public sealed class VerificationInfo
{
    public List<string> Files { get; set; } = new();
    public int NewErrors { get; set; }
    public int NewWarnings { get; set; }
    public List<CompactDiagnostic> Errors { get; set; } = new();
    public List<CompactDiagnostic> Warnings { get; set; } = new();
}

// -------- #82 session scope --------

public sealed class SessionScopeResult
{
    public string ScopeId { get; set; } = "";
    public List<string> ResolvedSymbols { get; set; } = new();
    public string? ResolvedProject { get; set; }
    public string? ResolvedFolder { get; set; }
    public long EstablishedAtMs { get; set; }
}

public sealed class SessionCurrentResult
{
    public List<string> Symbols { get; set; } = new();
    public string? Project { get; set; }
    public string? Folder { get; set; }
    public long EstablishedAtMs { get; set; }
    public bool Active { get; set; }
}

// -------- #85 io.context --------

public sealed class IoContextResult
{
    public ActiveEditorInfo? Editor { get; set; }
    public VsStatus? Solution { get; set; }
    public DebugInfo? Debugger { get; set; }
    public long? LastBuildAtMs { get; set; }
    public string? LastBuildOutcome { get; set; }
}

// -------- #80 truncation cursor --------

public sealed class CursorPage<T>
{
    public List<T> Items { get; set; } = new();
    public int Total { get; set; }
    public bool Truncated { get; set; }
    public int RemainingCount { get; set; }
    public string? NextCursor { get; set; }
    public string? DroppedHint { get; set; }
}
