using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using VSMCP.Shared;

namespace VSMCP.Vsix;

/// <summary>
/// Hooks DTE debugger events for the lifetime of a VS debug session and collects
/// them into a capped ring buffer. Must be constructed on the VS UI thread (COM
/// event subscriptions require it).
///
/// The ring buffer survives across multiple debug sessions in the same VS instance
/// so the AI can query events after the session ends.
/// </summary>
internal sealed class DiagEventCollector : IDisposable
{
    private const int MaxEvents = 200;
    private const int MaxFrames = 20;
    private const int CpuBufferSize = 300;      // 5 min at 1s intervals
    private const int CpuSampleIntervalMs = 1000;

    // Must be a field — if a local, COM GC's the event sink silently.
    private readonly EnvDTE.DebuggerEvents _dteEvents;
    private readonly EnvDTE80.DTE2 _dte;

    private readonly object _lock = new();
    private readonly List<DiagEventDetail> _events = new(MaxEvents + 1);
    private int _totalCollected;

    private readonly object _cpuLock = new();
    private readonly List<DiagCpuSample> _cpuSamples = new(CpuBufferSize + 1);
    private Timer? _cpuTimer;

    public DiagEventCollector(EnvDTE80.DTE2 dte)
    {
        _dte = dte;
        _dteEvents = dte.Events.DebuggerEvents;
        _dteEvents.OnExceptionThrown += OnExceptionThrown;
        _dteEvents.OnExceptionNotHandled += OnExceptionNotHandled;
        _dteEvents.OnEnterBreakMode += OnEnterBreakMode;
        _dteEvents.OnEnterRunMode += OnEnterRunMode;

        _cpuTimer = new Timer(SampleCpu, null, CpuSampleIntervalMs, CpuSampleIntervalMs);
    }

    // -------- Public API --------

    public DiagEventsResult GetEvents(string? filter, int maxResults)
    {
        var predicate = BuildPredicate(filter);
        lock (_lock)
        {
            var result = new DiagEventsResult { TotalCollected = _totalCollected };
            int cap = Math.Max(1, Math.Min(maxResults, _events.Count));
            for (int i = _events.Count - 1; i >= 0 && result.Events.Count < cap; i--)
            {
                var e = _events[i];
                if (predicate(e.Kind))
                {
                    result.Events.Add(new DiagEvent
                    {
                        Id = e.Id,
                        Kind = e.Kind,
                        TimestampMs = e.TimestampMs,
                        Summary = e.Summary,
                    });
                }
            }
            result.Events.Reverse(); // oldest first
            return result;
        }
    }

    public DiagEventDetail? GetDetail(string eventId)
    {
        lock (_lock)
        {
            foreach (var e in _events)
                if (e.Id == eventId) return e;
            return null;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _events.Clear();
            _totalCollected = 0;
        }
    }

    public DiagCpuTimelineResult GetCpuTimeline(int? windowMs)
    {
        lock (_cpuLock)
        {
            var cutoff = windowMs is > 0
                ? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - windowMs.Value
                : long.MinValue;

            var samples = new List<DiagCpuSample>();
            foreach (var s in _cpuSamples)
                if (s.TimestampMs >= cutoff)
                    samples.Add(s);

            int pid = 0;
            string? name = null;
            try
            {
                var proc = _dte.Debugger?.DebuggedProcesses;
                if (proc is { Count: > 0 })
                {
                    var first = proc.Item(1) as EnvDTE.Process;
                    pid = first?.ProcessID ?? 0;
                    name = first?.Name;
                }
            }
            catch { }

            return new DiagCpuTimelineResult
            {
                Samples = samples,
                ProcessId = pid,
                ProcessName = name,
                IntervalMs = CpuSampleIntervalMs,
            };
        }
    }

    // -------- DTE event handlers (fire on VS UI thread) --------

    private void OnExceptionThrown(string exceptionType, string name, int code, string description,
        ref EnvDTE.dbgExceptionAction exceptionAction)
    {
        AddEvent(new DiagEventDetail
        {
            Id = NewId(),
            Kind = DiagEventKind.ExceptionThrown,
            TimestampMs = Now(),
            Summary = string.IsNullOrEmpty(description)
                ? exceptionType
                : $"{exceptionType}: {description}",
            ExceptionType = exceptionType,
            ExceptionMessage = description,
            ExceptionCode = code,
            ThreadId = TryGetCurrentThreadId(),
            ThreadName = TryGetCurrentThreadName(),
            Frames = TryCapture(),
        });
    }

    private void OnExceptionNotHandled(string exceptionType, string name, int code, string description,
        ref EnvDTE.dbgExceptionAction exceptionAction)
    {
        AddEvent(new DiagEventDetail
        {
            Id = NewId(),
            Kind = DiagEventKind.ExceptionUnhandled,
            TimestampMs = Now(),
            Summary = string.IsNullOrEmpty(description)
                ? $"UNHANDLED {exceptionType}"
                : $"UNHANDLED {exceptionType}: {description}",
            ExceptionType = exceptionType,
            ExceptionMessage = description,
            ExceptionCode = code,
            ThreadId = TryGetCurrentThreadId(),
            ThreadName = TryGetCurrentThreadName(),
            Frames = TryCapture(),
        });
    }

