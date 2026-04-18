using System.Collections.Generic;

namespace VSMCP.Shared;

/// <summary>Options for <c>trace.start</c>: which ETW providers to enable and where to write the .etl.</summary>
public sealed class TraceStartOptions
{
    /// <summary>
    /// User-mode ETW providers to enable. Each entry is either a GUID or a registered provider name
    /// (e.g. "Microsoft-Windows-DotNETRuntime", "Microsoft-Windows-Kernel-Process"). Empty = no user providers.
    /// </summary>
    public List<TraceProviderSpec> Providers { get; set; } = new();

    /// <summary>
    /// Kernel keywords to enable (e.g. "Process,ImageLoad,Thread"). Null/empty = no kernel provider.
    /// Valid names map to <c>KernelTraceEventParser.Keywords</c>. Requires the session to run as admin.
    /// </summary>
    public List<string>? KernelKeywords { get; set; }

    /// <summary>Absolute path of the .etl to write. Omit to auto-generate under %TEMP%.</summary>
    public string? OutputPath { get; set; }

    /// <summary>Optional circular buffer size in MB. Omit to use TraceEvent's default.</summary>
    public int? BufferSizeMB { get; set; }
}

public sealed class TraceProviderSpec
{
    /// <summary>Provider name ("Microsoft-Windows-DotNETRuntime") or GUID ("{e13c0d23-ccbc-4e12-931b-d9cc2eee27e4}").</summary>
    public string Name { get; set; } = "";
    /// <summary>Event level 1..5. 0 means "default/informational".</summary>
    public int Level { get; set; } = 4;
    /// <summary>Keyword bitmask. 0 means "all keywords".</summary>
    public long Keywords { get; set; } = -1;
}

public sealed class TraceStartResult
{
    public string SessionId { get; set; } = "";
    public string OutputPath { get; set; } = "";
    public string StartedUtc { get; set; } = "";
    /// <summary>Whether a kernel provider was enabled in addition to user providers.</summary>
    public bool KernelEnabled { get; set; }
    public List<string> ProvidersEnabled { get; set; } = new();
}

public sealed class TraceStopResult
{
    public string SessionId { get; set; } = "";
    public string OutputPath { get; set; } = "";
    public long BytesWritten { get; set; }
    public double DurationSeconds { get; set; }
}

public sealed class TraceEventCount
{
    public string Provider { get; set; } = "";
    public string EventName { get; set; } = "";
    public long Count { get; set; }
}

public sealed class TraceReport
{
    public string Path { get; set; } = "";
    public double DurationSeconds { get; set; }
    public long TotalEvents { get; set; }
    public List<TraceEventCount> TopEvents { get; set; } = new();
    public Dictionary<string, long> EventsByProvider { get; set; } = new();
    /// <summary>True when the .etl could not be read or contained no events.</summary>
    public bool Empty { get; set; }
}
