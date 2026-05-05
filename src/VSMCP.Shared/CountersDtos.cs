using System.Collections.Generic;

namespace VSMCP.Shared;

/// <summary>Result of <c>counters.subscribe</c>: a handle to an active polling subscription.</summary>
public sealed class CountersSubscriptionHandle
{
    public string SubscriptionId { get; set; } = "";
    public int Pid { get; set; }
    public string ProcessName { get; set; } = "";
    public int SampleMs { get; set; }
    public int BufferSize { get; set; }
    public string StartedUtc { get; set; } = "";
}

/// <summary>Result of <c>counters.read</c>: any samples buffered since the last read.</summary>
public sealed class CountersReadResult
{
    public string SubscriptionId { get; set; } = "";
    public List<CountersSnapshot> Samples { get; set; } = new();
    /// <summary>Number of samples that had to be dropped because the ring buffer wrapped.</summary>
    public long Dropped { get; set; }
    /// <summary>True when the polling task has ended (process exited, subscription canceled).</summary>
    public bool Ended { get; set; }
    /// <summary>Reason the subscription ended, if any (process_exit, canceled, error).</summary>
    public string? EndReason { get; set; }
}

/// <summary>Result of <c>counters.unsubscribe</c>.</summary>
public sealed class CountersUnsubscribeResult
{
    public string SubscriptionId { get; set; } = "";
    public long TotalSamples { get; set; }
    public long Dropped { get; set; }
    public double DurationSeconds { get; set; }
}
public sealed class CountersSnapshot
{
    public int Pid { get; set; }
    public string Name { get; set; } = "";
    /// <summary>Sampling window in milliseconds used to compute <see cref="CpuPercent"/>.</summary>
    public int SampleMs { get; set; }
    /// <summary>CPU usage across the sampling window, as a percentage of one logical core (0..100 * logicalCpuCount).</summary>
    public double CpuPercent { get; set; }
    /// <summary>CPU usage normalized to total machine CPU (0..100 regardless of core count).</summary>
    public double CpuPercentNormalized { get; set; }
    public long WorkingSetBytes { get; set; }
    public long PrivateMemoryBytes { get; set; }
    public long VirtualMemoryBytes { get; set; }
    public long PagedMemoryBytes { get; set; }
    public int ThreadCount { get; set; }
    public int HandleCount { get; set; }
    /// <summary>Total CPU time accumulated since process start, milliseconds.</summary>
    public long TotalCpuTimeMs { get; set; }
    public long UptimeMs { get; set; }
    public int LogicalProcessorCount { get; set; }
}
