using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using VSMCP.Shared;

namespace VSMCP.Vsix;

internal sealed partial class RpcTarget
{
    public async Task<ThreadListResult> ThreadsListAsync(CancellationToken cancellationToken = default)
    {
        await _jtf.SwitchToMainThreadAsync(cancellationToken);
        var dte = await RequireDteAsync();
        var debugger = RequireDebugging(dte);

        var result = new ThreadListResult();
        var currentId = TryGetCurrentThreadId(debugger);
        var program = debugger.CurrentProgram;
        if (program is null) return result;

        foreach (EnvDTE.Thread t in program.Threads)
        {
            if (t is null) continue;
            result.Threads.Add(SnapshotThread(t, currentId));
        }
        return result;
    }

    public async Task<ThreadInfo> ThreadsFreezeAsync(int threadId, CancellationToken cancellationToken = default)
        => await SetThreadFrozenAsync(threadId, freeze: true, cancellationToken);

    public async Task<ThreadInfo> ThreadsThawAsync(int threadId, CancellationToken cancellationToken = default)
        => await SetThreadFrozenAsync(threadId, freeze: false, cancellationToken);

    public async Task<ThreadInfo> ThreadsSwitchAsync(int threadId, CancellationToken cancellationToken = default)
    {
        await _jtf.SwitchToMainThreadAsync(cancellationToken);
        var dte = await RequireDteAsync();
        var debugger = RequireDebugging(dte);

        var thread = FindThread(debugger, threadId)
            ?? throw new VsmcpException(ErrorCodes.NotFound, $"No thread with id {threadId}.");

        try { debugger.CurrentThread = thread; }
        catch (Exception ex) { throw new VsmcpException(ErrorCodes.InteropFault, $"Failed to switch thread: {ex.Message}", ex); }

        return SnapshotThread(thread, threadId);
    }

    public async Task<StackGetResult> StackGetAsync(int? threadId, int? depth, CancellationToken cancellationToken = default)
    {
        await _jtf.SwitchToMainThreadAsync(cancellationToken);
        var dte = await RequireDteAsync();
        var debugger = RequireDebugging(dte);

        var thread = threadId is int tid
            ? (FindThread(debugger, tid) ?? throw new VsmcpException(ErrorCodes.NotFound, $"No thread with id {tid}."))
            : debugger.CurrentThread ?? throw new VsmcpException(ErrorCodes.WrongState, "No current thread. Break into the debuggee first.");

        var result = new StackGetResult { ThreadId = thread.ID };
        int currentFrameIndex = TryGetCurrentFrameIndex(debugger, thread);
        int max = depth is int d && d > 0 ? d : int.MaxValue;

        int i = 0;
        foreach (EnvDTE.StackFrame f in thread.StackFrames)
        {
            if (f is null) { i++; continue; }
            if (i >= max) { result.Truncated = true; break; }

            var info = new StackFrameInfo
            {
                Index = i,
                ThreadId = thread.ID,
                FunctionName = SafeGet(() => f.FunctionName) ?? "",
                Module = SafeGet(() => f.Module),
                Language = SafeGet(() => f.Language),
                IsCurrent = i == currentFrameIndex,
            };
            result.Frames.Add(info);
            i++;
        }
        return result;
    }

    public async Task<StackFrameInfo> FrameSwitchAsync(int? threadId, int frameIndex, CancellationToken cancellationToken = default)
    {
        if (frameIndex < 0) throw new VsmcpException(ErrorCodes.NotFound, "Frame index must be >= 0.");
        await _jtf.SwitchToMainThreadAsync(cancellationToken);
        var dte = await RequireDteAsync();
        var debugger = RequireDebugging(dte);

        var thread = threadId is int tid
            ? (FindThread(debugger, tid) ?? throw new VsmcpException(ErrorCodes.NotFound, $"No thread with id {tid}."))
            : debugger.CurrentThread ?? throw new VsmcpException(ErrorCodes.WrongState, "No current thread.");

        if (debugger.CurrentThread is null || debugger.CurrentThread.ID != thread.ID)
        {
            try { debugger.CurrentThread = thread; }
            catch (Exception ex) { throw new VsmcpException(ErrorCodes.InteropFault, $"Failed to switch thread: {ex.Message}", ex); }
        }

        var frame = GetFrame(thread, frameIndex)
            ?? throw new VsmcpException(ErrorCodes.NotFound, $"Frame {frameIndex} does not exist on thread {thread.ID}.");

        try { debugger.CurrentStackFrame = frame; }
        catch (Exception ex) { throw new VsmcpException(ErrorCodes.InteropFault, $"Failed to switch frame: {ex.Message}", ex); }

        return new StackFrameInfo
        {
            Index = frameIndex,
            ThreadId = thread.ID,
            FunctionName = SafeGet(() => frame.FunctionName) ?? "",
            Module = SafeGet(() => frame.Module),
            Language = SafeGet(() => frame.Language),
            IsCurrent = true,
        };
    }

