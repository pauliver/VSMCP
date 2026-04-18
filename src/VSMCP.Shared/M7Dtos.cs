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

public sealed class DumpDbgEngOptions
{
    /// <summary>Absolute path to a .dmp (or any file cdb.exe can load with -z).</summary>
    public string DumpPath { get; set; } = "";
    /// <summary>DbgEng command to run (e.g. "!analyze -v", "k", "lm", "~*k"). Do not include "q" — the wrapper appends it.</summary>
    public string Command { get; set; } = "";
    /// <summary>Optional extra symbol search path (semicolon-separated). Overrides _NT_SYMBOL_PATH for this call.</summary>
    public string? SymbolPath { get; set; }
    /// <summary>Optional timeout in milliseconds. Default 120000 (2 min). Clamped to [5000, 600000].</summary>
    public int? TimeoutMs { get; set; }
    /// <summary>Override cdb.exe path. Default: auto-discover under Windows Kits.</summary>
    public string? CdbPath { get; set; }
}

public sealed class DumpDbgEngResult
{
    public string Command { get; set; } = "";
    public string CdbPath { get; set; } = "";
    public int ExitCode { get; set; }
    public long ElapsedMs { get; set; }
    public string Output { get; set; } = "";
    public string? Stderr { get; set; }
    /// <summary>True when output was truncated because it exceeded the 1 MiB cap.</summary>
    public bool Truncated { get; set; }
    /// <summary>True when the process was killed after exceeding the timeout.</summary>
    public bool TimedOut { get; set; }
}
