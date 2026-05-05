namespace VSMCP.Shared;

// M20: Edit & Continue (issue #71)

public sealed class EncStatusResult
{
    /// <summary>True when a debug session is currently active (Run or Break mode).</summary>
    public bool DebuggingActive { get; set; }
    /// <summary>True when the debugger is paused (in break mode). ENC is most reliable from here.</summary>
    public bool InBreakMode { get; set; }
    /// <summary>True when Edit & Continue is supported by the active debug engine and project type.</summary>
    public bool Available { get; set; }
    /// <summary>Human-readable reason when ENC isn't available (wrong mode, optimized build, runtime not supported, etc.).</summary>
    public string? Reason { get; set; }
}

public sealed class EncApplyResult
{
    /// <summary>True when the DTE command returned without error and we believe edits were applied.</summary>
    public bool Success { get; set; }
    /// <summary>Diagnostic message when Success=false; includes any caught COM exception detail.</summary>
    public string? Message { get; set; }
}
