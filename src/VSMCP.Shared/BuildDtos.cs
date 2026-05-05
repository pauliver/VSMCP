using System.Collections.Generic;

namespace VSMCP.Shared;

public enum BuildAction
{
    Build = 0,
    Rebuild = 1,
    Clean = 2,
}

public enum BuildState
{
    Queued = 0,
    Running = 1,
    Succeeded = 2,
    Failed = 3,
    Canceled = 4,
    TimedOut = 5,
}

public enum BuildSeverity
{
    Info = 0,
    Warning = 1,
    Error = 2,
}

public sealed class BuildHandle
{
    /// <summary>Opaque id for later status/wait/cancel/errors calls.</summary>
    public string BuildId { get; set; } = "";
    public BuildAction Action { get; set; }
    public string? Configuration { get; set; }
    public string? Platform { get; set; }
    public List<string> Projects { get; set; } = new();
    public long StartedAtMs { get; set; }
}

public sealed class BuildStatus
{
    public string BuildId { get; set; } = "";
    public BuildState State { get; set; }
    public long StartedAtMs { get; set; }
    public long? EndedAtMs { get; set; }
    public int ErrorCount { get; set; }
    public int WarningCount { get; set; }
    /// <summary>True if every project reported success. Populated when State is terminal.</summary>
    public bool? AllProjectsSucceeded { get; set; }
}

public sealed class BuildDiagnostic
{
    public BuildSeverity Severity { get; set; }
    public string? Code { get; set; }
    public string Message { get; set; } = "";
    public string? Project { get; set; }
    public string? File { get; set; }
    public int? Line { get; set; }
    public int? Column { get; set; }
}

public sealed class BuildOutput
{
    public string BuildId { get; set; } = "";
    /// <summary>Name of the Output window pane the text came from (typically "Build").</summary>
    public string Pane { get; set; } = "Build";
    public string Text { get; set; } = "";
}