    public async Task<VariableListResult> FrameLocalsAsync(int? threadId, int? frameIndex, int expandDepth, CancellationToken cancellationToken = default)
    {
        await _jtf.SwitchToMainThreadAsync(cancellationToken);
        var dte = await RequireDteAsync();
        var debugger = RequireDebugging(dte);
        var (thread, frame, idx) = ResolveFrame(debugger, threadId, frameIndex);

        var result = new VariableListResult { ThreadId = thread.ID, FrameIndex = idx };
        if (frame.Locals is null) return result;
        foreach (EnvDTE.Expression e in frame.Locals)
            if (e is not null) result.Variables.Add(Snapshot(e, expandDepth));
        return result;
    }

    public async Task<VariableListResult> FrameArgumentsAsync(int? threadId, int? frameIndex, int expandDepth, CancellationToken cancellationToken = default)
    {
        await _jtf.SwitchToMainThreadAsync(cancellationToken);
        var dte = await RequireDteAsync();
        var debugger = RequireDebugging(dte);
        var (thread, frame, idx) = ResolveFrame(debugger, threadId, frameIndex);

        var result = new VariableListResult { ThreadId = thread.ID, FrameIndex = idx };
        if (frame.Arguments is null) return result;
        foreach (EnvDTE.Expression e in frame.Arguments)
            if (e is not null) result.Variables.Add(Snapshot(e, expandDepth));
        return result;
    }

    public async Task<EvalResult> EvalExpressionAsync(EvalOptions options, CancellationToken cancellationToken = default)
    {
        if (options is null) throw new VsmcpException(ErrorCodes.NotFound, "Options are required.");
        if (string.IsNullOrWhiteSpace(options.Expression))
            throw new VsmcpException(ErrorCodes.NotFound, "Expression is required.");

        await _jtf.SwitchToMainThreadAsync(cancellationToken);
        var dte = await RequireDteAsync();
        var debugger = RequireDebugging(dte);

        // Switch context if caller specified a non-default thread/frame.
        if (options.ThreadId is int tid)
        {
            var t = FindThread(debugger, tid) ?? throw new VsmcpException(ErrorCodes.NotFound, $"No thread with id {tid}.");
            if (debugger.CurrentThread?.ID != t.ID)
            {
                try { debugger.CurrentThread = t; }
                catch (Exception ex) { throw new VsmcpException(ErrorCodes.InteropFault, $"Failed to switch thread: {ex.Message}", ex); }
            }
        }
        if (options.FrameIndex is int fi)
        {
            var thread = debugger.CurrentThread ?? throw new VsmcpException(ErrorCodes.WrongState, "No current thread.");
            var f = GetFrame(thread, fi) ?? throw new VsmcpException(ErrorCodes.NotFound, $"Frame {fi} does not exist.");
            try { debugger.CurrentStackFrame = f; }
            catch (Exception ex) { throw new VsmcpException(ErrorCodes.InteropFault, $"Failed to switch frame: {ex.Message}", ex); }
        }

        EnvDTE.Expression expr;
        try
        {
            var timeout = options.TimeoutMs > 0 ? options.TimeoutMs : 5000;
            expr = debugger.GetExpression(options.Expression, options.AllowSideEffects, timeout);
        }
        catch (Exception ex) { throw new VsmcpException(ErrorCodes.InteropFault, $"Eval failed: {ex.Message}", ex); }

        var result = new EvalResult
        {
            Expression = options.Expression,
            IsValid = expr.IsValidValue,
            Type = SafeGet(() => expr.Type),
            Value = SafeGet(() => expr.Value),
            IsExpandable = SafeGet(() => expr.DataMembers?.Count) is int c && c > 0,
        };

        if (result.IsValid && result.IsExpandable && options.ExpandDepth > 0)
        {
            foreach (EnvDTE.Expression m in expr.DataMembers)
                if (m is not null) result.Children.Add(Snapshot(m, options.ExpandDepth - 1));
        }
        return result;
    }

    // -------- helpers --------

