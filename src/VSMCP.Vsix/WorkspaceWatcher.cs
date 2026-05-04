using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using VSMCP.Shared;

namespace VSMCP.Vsix;

/// <summary>
/// Long-lived collector that fans DTE events into a ring buffer. Tools can pull a snapshot
/// (workspace.events_list) or long-poll (workspace.watch) for events newer than a cursor.
/// Modeled on DiagEventCollector — same SemaphoreSlim-pulse pattern.
/// </summary>
internal sealed class WorkspaceWatcher : IDisposable
{
    private const int MaxEvents = 500;

    private readonly EnvDTE80.DTE2 _dte;
    private readonly EnvDTE.DocumentEvents? _docEvents;
    private readonly EnvDTE.SolutionEvents? _solEvents;
    private readonly EnvDTE.BuildEvents? _buildEvents;
    private readonly EnvDTE.DebuggerEvents? _dbgEvents;
    private readonly EnvDTE.WindowEvents? _winEvents;

    private readonly object _lock = new();
    private readonly List<WorkspaceEvent> _events = new(MaxEvents + 1);
    private int _totalCollected;
    private readonly SemaphoreSlim _newEvent = new(0, 1);

    public WorkspaceWatcher(EnvDTE80.DTE2 dte)
    {
        _dte = dte;
        _docEvents = dte.Events.DocumentEvents;
        _solEvents = dte.Events.SolutionEvents;
        _buildEvents = dte.Events.BuildEvents;
        _dbgEvents = dte.Events.DebuggerEvents;
        _winEvents = dte.Events.WindowEvents;

        if (_docEvents is not null)
        {
            _docEvents.DocumentSaved += OnDocumentSaved;
            _docEvents.DocumentOpened += OnDocumentOpened;
            _docEvents.DocumentClosing += OnDocumentClosing;
        }
        if (_buildEvents is not null)
        {
            _buildEvents.OnBuildBegin += OnBuildBegin;
            _buildEvents.OnBuildDone += OnBuildDone;
        }
        if (_dbgEvents is not null)
        {
            _dbgEvents.OnEnterDesignMode += OnEnterDesignMode;
            _dbgEvents.OnEnterRunMode += OnEnterRunMode;
            _dbgEvents.OnEnterBreakMode += OnEnterBreakMode;
        }
        if (_winEvents is not null)
        {
            _winEvents.WindowActivated += OnWindowActivated;
        }
    }

    public WorkspaceEventsResult GetEvents(int maxResults, long sinceTimestampMs = 0)
    {
        lock (_lock)
        {
            var result = new WorkspaceEventsResult { TotalCollected = _totalCollected };
            int cap = Math.Max(1, Math.Min(maxResults, _events.Count));
            for (int i = _events.Count - 1; i >= 0 && result.Events.Count < cap; i--)
            {
                var e = _events[i];
                if (e.TimestampMs <= sinceTimestampMs) break;
                result.Events.Add(e);
            }
            result.Events.Reverse();
            result.LatestTimestampMs = result.Events.Count > 0
                ? result.Events[result.Events.Count - 1].TimestampMs
                : sinceTimestampMs;
            return result;
        }
    }

    public async Task<WorkspaceEventsResult> WaitForEventsAsync(
        long sinceTimestampMs, int timeoutMs, int maxResults, CancellationToken ct)
    {
        timeoutMs = Math.Max(100, Math.Min(timeoutMs, 30_000));
        var deadline = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + timeoutMs;

        while (true)
        {
            var result = GetEvents(maxResults, sinceTimestampMs);
            if (result.Events.Count > 0) return result;
            var remaining = (int)(deadline - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            if (remaining <= 0) return result;
            await _newEvent.WaitAsync(Math.Min(remaining, 1000), ct).ConfigureAwait(false);
        }
    }

    private void Add(WorkspaceEventKind kind, string summary, string? file = null)
    {
        var ev = new WorkspaceEvent
        {
            Id = Guid.NewGuid().ToString("N"),
            Kind = kind,
            TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Summary = summary,
            File = file,
        };
        lock (_lock)
        {
            _events.Add(ev);
            _totalCollected++;
            if (_events.Count > MaxEvents) _events.RemoveAt(0);
        }
        if (_newEvent.CurrentCount == 0)
            try { _newEvent.Release(); } catch { }
    }

    // -------- DTE handlers --------

    private void OnDocumentSaved(EnvDTE.Document doc) =>
        Add(WorkspaceEventKind.FileSaved, $"Saved: {doc?.Name ?? "<doc>"}", doc?.FullName);

    private void OnDocumentOpened(EnvDTE.Document doc) =>
        Add(WorkspaceEventKind.DocumentOpened, $"Opened: {doc?.Name ?? "<doc>"}", doc?.FullName);

    private void OnDocumentClosing(EnvDTE.Document doc) =>
        Add(WorkspaceEventKind.DocumentClosed, $"Closed: {doc?.Name ?? "<doc>"}", doc?.FullName);

    private void OnBuildBegin(EnvDTE.vsBuildScope scope, EnvDTE.vsBuildAction action) =>
        Add(WorkspaceEventKind.BuildStarted, $"Build started ({action} on {scope})");

    private void OnBuildDone(EnvDTE.vsBuildScope scope, EnvDTE.vsBuildAction action) =>
        Add(WorkspaceEventKind.BuildCompleted, $"Build completed ({action})");

    private void OnEnterDesignMode(EnvDTE.dbgEventReason reason) =>
        Add(WorkspaceEventKind.DebugStateChanged, $"Design mode ({reason})");

    private void OnEnterRunMode(EnvDTE.dbgEventReason reason) =>
        Add(WorkspaceEventKind.DebugStateChanged, $"Run mode ({reason})");

    private void OnEnterBreakMode(EnvDTE.dbgEventReason reason, ref EnvDTE.dbgExecutionAction action) =>
        Add(WorkspaceEventKind.DebugStateChanged, $"Break mode ({reason})");

    private void OnWindowActivated(EnvDTE.Window gotFocus, EnvDTE.Window lostFocus)
    {
        try
        {
            var name = gotFocus?.Document?.FullName;
            if (!string.IsNullOrEmpty(name))
                Add(WorkspaceEventKind.ActiveDocumentChanged, $"Active: {gotFocus?.Caption}", name);
        }
        catch { }
    }

    public void Dispose()
    {
        _newEvent.Dispose();
        try
        {
            if (_docEvents is not null)
            {
                _docEvents.DocumentSaved -= OnDocumentSaved;
                _docEvents.DocumentOpened -= OnDocumentOpened;
                _docEvents.DocumentClosing -= OnDocumentClosing;
            }
            if (_buildEvents is not null)
            {
                _buildEvents.OnBuildBegin -= OnBuildBegin;
                _buildEvents.OnBuildDone -= OnBuildDone;
            }
            if (_dbgEvents is not null)
            {
                _dbgEvents.OnEnterDesignMode -= OnEnterDesignMode;
                _dbgEvents.OnEnterRunMode -= OnEnterRunMode;
                _dbgEvents.OnEnterBreakMode -= OnEnterBreakMode;
            }
            if (_winEvents is not null)
                _winEvents.WindowActivated -= OnWindowActivated;
        }
        catch { }
    }
}
