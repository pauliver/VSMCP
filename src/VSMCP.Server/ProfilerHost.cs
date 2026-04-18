using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;
using Microsoft.Diagnostics.Tracing.Stacks;
using VSMCP.Shared;

namespace VSMCP.Server;

/// <summary>
/// Owns EventPipe profiling sessions. Sessions are identified by a GUID string;
/// the raw .nettrace is streamed to a file while the session runs.
/// </summary>
public sealed class ProfilerHost : IDisposable
{
    private readonly ConcurrentDictionary<string, ActiveSession> _sessions = new(StringComparer.Ordinal);

    public ProfilerStartResult Start(int pid, ProfilerMode mode, string? outputPath)
    {
        if (pid <= 0) throw new ArgumentOutOfRangeException(nameof(pid), "Pid must be > 0.");

        var outPath = string.IsNullOrWhiteSpace(outputPath)
            ? Path.Combine(Path.GetTempPath(), $"vsmcp-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{pid}-{mode}.nettrace")
            : Path.GetFullPath(outputPath!);
        var parent = Path.GetDirectoryName(outPath);
        if (!string.IsNullOrEmpty(parent) && !Directory.Exists(parent))
            throw new DirectoryNotFoundException($"Parent directory does not exist: {parent}");

        var providers = BuildProviders(mode);
        var client = new DiagnosticsClient(pid);
        EventPipeSession session;
        try
        {
            session = client.StartEventPipeSession(providers, requestRundown: true);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to start EventPipe session on pid {pid}. Target must be a running .NET 5+ process reachable over the diagnostic port. Inner: {ex.Message}", ex);
        }

        var sessionId = Guid.NewGuid().ToString("N");
        var started = DateTimeOffset.UtcNow;

        var stream = new FileStream(outPath, FileMode.Create, FileAccess.Write, FileShare.Read);
        var cts = new CancellationTokenSource();
        var copyTask = Task.Run(async () =>
        {
            try
            {
                await session.EventStream.CopyToAsync(stream, 64 * 1024, cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { /* normal on stop */ }
            catch (Exception) { /* swallow; caller inspects file size */ }
            finally
            {
                await stream.FlushAsync().ConfigureAwait(false);
                stream.Dispose();
            }
        }, cts.Token);

        _sessions[sessionId] = new ActiveSession(sessionId, pid, mode, outPath, started, session, copyTask, cts);

        return new ProfilerStartResult
        {
            SessionId = sessionId,
            Pid = pid,
            Mode = mode,
            OutputPath = outPath,
            StartedUtc = started.ToString("o"),
        };
    }

    public async Task<ProfilerStopResult> StopAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (!_sessions.TryRemove(sessionId, out var s))
            throw new InvalidOperationException($"No active profiler session with id '{sessionId}'.");

        try { s.Session.Stop(); } catch { /* session may already be closing */ }
        try { await s.CopyTask.WaitAsync(cancellationToken).ConfigureAwait(false); } catch { }
        s.CancelTokenSource.Dispose();
        s.Session.Dispose();

        long size = 0;
        try { size = new FileInfo(s.OutputPath).Length; } catch { }

        return new ProfilerStopResult
        {
            SessionId = sessionId,
            OutputPath = s.OutputPath,
            BytesWritten = size,
            DurationSeconds = (DateTimeOffset.UtcNow - s.StartedUtc).TotalSeconds,
        };
    }

    public ProfilerReport Report(string tracePath, int top)
    {
        if (string.IsNullOrWhiteSpace(tracePath)) throw new ArgumentException("Trace path is required.", nameof(tracePath));
        tracePath = Path.GetFullPath(tracePath);
        if (!File.Exists(tracePath)) throw new FileNotFoundException("Trace file not found.", tracePath);
        if (top <= 0) top = 20;
        if (top > 1000) top = 1000;

        var report = new ProfilerReport { Path = tracePath };

        string etlx;
        try
        {
            etlx = TraceLog.CreateFromEventPipeDataFile(tracePath);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Could not convert .nettrace to TraceLog: {ex.Message}", ex);
        }

        using var traceLog = new TraceLog(etlx);
        report.DurationSeconds = traceLog.SessionDuration.TotalSeconds;

        // Aggregate CPU samples by leaf method. ClrSamplingProfilerSampledProfile events carry a CallStackIndex
        // that TraceLog can resolve; the leaf (top of stack) is the self-time bucket for that sample.
        var byMethod = new Dictionary<string, (long count, string module)>(StringComparer.Ordinal);
        long total = 0;

        foreach (var ev in traceLog.Events)
        {
            // Sample profiler events show up in traceLog as generic events when parsed through EventPipe;
            // match by TaskName to stay provider-agnostic.
            if (ev.ProviderName != "Microsoft-DotNETCore-SampleProfiler") continue;
            if (ev.EventName != "Thread/Sample") continue;

            var callStack = ev.CallStack();
            if (callStack is null) continue;

            var method = callStack.CodeAddress.Method;
            var name = method?.FullMethodName ?? ("0x" + callStack.CodeAddress.Address.ToString("X"));
            var module = method?.MethodModuleFile?.Name ?? "?";
            if (byMethod.TryGetValue(name, out var prev))
                byMethod[name] = (prev.count + 1, module);
            else
                byMethod[name] = (1, module);
            total++;
        }

        report.TotalSamples = total;
        if (total == 0)
        {
            report.Empty = true;
            return report;
        }

        var ordered = new List<KeyValuePair<string, (long count, string module)>>(byMethod);
        ordered.Sort((a, b) => b.Value.count.CompareTo(a.Value.count));
        for (int i = 0; i < Math.Min(top, ordered.Count); i++)
        {
            var e = ordered[i];
            report.Hot.Add(new HotFunction
            {
                FunctionName = e.Key,
                Module = e.Value.module,
                SampleCount = e.Value.count,
                PercentOfSamples = 100.0 * e.Value.count / total,
            });
        }
        return report;
    }

    public IReadOnlyList<string> ActiveSessionIds()
    {
        var keys = new List<string>(_sessions.Keys);
        keys.Sort(StringComparer.Ordinal);
        return keys;
    }

    public void Dispose()
    {
        foreach (var kv in _sessions)
        {
            try { kv.Value.CancelTokenSource.Cancel(); } catch { }
            try { kv.Value.Session.Dispose(); } catch { }
        }
        _sessions.Clear();
    }

    private static List<EventPipeProvider> BuildProviders(ProfilerMode mode) => mode switch
    {
        ProfilerMode.CpuSampling => new List<EventPipeProvider>
        {
            new("Microsoft-DotNETCore-SampleProfiler", EventLevel.Informational),
            new("Microsoft-Windows-DotNETRuntime", EventLevel.Informational,
                (long)(ClrTraceEventParser.Keywords.Loader | ClrTraceEventParser.Keywords.Jit | ClrTraceEventParser.Keywords.JittedMethodILToNativeMap | ClrTraceEventParser.Keywords.NGen | ClrTraceEventParser.Keywords.Stack)),
        },
        ProfilerMode.Allocations => new List<EventPipeProvider>
        {
            new("Microsoft-Windows-DotNETRuntime", EventLevel.Informational,
                (long)(ClrTraceEventParser.Keywords.GC | ClrTraceEventParser.Keywords.GCSampledObjectAllocationHigh | ClrTraceEventParser.Keywords.Type | ClrTraceEventParser.Keywords.Stack)),
        },
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown profiler mode."),
    };

    private sealed record ActiveSession(
        string SessionId,
        int Pid,
        ProfilerMode Mode,
        string OutputPath,
        DateTimeOffset StartedUtc,
        EventPipeSession Session,
        Task CopyTask,
        CancellationTokenSource CancelTokenSource);
}