    private void OnEnterBreakMode(EnvDTE.dbgEventReason reason, ref EnvDTE.dbgExecutionAction executionAction)
    {
        // Exceptions are captured by the dedicated handlers above; skip them here.
        if (reason == EnvDTE.dbgEventReason.dbgEventReasonExceptionThrown ||
            reason == EnvDTE.dbgEventReason.dbgEventReasonExceptionNotHandled)
            return;

        var kind = reason == EnvDTE.dbgEventReason.dbgEventReasonBreakpoint
            ? DiagEventKind.BreakpointHit
            : DiagEventKind.UserBreak;

        AddEvent(new DiagEventDetail
        {
            Id = NewId(),
            Kind = kind,
            TimestampMs = Now(),
            Summary = kind == DiagEventKind.BreakpointHit ? "Breakpoint hit" : "User break",
            ThreadId = TryGetCurrentThreadId(),
            ThreadName = TryGetCurrentThreadName(),
            Frames = TryCapture(),
        });
    }

    private void OnEnterRunMode(EnvDTE.dbgEventReason reason) { }

    // -------- CPU sampling (timer thread) --------

    private void SampleCpu(object? _)
    {
        int pid = 0;
        try
        {
            // Read pid from the current debugged process without touching DTE
            // (DTE requires UI thread; Process does not).
            // We store it when the debug session starts via OnEnterRunMode.
            // Fallback: iterate DebuggedProcesses is UI-thread-only so we skip here.
        }
        catch { }

        // Sample the most recently seen debugged PID, or skip if none.
        // We rely on a pid captured at run-mode entry rather than calling DTE here.
        var targetPid = Volatile.Read(ref _lastDebuggingPid);
        if (targetPid <= 0) return;

        try
        {
            var sw = Stopwatch.StartNew();
            using var proc = Process.GetProcessById(targetPid);
            var t1 = proc.TotalProcessorTime;
            var ts1 = DateTime.UtcNow;

            Thread.Sleep(100); // short sample window

            proc.Refresh();
            var t2 = proc.TotalProcessorTime;
            var ts2 = DateTime.UtcNow;

            var elapsed = (ts2 - ts1).TotalMilliseconds;
            var cpu = elapsed > 0
                ? (t2 - t1).TotalMilliseconds / elapsed * 100.0
                : 0.0;

            var sample = new DiagCpuSample
            {
                TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                CpuPercent = Math.Round(cpu, 1),
                WorkingSetBytes = proc.WorkingSet64,
            };

            lock (_cpuLock)
            {
                _cpuSamples.Add(sample);
                if (_cpuSamples.Count > CpuBufferSize)
                    _cpuSamples.RemoveAt(0);
            }
        }
        catch { }
    }

    private int _lastDebuggingPid;

    // Called from DTE OnEnterRunMode (UI thread) to record the PID so the timer can use it.
    internal void SetDebuggingPid(int pid) => Volatile.Write(ref _lastDebuggingPid, pid);

    // -------- Helpers --------

    private void AddEvent(DiagEventDetail detail)
    {
        lock (_lock)
        {
            _events.Add(detail);
            _totalCollected++;
            if (_events.Count > MaxEvents)
                _events.RemoveAt(0);
        }
    }

    private List<StackFrameInfo> TryCapture()
    {
        var result = new List<StackFrameInfo>();
        try
        {
            var thread = _dte.Debugger?.CurrentThread;
            if (thread is null) return result;
            int tid = thread.ID;
            int i = 0;
            foreach (EnvDTE.StackFrame frame in thread.StackFrames)
            {
                result.Add(new StackFrameInfo
                {
                    Index = i++,
                    ThreadId = tid,
                    FunctionName = frame.FunctionName ?? "",
                    Module = frame.Module,
                    Language = frame.Language,
                });
                if (i >= MaxFrames) break;
            }
        }
        catch { }
        return result;
    }

    private int TryGetCurrentThreadId()
    {
        try { return _dte.Debugger?.CurrentThread?.ID ?? 0; } catch { return 0; }
    }

    private string? TryGetCurrentThreadName()
    {
        try { return _dte.Debugger?.CurrentThread?.Name; } catch { return null; }
    }

    private static string NewId() => Guid.NewGuid().ToString("N");
    private static long Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private static Func<DiagEventKind, bool> BuildPredicate(string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter) || filter.Equals("all", StringComparison.OrdinalIgnoreCase))
            return _ => true;

        // "exception" matches both thrown and unhandled
        if (filter.Equals("exception", StringComparison.OrdinalIgnoreCase))
            return k => k == DiagEventKind.ExceptionThrown || k == DiagEventKind.ExceptionUnhandled;

        return filter.ToLowerInvariant() switch
        {
            "exceptionthrown"    => k => k == DiagEventKind.ExceptionThrown,
            "exceptionunhandled" => k => k == DiagEventKind.ExceptionUnhandled,
            "breakpointhit"      => k => k == DiagEventKind.BreakpointHit,
            "breakpoint"         => k => k == DiagEventKind.BreakpointHit,
            "userbreak"          => k => k == DiagEventKind.UserBreak,
            _                    => _ => true,
        };
    }

    public void Dispose()
    {
        _cpuTimer?.Dispose();
        _cpuTimer = null;

        try
        {
            _dteEvents.OnExceptionThrown -= OnExceptionThrown;
            _dteEvents.OnExceptionNotHandled -= OnExceptionNotHandled;
            _dteEvents.OnEnterBreakMode -= OnEnterBreakMode;
            _dteEvents.OnEnterRunMode -= OnEnterRunMode;
        }
        catch { }
    }
}
