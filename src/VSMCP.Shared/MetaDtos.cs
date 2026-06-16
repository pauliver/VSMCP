using System.Collections.Generic;

namespace VSMCP.Shared;

public sealed class HandshakeResult
{
    public int ProtocolMajor { get; set; }
    public int ProtocolMinor { get; set; }
    public string? ExtensionVersion { get; set; }
    public int VsProcessId { get; set; }
    public string? VsEdition { get; set; }
    public string? VsVersion { get; set; }
}

public sealed class PingResult
{
    public string Message { get; set; } = "pong";
    public long ServerTimestampMs { get; set; }
}

public sealed class VsStatus
{
    public bool SolutionOpen { get; set; }
    public string? SolutionPath { get; set; }
    public string? SolutionName { get; set; }
    public string? ActiveConfiguration { get; set; }
    public string? ActivePlatform { get; set; }
    public string? StartupProject { get; set; }
    public bool Debugging { get; set; }
    public string? DebugMode { get; set; }
    public List<string> LoadedProjects { get; set; } = new();
}

public sealed class VsInstance
{
    public int ProcessId { get; set; }
    public string PipeName { get; set; } = "";
    public string? MainWindowTitle { get; set; }
    public string? SolutionPath { get; set; }
}

public sealed class FocusResult
{
    public bool Focused { get; set; }
    public string? Hwnd { get; set; }
    public string? Reason { get; set; }
}

public sealed class AutoFocusResult
{
    public bool Enabled { get; set; }
}
