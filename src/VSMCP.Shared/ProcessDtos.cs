using System.Collections.Generic;

namespace VSMCP.Shared;

public sealed class ProcessListFilter
{
    /// <summary>Substring match against process name (case-insensitive). Null = no filter.</summary>
    public string? NameContains { get; set; }
    /// <summary>When true, only processes owned by the same Windows session as devenv.exe are returned.</summary>
    public bool CurrentSessionOnly { get; set; } = true;
}

public sealed class ProcessInfoRow
{
    public int Pid { get; set; }
    public string Name { get; set; } = "";
    public int SessionId { get; set; }
    public long WorkingSetBytes { get; set; }
    public long PrivateMemoryBytes { get; set; }
    public int ThreadCount { get; set; }
    /// <summary>Path to the main module when readable; null for protected processes (lsass, system, etc.).</summary>
    public string? MainModulePath { get; set; }
    /// <summary>ISO-8601 UTC start time, null if inaccessible.</summary>
    public string? StartedUtc { get; set; }
}

public sealed class ProcessListResult
{
    public List<ProcessInfoRow> Processes { get; set; } = new();
    /// <summary>Count of processes skipped because of access denied (usually elevated/system processes).</summary>
    public int Inaccessible { get; set; }
}

