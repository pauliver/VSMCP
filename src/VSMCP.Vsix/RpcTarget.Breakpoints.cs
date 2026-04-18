using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using VSMCP.Shared;

namespace VSMCP.Vsix;

internal sealed partial class RpcTarget
{
    // Id -> list of underlying EnvDTE breakpoints (one logical bp may bind in multiple places).
    private static readonly ConcurrentDictionary<string, List<EnvDTE.Breakpoint>> s_bpStore = new();

    public async Task<BreakpointInfo> BreakpointSetAsync(BreakpointSetOptions options, CancellationToken cancellationToken = default)
    {
        if (options is null) throw new VsmcpException(ErrorCodes.NotFound, "Options are required.");
        await _jtf.SwitchToMainThreadAsync(cancellationToken);
        var dte = await RequireDteAsync();

        var bps = AddBreakpoints(dte, options);
        if (bps.Count == 0)
            throw new VsmcpException(ErrorCodes.InteropFault, "Visual Studio did not bind any breakpoint for the given options.");

        var isTracepoint = !string.IsNullOrEmpty(options.TracepointMessage);
        if (isTracepoint)
            ApplyTracepoint(bps, options.TracepointMessage!);

        if (options.Disabled)
        {
            foreach (var bp in bps) try { bp.Enabled = false; } catch { }
        }

        var id = Guid.NewGuid().ToString("N");
        s_bpStore[id] = bps;

        return BuildInfo(id, options.Kind, bps, isTracepoint, options.TracepointMessage);
    }

    public async Task<BreakpointListResult> BreakpointListAsync(CancellationToken cancellationToken = default)
    {
        await _jtf.SwitchToMainThreadAsync(cancellationToken);
        _ = await RequireDteAsync();

        var result = new BreakpointListResult();
        foreach (var kv in s_bpStore.ToArray())
        {
            var live = kv.Value.Where(IsLive).ToList();
            if (live.Count == 0)
            {
                s_bpStore.TryRemove(kv.Key, out _);
                continue;
            }

            var first = live[0];
            var kind = InferKind(first);
            var tp = ReadTracepoint(first);
            result.Breakpoints.Add(BuildInfo(kv.Key, kind, live, tp.isTracepoint, tp.message));
        }
        return result;
    }

    public async Task BreakpointRemoveAsync(string bpId, CancellationToken cancellationToken = default)
    {
        await _jtf.SwitchToMainThreadAsync(cancellationToken);
        _ = await RequireDteAsync();

        if (!s_bpStore.TryRemove(bpId, out var bps))
            throw new VsmcpException(ErrorCodes.NotFound, $"No breakpoint with id '{bpId}'.");

        foreach (var bp in bps)
        {
            try { bp.Delete(); } catch { }
        }
    }

    public async Task<BreakpointInfo> BreakpointEnableAsync(string bpId, CancellationToken cancellationToken = default)
        => await SetEnabledAsync(bpId, true, cancellationToken);

    public async Task<BreakpointInfo> BreakpointDisableAsync(string bpId, CancellationToken cancellationToken = default)
        => await SetEnabledAsync(bpId, false, cancellationToken);

    private async Task<BreakpointInfo> SetEnabledAsync(string bpId, bool enabled, CancellationToken cancellationToken)
    {
        await _jtf.SwitchToMainThreadAsync(cancellationToken);
        _ = await RequireDteAsync();

        if (!s_bpStore.TryGetValue(bpId, out var bps))
            throw new VsmcpException(ErrorCodes.NotFound, $"No breakpoint with id '{bpId}'.");

        var live = bps.Where(IsLive).ToList();
        if (live.Count == 0)
        {
            s_bpStore.TryRemove(bpId, out _);
            throw new VsmcpException(ErrorCodes.NotFound, $"Breakpoint '{bpId}' no longer exists.");
        }

        foreach (var bp in live)
        {
            try { bp.Enabled = enabled; } catch { }
        }

        var tp = ReadTracepoint(live[0]);
        return BuildInfo(bpId, InferKind(live[0]), live, tp.isTracepoint, tp.message);
    }

