using System.Collections.Generic;

namespace VSMCP.Shared
{
    public enum WorkspaceEventKind
    {
        BuildStarted = 1,
        BuildCompleted = 2,
        FileSaved = 3,
        DocumentOpened = 4,
        DocumentClosed = 5,
        ActiveDocumentChanged = 6,
        DebugStateChanged = 7,
        Generic = 8,
        SolutionOpened = 9,
        SolutionClosed = 10,
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
}