    private async Task<ThreadInfo> SetThreadFrozenAsync(int threadId, bool freeze, CancellationToken ct)
    {
        await _jtf.SwitchToMainThreadAsync(ct);
        var dte = await RequireDteAsync();
        var debugger = RequireDebugging(dte);

        var thread = FindThread(debugger, threadId)
            ?? throw new VsmcpException(ErrorCodes.NotFound, $"No thread with id {threadId}.");

        try
        {
            if (freeze) thread.Freeze();
            else thread.Thaw();
        }
        catch (Exception ex) { throw new VsmcpException(ErrorCodes.InteropFault, $"Failed to {(freeze ? "freeze" : "thaw")} thread: {ex.Message}", ex); }

        return SnapshotThread(thread, TryGetCurrentThreadId(debugger));
    }

    private static EnvDTE.Debugger RequireDebugging(EnvDTE80.DTE2 dte)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var debugger = dte.Debugger
            ?? throw new VsmcpException(ErrorCodes.InteropFault, "Debugger service unavailable.");
        if (debugger.CurrentMode == EnvDTE.dbgDebugMode.dbgDesignMode)
            throw new VsmcpException(ErrorCodes.NotDebugging, "No active debug session.");
        return debugger;
    }

    private static EnvDTE.Thread? FindThread(EnvDTE.Debugger debugger, int threadId)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var program = debugger.CurrentProgram;
        if (program is null) return null;
        foreach (EnvDTE.Thread t in program.Threads)
            if (t is not null && t.ID == threadId) return t;
        return null;
    }

    private static int TryGetCurrentThreadId(EnvDTE.Debugger debugger)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        try { return debugger.CurrentThread?.ID ?? -1; } catch { return -1; }
    }

    private static int TryGetCurrentFrameIndex(EnvDTE.Debugger debugger, EnvDTE.Thread thread)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        EnvDTE.StackFrame? current = null;
        try { current = debugger.CurrentStackFrame; } catch { }
        if (current is null) return 0;

        int i = 0;
        foreach (EnvDTE.StackFrame f in thread.StackFrames)
        {
            if (ReferenceEquals(f, current)) return i;
            i++;
        }
        return 0;
    }

    private static EnvDTE.StackFrame? GetFrame(EnvDTE.Thread thread, int index)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        int i = 0;
        foreach (EnvDTE.StackFrame f in thread.StackFrames)
        {
            if (i == index) return f;
            i++;
        }
        return null;
    }

    private static (EnvDTE.Thread thread, EnvDTE.StackFrame frame, int index) ResolveFrame(EnvDTE.Debugger debugger, int? threadId, int? frameIndex)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var thread = threadId is int tid
            ? (FindThread(debugger, tid) ?? throw new VsmcpException(ErrorCodes.NotFound, $"No thread with id {tid}."))
            : debugger.CurrentThread ?? throw new VsmcpException(ErrorCodes.WrongState, "No current thread. Break into the debuggee first.");

        var idx = frameIndex ?? TryGetCurrentFrameIndex(debugger, thread);
        if (idx < 0) idx = 0;

        var frame = GetFrame(thread, idx)
            ?? throw new VsmcpException(ErrorCodes.NotFound, $"Frame {idx} does not exist on thread {thread.ID}.");

        return (thread, frame, idx);
    }

    private static ThreadInfo SnapshotThread(EnvDTE.Thread t, int currentThreadId)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var info = new ThreadInfo
        {
            Id = TryInt(() => t.ID),
            Name = SafeGet(() => t.Name),
            Location = SafeGet(() => t.Location),
            IsFrozen = TryBool(() => t.IsFrozen),
            State = TryBool(() => t.IsAlive) ? "Alive" : "Terminated",
        };
        info.IsCurrent = info.Id == currentThreadId;
        return info;
    }

    private static int TryInt(Func<int> f) { try { return f(); } catch { return 0; } }
    private static bool TryBool(Func<bool> f) { try { return f(); } catch { return false; } }

    private static VariableInfo Snapshot(EnvDTE.Expression e, int remainingDepth)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var v = new VariableInfo
        {
            Name = SafeGet(() => e.Name) ?? "",
            Type = SafeGet(() => e.Type),
            Value = SafeGet(() => e.Value),
            IsExpandable = SafeGet(() => e.DataMembers?.Count) is int c && c > 0,
        };

        if (v.IsExpandable && remainingDepth > 0 && e.DataMembers is not null)
        {
            foreach (EnvDTE.Expression m in e.DataMembers)
                if (m is not null) v.Children.Add(Snapshot(m, remainingDepth - 1));
        }
        return v;
    }

    private static T? SafeGet<T>(Func<T> getter)
    {
        try { return getter(); }
        catch { return default; }
    }
}