    // -------- helpers --------

    private static List<EnvDTE.Breakpoint> AddBreakpoints(EnvDTE80.DTE2 dte, BreakpointSetOptions o)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var conditionType = o.ConditionKind switch
        {
            BreakpointConditionKind.WhenChanged => EnvDTE.dbgBreakpointConditionType.dbgBreakpointConditionTypeWhenChanged,
            _ => EnvDTE.dbgBreakpointConditionType.dbgBreakpointConditionTypeWhenTrue,
        };
        var condition = o.ConditionKind == BreakpointConditionKind.None ? "" : (o.ConditionExpression ?? "");

        var hitType = o.HitKind switch
        {
            BreakpointHitKind.HitCountEqual => EnvDTE.dbgHitCountType.dbgHitCountTypeEqual,
            BreakpointHitKind.HitCountGreaterOrEqual => EnvDTE.dbgHitCountType.dbgHitCountTypeGreaterOrEqual,
            BreakpointHitKind.HitCountMultiple => EnvDTE.dbgHitCountType.dbgHitCountTypeMultiple,
            _ => EnvDTE.dbgHitCountType.dbgHitCountTypeNone,
        };
        var hitCount = o.HitKind == BreakpointHitKind.Always ? 0 : (o.HitCount ?? 1);

        try
        {
            EnvDTE.Breakpoints added;
            switch (o.Kind)
            {
                case BreakpointKind.Line:
                    if (string.IsNullOrWhiteSpace(o.File) || o.Line is null or < 1)
                        throw new VsmcpException(ErrorCodes.NotFound, "Line breakpoints require File and Line.");
                    added = dte.Debugger.Breakpoints.Add(
                        Function: "",
                        File: o.File,
                        Line: o.Line!.Value,
                        Column: o.Column ?? 1,
                        Condition: condition,
                        ConditionType: conditionType,
                        Language: "",
                        Data: "",
                        DataCount: 1,
                        Address: "",
                        HitCount: hitCount,
                        HitCountType: hitType);
                    break;

                case BreakpointKind.Function:
                    if (string.IsNullOrWhiteSpace(o.Function))
                        throw new VsmcpException(ErrorCodes.NotFound, "Function breakpoints require Function.");
                    added = dte.Debugger.Breakpoints.Add(
                        Function: o.Function,
                        File: "",
                        Line: 1,
                        Column: 1,
                        Condition: condition,
                        ConditionType: conditionType,
                        Language: "",
                        Data: "",
                        DataCount: 1,
                        Address: "",
                        HitCount: hitCount,
                        HitCountType: hitType);
                    break;

                case BreakpointKind.Address:
                    if (string.IsNullOrWhiteSpace(o.Address))
                        throw new VsmcpException(ErrorCodes.NotFound, "Address breakpoints require Address.");
                    added = dte.Debugger.Breakpoints.Add(
                        Function: "",
                        File: "",
                        Line: 1,
                        Column: 1,
                        Condition: condition,
                        ConditionType: conditionType,
                        Language: "",
                        Data: "",
                        DataCount: 1,
                        Address: o.Address,
                        HitCount: hitCount,
                        HitCountType: hitType);
                    break;

                case BreakpointKind.Data:
                    if (string.IsNullOrWhiteSpace(o.Address))
                        throw new VsmcpException(ErrorCodes.NotFound, "Data breakpoints require Address (of the watched memory).");
                    var count = o.DataByteCount ?? 4;
                    if (count != 1 && count != 2 && count != 4 && count != 8)
                        throw new VsmcpException(ErrorCodes.NotFound, "DataByteCount must be 1, 2, 4, or 8.");
                    added = dte.Debugger.Breakpoints.Add(
                        Function: "",
                        File: "",
                        Line: 1,
                        Column: 1,
                        Condition: condition,
                        ConditionType: conditionType,
                        Language: "",
                        Data: o.Address,
                        DataCount: count,
                        Address: "",
                        HitCount: hitCount,
                        HitCountType: hitType);
                    break;

                default:
                    throw new VsmcpException(ErrorCodes.Unsupported, $"Unsupported breakpoint kind: {o.Kind}.");
            }

            var list = new List<EnvDTE.Breakpoint>();
            foreach (EnvDTE.Breakpoint bp in added) list.Add(bp);
            return list;
        }
        catch (VsmcpException) { throw; }
        catch (Exception ex)
        {
            throw new VsmcpException(ErrorCodes.InteropFault, $"Failed to set breakpoint: {ex.Message}", ex);
        }
    }

    private static void ApplyTracepoint(IEnumerable<EnvDTE.Breakpoint> bps, string message)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        foreach (var bp in bps)
        {
            if (bp is EnvDTE80.Breakpoint2 bp2)
            {
                try
                {
                    bp2.Message = message;
                    bp2.BreakWhenHit = false;
                }
                catch { }
            }
        }
    }

    private static (bool isTracepoint, string? message) ReadTracepoint(EnvDTE.Breakpoint bp)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (bp is EnvDTE80.Breakpoint2 bp2)
        {
            try
            {
                var msg = bp2.Message;
                if (!string.IsNullOrEmpty(msg)) return (!bp2.BreakWhenHit, msg);
            }
            catch { }
        }
        return (false, null);
    }

    private static bool IsLive(EnvDTE.Breakpoint bp)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        try { _ = bp.Enabled; return true; }
        catch { return false; }
    }

    private static BreakpointKind InferKind(EnvDTE.Breakpoint bp)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        try
        {
            if (!string.IsNullOrEmpty(bp.FunctionName) && string.IsNullOrEmpty(bp.File)) return BreakpointKind.Function;
        }
        catch { }
        return BreakpointKind.Line;
    }

    private static BreakpointInfo BuildInfo(string id, BreakpointKind kind, List<EnvDTE.Breakpoint> bps, bool isTracepoint, string? tpMessage)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var info = new BreakpointInfo
        {
            Id = id,
            Kind = kind,
            BindSites = bps.Count,
            IsTracepoint = isTracepoint,
            TracepointMessage = tpMessage,
        };

        var first = bps[0];
        try { info.Enabled = first.Enabled; } catch { }
        try { info.File = string.IsNullOrEmpty(first.File) ? null : first.File; } catch { }
        try { info.Line = first.FileLine > 0 ? first.FileLine : (int?)null; } catch { }
        try { info.Column = first.FileColumn > 0 ? first.FileColumn : (int?)null; } catch { }
        try { info.Function = string.IsNullOrEmpty(first.FunctionName) ? null : first.FunctionName; } catch { }
        try { info.ConditionExpression = string.IsNullOrEmpty(first.Condition) ? null : first.Condition; } catch { }

        try
        {
            info.ConditionKind = first.ConditionType switch
            {
                EnvDTE.dbgBreakpointConditionType.dbgBreakpointConditionTypeWhenChanged => BreakpointConditionKind.WhenChanged,
                EnvDTE.dbgBreakpointConditionType.dbgBreakpointConditionTypeWhenTrue when !string.IsNullOrEmpty(first.Condition) => BreakpointConditionKind.WhenTrue,
                _ => BreakpointConditionKind.None,
            };
        }
        catch { }

        try
        {
            info.HitKind = first.HitCountType switch
            {
                EnvDTE.dbgHitCountType.dbgHitCountTypeEqual => BreakpointHitKind.HitCountEqual,
                EnvDTE.dbgHitCountType.dbgHitCountTypeGreaterOrEqual => BreakpointHitKind.HitCountGreaterOrEqual,
                EnvDTE.dbgHitCountType.dbgHitCountTypeMultiple => BreakpointHitKind.HitCountMultiple,
                _ => BreakpointHitKind.Always,
            };
            info.HitCount = first.HitCountTarget > 0 ? first.HitCountTarget : (int?)null;
            info.CurrentHits = first.CurrentHits;
        }
        catch { }

        return info;
    }
}
