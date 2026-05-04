namespace VSMCP.Shared;

public static class ErrorCodes
{
    public const string NotConnected = "VSMCP-not-connected";
    public const string NotDebugging = "VSMCP-not-debugging";
    public const string WrongState = "VSMCP-wrong-state";
    public const string TargetBusy = "VSMCP-target-busy";
    public const string NotFound = "VSMCP-not-found";
    public const string Timeout = "VSMCP-timeout";
    public const string InteropFault = "VSMCP-interop-fault";
    public const string Unsupported = "VSMCP-unsupported";
    public const string UpgradeRequired = "VSMCP-upgrade-required";

    // M18+M19 additions
    public const string SymbolAmbiguous = "VSMCP-symbol-ambiguous";
    public const string ContentHashMismatch = "VSMCP-content-hash-mismatch";
    public const string WorkspaceLocked = "VSMCP-workspace-locked";
    public const string RoslynUnavailable = "VSMCP-roslyn-unavailable";
}
