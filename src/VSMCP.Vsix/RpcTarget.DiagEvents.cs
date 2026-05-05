using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using VSMCP.Shared;

namespace VSMCP.Vsix;

internal sealed partial class RpcTarget
{
    public async Task<DiagEventsResult> DiagEventsListAsync(
        string? filter,
        int maxResults,
        CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        var collector = _package.DiagEvents
            ?? throw new VsmcpException(ErrorCodes.WrongState, "DiagEventCollector not initialised.");
        return collector.GetEvents(filter, maxResults <= 0 ? 100 : maxResults);
    }

    public async Task<DiagEventsResult> DiagEventsWatchAsync(
        string? filter,
        int maxResults,
        long sinceTimestampMs,
        int timeoutMs,
        CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        var collector = _package.DiagEvents
            ?? throw new VsmcpException(ErrorCodes.WrongState, "DiagEventCollector not initialised.");
        return await collector.WaitForEventsAsync(
            filter,
            maxResults <= 0 ? 100 : maxResults,
            sinceTimestampMs,
            timeoutMs <= 0 ? 10_000 : timeoutMs,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<DiagEventDetail> DiagEventDetailAsync(
        string eventId,
        CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        var collector = _package.DiagEvents
            ?? throw new VsmcpException(ErrorCodes.WrongState, "DiagEventCollector not initialised.");
        return collector.GetDetail(eventId)
            ?? throw new VsmcpException(ErrorCodes.NotFound, $"Event '{eventId}' not found.");
    }

    public async Task DiagEventsClearAsync(CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        var collector = _package.DiagEvents
            ?? throw new VsmcpException(ErrorCodes.WrongState, "DiagEventCollector not initialised.");
        collector.Clear();
    }

    public async Task<DiagMemorySnapshot> DiagMemorySnapshotAsync(CancellationToken cancellationToken = default)
    {
        await _jtf.SwitchToMainThreadAsync(cancellationToken);

        int pid = 0;
        string? name = null;
        try
        {
            var dte = await RequireDteAsync();
            var procs = dte.Debugger?.DebuggedProcesses;
            if (procs is { Count: > 0 })
            {
                var first = procs.Item(1) as EnvDTE.Process;
                pid = first?.ProcessID ?? 0;
                name = first?.Name;
            }
        }
        catch { }

        long workingSet = 0, privateBytes = 0, gcHeap = 0;

        if (pid > 0)
        {
            try
            {
                using var proc = Process.GetProcessById(pid);
                workingSet = proc.WorkingSet64;
                privateBytes = proc.PrivateMemorySize64;
            }
            catch { }
        }

        // GC.GetTotalMemory reflects the managed heap of *this* process (devenv.exe),
        // not the debuggee — useful as a cross-check but not the debuggee's heap.
        // Full managed heap snapshot requires IVsDiagnosticsHub (future work).
        try { gcHeap = GC.GetTotalMemory(forceFullCollection: false); } catch { }

        return new DiagMemorySnapshot
        {
            SnapshotId = Guid.NewGuid().ToString("N"),
            TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            WorkingSetBytes = workingSet,
            PrivateBytes = privateBytes,
            GcHeapBytes = gcHeap,
            ProcessId = pid,
            ProcessName = name,
        };
    }

    public async Task<DiagCpuTimelineResult> DiagCpuTimelineAsync(
        int? windowMs,
        CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        var collector = _package.DiagEvents
            ?? throw new VsmcpException(ErrorCodes.WrongState, "DiagEventCollector not initialised.");
        return collector.GetCpuTimeline(windowMs);
    }
public async Task<DiagEventsResult> DiagEventsListInternedAsync(
        string? filter, int maxResults, CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        var collector = _package.DiagEvents
            ?? throw new VsmcpException(ErrorCodes.WrongState, "DiagEventCollector not initialised.");

        // Get the standard list, then build a shared frame table.
        var raw = collector.GetEvents(filter, maxResults <= 0 ? 100 : maxResults);
        var framesTable = new Dictionary<int, StackFrameInfo>();
        var keyToId = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var e in raw.Events)
        {
            // Pull detail to access frames (DiagEvent doesn't carry them in list form).
            var detail = collector.GetDetail(e.Id);
            if (detail?.Frames is null) continue;
            var frameIds = new List<int>();
            foreach (var f in detail.Frames)
            {
                var key = $"{f.FunctionName}|{f.Module}|{f.File}|{f.Line}";
                if (!keyToId.TryGetValue(key, out var id))
                {
                    id = framesTable.Count;
                    keyToId[key] = id;
                    framesTable[id] = f;
                }
                frameIds.Add(id);
            }
            e.FrameIds = frameIds;
        }

        raw.FramesTable = framesTable;
        return raw;
    }
}
