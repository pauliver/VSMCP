using System.Collections.Generic;

namespace VSMCP.Shared;

// M19: Active editor + workspace events + tests + NuGet

public sealed class ActiveEditorInfo
{
    public string? File { get; set; }
    public string? Language { get; set; }
    public bool IsDirty { get; set; }
    public int? CursorLine { get; set; }
    public int? CursorColumn { get; set; }
}

public sealed class EditorSelection
{
    public string File { get; set; } = "";
    public int StartLine { get; set; }
    public int StartColumn { get; set; }
    public int EndLine { get; set; }
    public int EndColumn { get; set; }
    public string Text { get; set; } = "";
}

public enum WorkspaceEventKind
{
    BuildStarted = 1,
    BuildCompleted = 2,
    FileSaved = 3,
    DocumentOpened = 4,
    DocumentClosed = 5,
    ActiveDocumentChanged = 6,
    DebugStateChanged = 7,
}

public sealed class WorkspaceEvent
{
    public string Id { get; set; } = "";
    public WorkspaceEventKind Kind { get; set; }
    public long TimestampMs { get; set; }
    public string Summary { get; set; } = "";
    public string? File { get; set; }
}

public sealed class WorkspaceEventsResult
{
    public List<WorkspaceEvent> Events { get; set; } = new();
    public int TotalCollected { get; set; }
    public long LatestTimestampMs { get; set; }
}

public sealed class TestDiscoveryResult
{
    public List<TestCase> Tests { get; set; } = new();
    public int Total { get; set; }
}

public sealed class TestCase
{
    public string FullyQualifiedName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? Source { get; set; }
    public string? CodeFilePath { get; set; }
    public int LineNumber { get; set; }
}

public enum TestOutcome
{
    None = 0,
    Passed = 1,
    Failed = 2,
    Skipped = 3,
    NotFound = 4,
}

public sealed class TestRunResult
{
    public string RunId { get; set; } = "";
    public int Passed { get; set; }
    public int Failed { get; set; }
    public int Skipped { get; set; }
    public List<TestResultItem> Results { get; set; } = new();
    public string? Output { get; set; }
}

public sealed class TestResultItem
{
    public string FullyQualifiedName { get; set; } = "";
    public TestOutcome Outcome { get; set; }
    public double DurationMs { get; set; }
    public string? ErrorMessage { get; set; }
    public string? StackTrace { get; set; }
}

public sealed class NuGetPackage
{
    public string Id { get; set; } = "";
    public string Version { get; set; } = "";
    public string? ProjectId { get; set; }
}

public sealed class NuGetListResult
{
    public List<NuGetPackage> Packages { get; set; } = new();
}

public sealed class NuGetActionResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public NuGetPackage? Package { get; set; }
}
