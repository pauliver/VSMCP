using System.Collections.Generic;

namespace VSMCP.Shared;

/// <summary>
/// High-level debug mode, mirroring <c>EnvDTE.dbgDebugMode</c> without leaking the interop type.
/// </summary>
public enum DebugMode
{
    Design = 0,
    Break = 1,
    Run = 2,
}

/// <summary>
/// Why the debugger is stopped. Populated only when <see cref="DebugInfo.Mode"/> is <see cref="DebugMode.Break"/>.
/// </summary>
public enum DebugStoppedReason
{
    Unknown = 0,
    UserBreak = 1,
    Breakpoint = 2,
    Step = 3,
    Exception = 4,
    Terminated = 5,
}

public sealed class DebugLaunchOptions
{
    /// <summary>Project id (UniqueName, Name, or full path). Omit to debug the solution's configured startup project.</summary>
    public string? ProjectId { get; set; }
    /// <summary>Override the startup project's command-line arguments. Omit to keep the configured value.</summary>
    public string? Args { get; set; }
    /// <summary>Environment variables to layer on top of the startup project's environment.</summary>
    public Dictionary<string, string>? Env { get; set; }
    /// <summary>Override the working directory. Omit to keep the configured value.</summary>
    public string? Cwd { get; set; }
    /// <summary>When true, start without attaching a debugger (F5 vs Ctrl+F5).</summary>
    public bool NoDebug { get; set; }
}

public sealed class DebugAttachOptions
{
    /// <summary>Process id to attach to. Exactly one of <see cref="Pid"/> or <see cref="ProcessName"/> must be set.</summary>
    public int? Pid { get; set; }
    /// <summary>Process name (matched case-insensitively against <c>Process.Name</c>). First match wins.</summary>
    public string? ProcessName { get; set; }
    /// <summary>
    /// Optional debug-engine names (e.g. "Managed (.NET Core)", "Native", "Python"). Empty or null
    /// lets VS pick automatically.
    /// </summary>
    public List<string>? Engines { get; set; }
}

public sealed class DebugThreadInfo
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public bool IsCurrent { get; set; }
}

public sealed class DebugFrameInfo
{
    public int Index { get; set; }
    public string FunctionName { get; set; } = "";
    public string? File { get; set; }
    public int? Line { get; set; }
    public int? Column { get; set; }
    public string? Language { get; set; }
}

public sealed class DebugInfo
{
    public DebugMode Mode { get; set; }
    public DebugStoppedReason StoppedReason { get; set; }
    public int? CurrentProcessId { get; set; }
    public string? CurrentProcessName { get; set; }
    public DebugThreadInfo? CurrentThread { get; set; }
    public DebugFrameInfo? CurrentFrame { get; set; }
    /// <summary>Last exception description, when <see cref="StoppedReason"/> is <see cref="DebugStoppedReason.Exception"/>.</summary>
    public string? LastExceptionMessage { get; set; }
}

public sealed class DebugActionResult
{
    /// <summary>Post-action debug info snapshot. Callers typically use this to confirm the transition.</summary>
    public DebugInfo Info { get; set; } = new();
    /// <summary>Human-readable note about the action (e.g. "Attached to pid 12345").</summary>
    public string? Note { get; set; }
}
