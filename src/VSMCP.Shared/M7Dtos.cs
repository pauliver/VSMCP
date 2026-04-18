using System.Collections.Generic;

namespace VSMCP.Shared;

public sealed class DumpOpenOptions
{
    /// <summary>Absolute path to a .dmp / .mdmp / minidump file.</summary>
    public string Path { get; set; } = "";
    /// <summary>Extra symbol search paths (semicolon-separated) to layer on top of VS's configured SymbolPath for this session. Reserved — not applied in v1.</summary>
    public string? SymbolPath { get; set; }
    /// <summary>Extra source search paths (semicolon-separated). Reserved — not applied in v1.</summary>
    public string? SourcePath { get; set; }
}

public sealed class DumpOpenResult
{
    public string Path { get; set; } = "";
    /// <summary>Reported at load time if the engine surfaced a faulting/current thread id.</summary>
    public int? FaultingThreadId { get; set; }
    /// <summary>Text of the exception that surfaced on dump load, when available.</summary>
    public string? ExceptionMessage { get; set; }
    /// <summary>Number of modules visible after the dump loaded (best-effort — modules that loaded before the extension bound the event sink won't appear).</summary>
    public int ModuleCount { get; set; }
}

public sealed class DumpSummaryResult
{
    public int? FaultingThreadId { get; set; }
    public string? FaultingThreadName { get; set; }
    public string? ExceptionMessage { get; set; }
    public DebugMode Mode { get; set; }
    public int ModuleCount { get; set; }
    public int ManagedModuleCount { get; set; }
    public int NativeModuleCount { get; set; }
    public List<ModuleInfo> Modules { get; set; } = new();
    /// <summary>Process id reported by the debug engine, if any.</summary>
    public int? ProcessId { get; set; }
    public string? ProcessName { get; set; }
}

public sealed class DumpSaveOptions
{
    /// <summary>Target process id. Must be accessible to devenv.exe (same user, or devenv elevated).</summary>
    public int Pid { get; set; }
    /// <summary>Absolute destination path. Parent directory must exist.</summary>
    public string Path { get; set; } = "";
    /// <summary>When true, writes a full-memory dump (MiniDumpWithFullMemory + typical heap/handle/token flags). False = minidump (stacks + modules).</summary>
    public bool Full { get; set; } = true;
}

public sealed class DumpSaveResult
{
    public string Path { get; set; } = "";
    public long BytesWritten { get; set; }
    public bool Full { get; set; }
}
