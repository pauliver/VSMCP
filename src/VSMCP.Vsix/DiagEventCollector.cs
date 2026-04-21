using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Debugger.Interop;
using VSMCP.Shared;

namespace VSMCP.Vsix;

/// <summary>
/// Hooks DTE debugger events for the lifetime of the VS instance.
/// Collects exceptions, breakpoints, and user breaks into a ring buffer;
/// runs a 1-second CPU/RSS sampler for the debugged process.
///
/// Must be constructed on the VS UI thread (DTE COM event subscriptions require it).
/// </summary>
internal sealed class DiagEventCollector : IDisposable
{
    private const int MaxEvents = 200;
    private const int MaxFrames = 20;
    private const int CpuBufferSize = 300;      // 5 min at 1s intervals
    private const int CpuSampleIntervalMs = 1000;

    // Must be a field — if a local, COM GC's the DTE event sink silently.
    private readonly EnvDTE.DebuggerEvents _dteEvents;
    private readonly EnvDTE80.DTE2 _dte;
    private readonly ModuleTracker? _moduleTracker;

    private readonly object _lock = new();
    private readonly List<DiagEventDetail> _events = new(MaxEvents + 1);
    private int _totalCollected;
    // Pulsed whenever a new event is added so WaitForEventsAsync can wake up.
    private readonly SemaphoreSlim _newEvent = new(0, 1);

    private readonly object _cpuLock = new();
    private readonly List<DiagCpuSample> _cpuSamples = new(CpuBufferSize + 1);
    private Timer? _cpuTimer;
    private int _lastDebuggingPid;

    public DiagEventCollector(EnvDTE80.DTE2 dte, ModuleTracker? moduleTracker)
    {
        _dte = dte;
        _moduleTracker = moduleTracker;

        _dteEvents = dte.Events.DebuggerEvents;
        _dteEvents.OnExceptionThrown += OnExceptionThrown;
        _dteEvents.OnExceptionNotHandled += OnExceptionNotHandled;
        _dteEvents.OnEnterBreakMode += OnEnterBreakMode;
        _dteEvents.OnEnterRunMode += OnEnterRunMode;

        _cpuTimer = new Timer(SampleCpu, null, CpuSampleIntervalMs, CpuSampleIntervalMs);
    }

    // -------- Public API --------

