using System.Collections.Generic;

namespace VSMCP.Shared;

public enum DiagEventKind
{
    ExceptionThrown = 1,
    ExceptionUnhandled = 2,
    BreakpointHit = 3,
    UserBreak = 4,
}

public sealed class DiagEvent
{
    public string Id { get; set; } = "";
    public DiagEventKind Kind { get; set; }
    public long TimestampMs { get; set; }
    public string Summary { get; set; } = "";
    /// <summary>Populated only by the *_interned diag tools (issue #84): IDs into DiagEventsResult.FramesTable.</summary>
    public List<int>? FrameIds { get; set; }
}

public sealed class DiagEventDetail
{
    public string Id { get; set; } = "";
    public DiagEventKind Kind { get; set; }
    public long TimestampMs { get; set; }
    public string Summary { get; set; } = "";
    public string? ExceptionType { get; set; }
    public string? ExceptionMessage { get; set; }
    public int? ExceptionCode { get; set; }
    public int? ThreadId { get; set; }
    public string? ThreadName { get; set; }
    /// <summary>Top frames at the moment the event was captured. May be empty if the process was not paused.</summary>
    public List<StackFrameInfo> Frames { get; set; } = new();
}

public sealed class DiagEventsResult
{
    public List<DiagEvent> Events { get; set; } = new();
    /// <summary>Total events collected this session (may exceed Events.Count if the buffer was trimmed).</summary>
    public int TotalCollected { get; set; }
    /// <summary>TimestampMs of the newest event in this result. Pass as sinceTimestampMs on the next call to receive only newer events.</summary>
    public long LatestTimestampMs { get; set; }
    /// <summary>Frame intern table (issue #84). Populated by the *_interned variants; keyed by frame id, value is the shared frame info.
    /// When set, each event's full Frames list is replaced by FrameIds referencing this table.</summary>
    public Dictionary<int, StackFrameInfo>? FramesTable { get; set; }
}

public sealed class DiagMemorySnapshot
{
    public string SnapshotId { get; set; } = "";
    public long TimestampMs { get; set; }
    /// <summary>Process working set in bytes.</summary>
    public long WorkingSetBytes { get; set; }
    /// <summary>Process private bytes (committed memory).</summary>
    public long PrivateBytes { get; set; }
    /// <summary>Managed GC heap size in bytes (GC.GetTotalMemory, not a full collection).</summary>
    public long GcHeapBytes { get; set; }
    public int ProcessId { get; set; }
    public string? ProcessName { get; set; }
}

public sealed class DiagCpuSample
{
    public long TimestampMs { get; set; }
    /// <summary>CPU % for the target process at this sample (0-100, may exceed 100 on multi-core).</summary>
    public double CpuPercent { get; set; }
    public long WorkingSetBytes { get; set; }
}

public sealed class DiagCpuTimelineResult
{
    public List<DiagCpuSample> Samples { get; set; } = new();
    public int ProcessId { get; set; }
    public string? ProcessName { get; set; }
    /// <summary>Sampling interval in ms.</summary>
    public int IntervalMs { get; set; }
}
