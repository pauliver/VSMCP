using System.Collections.Generic;

namespace VSMCP.Shared;

public enum ProfilerMode
{
    CpuSampling = 0,
    Allocations = 1,
}

public sealed class ProfilerStartResult
{
    public string SessionId { get; set; } = "";
    public int Pid { get; set; }
    public ProfilerMode Mode { get; set; }
    public string OutputPath { get; set; } = "";
    public string StartedUtc { get; set; } = "";
}

public sealed class ProfilerStopResult
{
    public string SessionId { get; set; } = "";
    public string OutputPath { get; set; } = "";
    public long BytesWritten { get; set; }
    public double DurationSeconds { get; set; }
}

public sealed class HotFunction
{
    public string Module { get; set; } = "";
    public string FunctionName { get; set; } = "";
    public long SampleCount { get; set; }
    public double PercentOfSamples { get; set; }
}

public sealed class ProfilerReport
{
    public string Path { get; set; } = "";
    public long TotalSamples { get; set; }
    public double DurationSeconds { get; set; }
    public List<HotFunction> Hot { get; set; } = new();
    /// <summary>True when the trace was unresolvable (wrong format, empty, truncated).</summary>
    public bool Empty { get; set; }
}
