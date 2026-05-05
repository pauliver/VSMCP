using System.Collections.Generic;

namespace VSMCP.Shared;

public enum BreakpointKind
{
    /// <summary>File + line (with optional column). The default.</summary>
    Line = 0,
    /// <summary>Function name match (e.g. "MyClass.DoWork"). All overloads by default.</summary>
    Function = 1,
    /// <summary>Memory address (hex string like "0x7FF123AB" or "0x...").</summary>
    Address = 2,
    /// <summary>Data breakpoint: break when a memory address is written to.</summary>
    Data = 3,
}

public enum BreakpointHitKind
{
    /// <summary>Break every time the breakpoint is hit (default).</summary>
    Always = 0,
    /// <summary>Break when the hit count equals <see cref="BreakpointSetOptions.HitCount"/>.</summary>
    HitCountEqual = 1,
    /// <summary>Break once the hit count is >= <see cref="BreakpointSetOptions.HitCount"/>.</summary>
    HitCountGreaterOrEqual = 2,
    /// <summary>Break every Nth hit where N is <see cref="BreakpointSetOptions.HitCount"/>.</summary>
    HitCountMultiple = 3,
}

public enum BreakpointConditionKind
{
    None = 0,
    /// <summary>Break when the expression evaluates to true.</summary>
    WhenTrue = 1,
    /// <summary>Break when the expression's value changes between hits.</summary>
    WhenChanged = 2,
}

public sealed class BreakpointSetOptions
{
    public BreakpointKind Kind { get; set; } = BreakpointKind.Line;

    // Line breakpoints
    public string? File { get; set; }
    public int? Line { get; set; }
    public int? Column { get; set; }

    // Function breakpoints
    public string? Function { get; set; }

    // Address / data breakpoints
    public string? Address { get; set; }
    /// <summary>Bytes to watch for a data breakpoint (1, 2, 4, or 8).</summary>
    public int? DataByteCount { get; set; }

    // Common modifiers
    public BreakpointConditionKind ConditionKind { get; set; } = BreakpointConditionKind.None;
    public string? ConditionExpression { get; set; }

    public BreakpointHitKind HitKind { get; set; } = BreakpointHitKind.Always;
    public int? HitCount { get; set; }

    /// <summary>When set, creates a tracepoint (logpoint) instead of a break: emits this message to the Output window without stopping.</summary>
    public string? TracepointMessage { get; set; }

    /// <summary>Start disabled (default: enabled).</summary>
    public bool Disabled { get; set; }
}

public sealed class BreakpointInfo
{
    /// <summary>Opaque id minted by the VSIX. Stable for the lifetime of the session.</summary>
    public string Id { get; set; } = "";
    public BreakpointKind Kind { get; set; }
    public string? File { get; set; }
    public int? Line { get; set; }
    public int? Column { get; set; }
    public string? Function { get; set; }
    public string? Address { get; set; }
    public int? DataByteCount { get; set; }
    public BreakpointConditionKind ConditionKind { get; set; }
    public string? ConditionExpression { get; set; }
    public BreakpointHitKind HitKind { get; set; }
    public int? HitCount { get; set; }
    public int CurrentHits { get; set; }
    public bool Enabled { get; set; }
    public bool IsTracepoint { get; set; }
    public string? TracepointMessage { get; set; }
    /// <summary>Number of concrete bind sites (a single file:line may resolve to multiple physical breakpoints).</summary>
    public int BindSites { get; set; }
}

public sealed class BreakpointListResult
{
    public List<BreakpointInfo> Breakpoints { get; set; } = new();
}