    public DiagEventsResult GetEvents(string? filter, int maxResults, long sinceTimestampMs = 0)
    {
        var predicate = BuildPredicate(filter);
        lock (_lock)
        {
            var result = new DiagEventsResult { TotalCollected = _totalCollected };
            int cap = Math.Max(1, Math.Min(maxResults, _events.Count));
            for (int i = _events.Count - 1; i >= 0 && result.Events.Count < cap; i--)
            {
                var e = _events[i];
                if (e.TimestampMs <= sinceTimestampMs) break; // events are insertion-ordered; everything earlier is older
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
            result.LatestTimestampMs = result.Events.Count > 0
                ? result.Events[result.Events.Count - 1].TimestampMs
                : sinceTimestampMs;
            return result;
        }
    }

    /// <summary>
    /// Long-polls for events newer than <paramref name="sinceTimestampMs"/>, returning as soon as any
    /// arrive or <paramref name="timeoutMs"/> elapses.
    /// </summary>
    public async Task<DiagEventsResult> WaitForEventsAsync(
        string? filter, int maxResults, long sinceTimestampMs, int timeoutMs, CancellationToken cancellationToken)
    {
        timeoutMs = Math.Max(100, Math.Min(timeoutMs, 30_000));
        var deadline = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + timeoutMs;

        while (true)
        {
            var result = GetEvents(filter, maxResults, sinceTimestampMs);
            if (result.Events.Count > 0) return result;

            var remaining = (int)(deadline - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            if (remaining <= 0) return result;

            // Wait for a signal (pulsed in AddEvent) or timeout, whichever comes first.
            await _newEvent.WaitAsync(Math.Min(remaining, 1000), cancellationToken).ConfigureAwait(false);
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
                var procs = _dte.Debugger?.DebuggedProcesses;
                if (procs is { Count: > 0 })
                {
                    var first = procs.Item(1) as EnvDTE.Process;
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
            Summary = string.IsNullOrEmpty(description) ? exceptionType : $"{exceptionType}: {description}",
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
        // Exceptions handled by the dedicated handlers above.
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

    private void OnEnterRunMode(EnvDTE.dbgEventReason reason)
    {
        try
        {
            var procs = _dte.Debugger?.DebuggedProcesses;
            if (procs is { Count: > 0 })
                SetDebuggingPid((procs.Item(1) as EnvDTE.Process)?.ProcessID ?? 0);
        }
        catch { }
    }

    // -------- CPU sampling (timer thread) --------

    private void SampleCpu(object? _)
    {
        var targetPid = Volatile.Read(ref _lastDebuggingPid);
        if (targetPid <= 0) return;

        try
        {
            using var proc = Process.GetProcessById(targetPid);
            var t1 = proc.TotalProcessorTime;
            var ts1 = DateTime.UtcNow;

            Thread.Sleep(100);

            proc.Refresh();
            var t2 = proc.TotalProcessorTime;
            var ts2 = DateTime.UtcNow;

            var elapsed = (ts2 - ts1).TotalMilliseconds;
            var cpu = elapsed > 0 ? (t2 - t1).TotalMilliseconds / elapsed * 100.0 : 0.0;

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

    internal void SetDebuggingPid(int pid) => Volatile.Write(ref _lastDebuggingPid, pid);

    private void AddEvent(DiagEventDetail detail)
    {
        lock (_lock)
        {
            _events.Add(detail);
            _totalCollected++;
            if (_events.Count > MaxEvents)
                _events.RemoveAt(0);
        }
        if (_newEvent.CurrentCount == 0)
            try { _newEvent.Release(); } catch { }
    }

    // -------- Frame capture --------

    private List<StackFrameInfo> TryCapture()
    {
        // Prefer native IDebugThread2 path (has file + line); fall back to DTE (has none).
        var nativeThread = _moduleTracker?.LastThread;
        if (nativeThread is not null)
        {
            var frames = TryCaptureNative(nativeThread);
            if (frames.Count > 0) return frames;
        }
        return TryCaptureDte();
    }

    private static List<StackFrameInfo> TryCaptureNative(IDebugThread2 thread)
    {
        var result = new List<StackFrameInfo>();
        try
        {
            int tid = 0;
            try
            {
                thread.GetThreadId(out var nativeTid);
                tid = (int)nativeTid;
            }
            catch { }

            var flags = enum_FRAMEINFO_FLAGS.FIF_FUNCNAME
                      | enum_FRAMEINFO_FLAGS.FIF_MODULE
                      | enum_FRAMEINFO_FLAGS.FIF_LANGUAGE
                      | enum_FRAMEINFO_FLAGS.FIF_FRAME;

            if (thread.EnumFrameInfo(flags, 10, out var frameEnum) != Microsoft.VisualStudio.VSConstants.S_OK || frameEnum is null)
                return result;

            var buf = new FRAMEINFO[1];
            uint fetched = 0;
            int i = 0;
            while (frameEnum.Next(1, buf, ref fetched) == Microsoft.VisualStudio.VSConstants.S_OK && fetched == 1)
            {
                var fi = buf[0];
                string? file = null;
                int? line = null;

                if (fi.m_pFrame is not null)
                {
                    try
                    {
                        if (fi.m_pFrame.GetDocumentContext(out var docCtx) == Microsoft.VisualStudio.VSConstants.S_OK && docCtx is not null)
                        {
                            var begin = new TEXT_POSITION[1];
                            var end = new TEXT_POSITION[1];
                            if (docCtx.GetStatementRange(begin, end) == Microsoft.VisualStudio.VSConstants.S_OK)
                                line = (int)(begin[0].dwLine + 1); // 0-based → 1-based

                            if (docCtx.GetDocument(out var doc) == Microsoft.VisualStudio.VSConstants.S_OK && doc is not null)
                            {
                                doc.GetName(enum_GETNAME_TYPE.GN_FILENAME, out file);
                            }
                        }
                    }
                    catch { }
                }

                result.Add(new StackFrameInfo
                {
                    Index = i++,
                    ThreadId = tid,
                    FunctionName = fi.m_bstrFuncName ?? "",
                    Module = string.IsNullOrEmpty(fi.m_bstrModule) ? null : fi.m_bstrModule,
                    Language = string.IsNullOrEmpty(fi.m_bstrLanguage) ? null : fi.m_bstrLanguage,
                    File = file,
                    Line = line,
                });

                if (i >= MaxFrames) break;
            }
        }
        catch { }
        return result;
    }

    private List<StackFrameInfo> TryCaptureDte()
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
        _newEvent.Dispose();

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
