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
