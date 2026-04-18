using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using VSMCP.Shared;

namespace VSMCP.Server;

/// <summary>
/// Background poller that samples per-process counters at a fixed cadence and buffers the results
/// for out-of-band retrieval via <see cref="Read"/>. Subscriptions self-terminate when the target
/// process exits.
/// </summary>
public sealed class CountersSubscriptionHost : IDisposable
{
    private const int DefaultBufferSize = 256;

    private readonly ConcurrentDictionary<string, Subscription> _subs = new(StringComparer.Ordinal);

    public CountersSubscriptionHandle Subscribe(int pid, int sampleMs)
    {
        if (pid <= 0) throw new ArgumentOutOfRangeException(nameof(pid), "Pid must be > 0.");
        if (sampleMs < 100) sampleMs = 100;
        if (sampleMs > 60_000) sampleMs = 60_000;

        Process process;
        try { process = Process.GetProcessById(pid); }
        catch (ArgumentException) { throw new InvalidOperationException($"No process with pid {pid}."); }

        var id = Guid.NewGuid().ToString("N");
        var started = DateTimeOffset.UtcNow;
        var sub = new Subscription(id, pid, process.ProcessName, sampleMs, DefaultBufferSize, started, process);
        _subs[id] = sub;

        sub.Task = Task.Run(() => PollLoop(sub, sub.CancelTokenSource.Token));

        return new CountersSubscriptionHandle
        {
            SubscriptionId = id,
            Pid = pid,
            ProcessName = sub.ProcessName,
            SampleMs = sampleMs,
            BufferSize = DefaultBufferSize,
            StartedUtc = started.ToString("o"),
        };
    }

    public CountersReadResult Read(string subscriptionId, int maxSamples)
    {
        if (!_subs.TryGetValue(subscriptionId, out var sub))
            throw new InvalidOperationException($"No counters subscription '{subscriptionId}'.");
        if (maxSamples <= 0) maxSamples = DefaultBufferSize;
        if (maxSamples > DefaultBufferSize) maxSamples = DefaultBufferSize;

        var result = new CountersReadResult { SubscriptionId = subscriptionId };
        lock (sub.BufferLock)
        {
            while (result.Samples.Count < maxSamples && sub.Buffer.TryDequeue(out var s))
                result.Samples.Add(s);
            result.Dropped = Interlocked.Read(ref sub.Dropped);
        }
        result.Ended = sub.Ended;
        result.EndReason = sub.EndReason;
        return result;
    }

    public CountersUnsubscribeResult Unsubscribe(string subscriptionId)
    {
        if (!_subs.TryRemove(subscriptionId, out var sub))
            throw new InvalidOperationException($"No counters subscription '{subscriptionId}'.");

        sub.CancelTokenSource.Cancel();
        try { sub.Task?.Wait(TimeSpan.FromSeconds(3)); } catch { }
        sub.CancelTokenSource.Dispose();
        try { sub.Process.Dispose(); } catch { }

        return new CountersUnsubscribeResult
        {
            SubscriptionId = subscriptionId,
            TotalSamples = Interlocked.Read(ref sub.TotalSamples),
            Dropped = Interlocked.Read(ref sub.Dropped),
            DurationSeconds = (DateTimeOffset.UtcNow - sub.StartedUtc).TotalSeconds,
        };
    }

    public IReadOnlyList<string> ActiveSubscriptionIds()
    {
        var keys = new List<string>(_subs.Keys);
        keys.Sort(StringComparer.Ordinal);
        return keys;
    }

    public void Dispose()
    {
        foreach (var kv in _subs)
        {
            try { kv.Value.CancelTokenSource.Cancel(); } catch { }
            try { kv.Value.Process.Dispose(); } catch { }
        }
        _subs.Clear();
    }

    private static async Task PollLoop(Subscription sub, CancellationToken ct)
    {
        var process = sub.Process;
        var logicalCpus = Environment.ProcessorCount;
        try
        {
            TimeSpan prev;
            try { prev = process.TotalProcessorTime; }
            catch (Exception ex) { sub.Ended = true; sub.EndReason = "access_denied: " + ex.Message; return; }
            var prevT = DateTime.UtcNow;

            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(sub.SampleMs, ct).ConfigureAwait(false);
                if (process.HasExited) { sub.Ended = true; sub.EndReason = "process_exit"; return; }

                process.Refresh();
                TimeSpan now;
                try { now = process.TotalProcessorTime; }
                catch (Exception ex) { sub.Ended = true; sub.EndReason = "error: " + ex.Message; return; }
                var nowT = DateTime.UtcNow;

                var wallMs = (nowT - prevT).TotalMilliseconds;
                var cpuMsDelta = (now - prev).TotalMilliseconds;
                var perCore = wallMs > 0 ? (cpuMsDelta / wallMs) * 100.0 : 0.0;
                var normalized = logicalCpus > 0 ? perCore / logicalCpus : perCore;

                var snap = new CountersSnapshot
                {
                    Pid = sub.Pid,
                    Name = sub.ProcessName,
                    SampleMs = sub.SampleMs,
                    CpuPercent = perCore,
                    CpuPercentNormalized = normalized,
                    LogicalProcessorCount = logicalCpus,
                    TotalCpuTimeMs = (long)now.TotalMilliseconds,
                };
                try { snap.WorkingSetBytes = process.WorkingSet64; } catch { }
                try { snap.PrivateMemoryBytes = process.PrivateMemorySize64; } catch { }
                try { snap.VirtualMemoryBytes = process.VirtualMemorySize64; } catch { }
                try { snap.PagedMemoryBytes = process.PagedMemorySize64; } catch { }
                try { snap.ThreadCount = process.Threads.Count; } catch { }
                try { snap.HandleCount = process.HandleCount; } catch { }
                try { snap.UptimeMs = (long)(DateTime.Now - process.StartTime).TotalMilliseconds; } catch { }

                Enqueue(sub, snap);
                prev = now;
                prevT = nowT;
            }
            sub.Ended = true;
            sub.EndReason ??= "canceled";
        }
        catch (OperationCanceledException)
        {
            sub.Ended = true;
            sub.EndReason ??= "canceled";
        }
        catch (Exception ex)
        {
            sub.Ended = true;
            sub.EndReason = "error: " + ex.Message;
        }
    }

    private static void Enqueue(Subscription sub, CountersSnapshot snap)
    {
        lock (sub.BufferLock)
        {
            sub.Buffer.Enqueue(snap);
            Interlocked.Increment(ref sub.TotalSamples);
            while (sub.Buffer.Count > sub.BufferSize)
            {
                sub.Buffer.TryDequeue(out _);
                Interlocked.Increment(ref sub.Dropped);
            }
        }
    }

    private sealed class Subscription
    {
        public string Id { get; }
        public int Pid { get; }
        public string ProcessName { get; }
        public int SampleMs { get; }
        public int BufferSize { get; }
        public DateTimeOffset StartedUtc { get; }
        public Queue<CountersSnapshot> Buffer { get; } = new();
        public object BufferLock { get; } = new();
        public long TotalSamples;
        public long Dropped;
        public CancellationTokenSource CancelTokenSource { get; } = new();
        public Task? Task;
        public volatile bool Ended;
        public string? EndReason;
        public Process Process { get; }

        public Subscription(string id, int pid, string name, int sampleMs, int bufferSize, DateTimeOffset started, Process process)
        {
            Id = id;
            Pid = pid;
            ProcessName = name;
            SampleMs = sampleMs;
            BufferSize = bufferSize;
            StartedUtc = started;
            Process = process;
        }
    }
}
