using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using VSMCP.Shared;

namespace VSMCP.Server;

/// <summary>
/// Wraps the <see cref="IVsmcpRpc"/> proxy so that faults thrown from any RPC call are
/// translated to <see cref="McpException"/> (#145). The MCP SDK sanitizes ordinary exceptions
/// to a generic "An error occurred invoking 'X'." but propagates <see cref="McpException"/>'s
/// message to the client — so this is how the VSMCP-* code + message reach the caller. The
/// code/message are recovered with <see cref="RpcError.FromException"/> (from #126), which
/// reads either a typed <see cref="VsmcpException"/> or the "{code}: {message}" string a
/// deserialized StreamJsonRpc RemoteInvocationException carries.
///
/// NOTE: must NOT be sealed — <see cref="DispatchProxy.Create{T,TProxy}"/> generates a runtime
/// subclass of this type, so a sealed modifier makes <see cref="Wrap"/> throw
/// "The base type ... cannot be sealed." on every connect (regression caught by
/// <c>FaultTranslatingRpcTests.Wrap_*</c>).
/// </summary>
public class FaultTranslatingRpc : DispatchProxy
{
    private IVsmcpRpc _inner = null!;

    /// <summary>Per-RPC logging seam (method/duration/error/correlation id). Set once at startup;
    /// null keeps the proxy silent. Errors log at Warning, successes at Debug.</summary>
    public static ILogger? Logger { get; set; }

    public static IVsmcpRpc Wrap(IVsmcpRpc inner)
    {
        var proxy = Create<IVsmcpRpc, FaultTranslatingRpc>();
        ((FaultTranslatingRpc)(object)proxy)._inner = inner ?? throw new ArgumentNullException(nameof(inner));
        return (IVsmcpRpc)proxy;
    }

    /// <summary>Convert any exception to an McpException carrying "{code}: {message}".</summary>
    public static McpException ToMcp(Exception ex) => ToMcp(ex, null);

    /// <summary>Same, stamped with the per-call correlation id so the client-facing error, the
    /// server log line, and the VSIX-side vsix.log lines are joinable by one id.</summary>
    public static McpException ToMcp(Exception ex, string? correlationId)
    {
        var e = RpcError.FromException(ex);
        var suffix = string.IsNullOrEmpty(correlationId) ? "" : $" [vsmcp:{correlationId}]";
        return new McpException($"{e.Code}: {e.Message}{suffix}");
    }

    public static async Task WrapVoid(Task t, string method = "(unknown)", string? correlationId = null)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            await t.ConfigureAwait(false);
            Logger?.LogDebug("rpc {Method} ok in {Ms}ms [{Id}]", method, sw.ElapsedMilliseconds, correlationId);
        }
        catch (Exception ex)
        {
            var mcp = ToMcp(ex, correlationId);
            Logger?.LogWarning("rpc {Method} failed after {Ms}ms [{Id}]: {Error}", method, sw.ElapsedMilliseconds, correlationId, mcp.Message);
            throw mcp;
        }
    }

    public static async Task<T> WrapResult<T>(Task<T> t, string method = "(unknown)", string? correlationId = null)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var result = await t.ConfigureAwait(false);
            Logger?.LogDebug("rpc {Method} ok in {Ms}ms [{Id}]", method, sw.ElapsedMilliseconds, correlationId);
            return result;
        }
        catch (Exception ex)
        {
            var mcp = ToMcp(ex, correlationId);
            Logger?.LogWarning("rpc {Method} failed after {Ms}ms [{Id}]: {Error}", method, sw.ElapsedMilliseconds, correlationId, mcp.Message);
            throw mcp;
        }
    }

    /// <summary>
    /// Run a server-LOCAL tool body — one that bypasses the IVsmcpRpc proxy (profiler/trace/
    /// counters hosts, dbgeng, compile_commands reader), so the <see cref="Wrap"/> path never
    /// sees its faults — and translate any exception to <see cref="McpException"/>, matching the
    /// cross-pipe behavior. Without this the MCP SDK sanitizes the throw to the generic
    /// "An error occurred invoking 'X'." (#145 follow-up).
    /// </summary>
    public static Task<T> Local<T>(Func<T> body)
    {
        try { return Task.FromResult(body()); }
        catch (Exception ex) { return Task.FromException<T>(ToMcp(ex)); }
    }

    /// <summary>Async counterpart of <see cref="Local{T}"/> for local bodies that return a Task.</summary>
    public static async Task<T> LocalAsync<T>(Func<Task<T>> body)
    {
        try { return await body().ConfigureAwait(false); }
        catch (Exception ex) { throw ToMcp(ex); }
    }

    private static readonly MethodInfo WrapResultMethod =
        typeof(FaultTranslatingRpc).GetMethod(nameof(WrapResult), BindingFlags.Public | BindingFlags.Static)!;

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod is null) return null;

        // Fresh correlation id per call. CorrelationManagerTracingStrategy (set on both JsonRpc
        // instances) carries it across the pipe, so VSIX vsix.log lines, the server log line,
        // and the client-facing error all share it.
        var activity = Guid.NewGuid();
        Trace.CorrelationManager.ActivityId = activity;
        var shortId = activity.ToString("N").Substring(0, 8);

        object? result;
        try
        {
            result = targetMethod.Invoke(_inner, args);
        }
        catch (TargetInvocationException tie) when (tie.InnerException is not null)
        {
            // Synchronous throw before the Task was produced.
            throw ToMcp(tie.InnerException, shortId);
        }

        // Every IVsmcpRpc method returns Task or Task<T>; object methods (ToString, etc.) don't.
        if (result is Task task)
        {
            var rt = targetMethod.ReturnType;
            if (rt.IsGenericType && rt.GetGenericTypeDefinition() == typeof(Task<>))
                return WrapResultMethod.MakeGenericMethod(rt.GetGenericArguments()[0]).Invoke(null, new object?[] { task, targetMethod.Name, shortId });
            return WrapVoid(task, targetMethod.Name, shortId);
        }
        return result;
    }
}
