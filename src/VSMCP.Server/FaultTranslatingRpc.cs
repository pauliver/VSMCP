using System;
using System.Reflection;
using System.Threading.Tasks;
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

    public static IVsmcpRpc Wrap(IVsmcpRpc inner)
    {
        var proxy = Create<IVsmcpRpc, FaultTranslatingRpc>();
        ((FaultTranslatingRpc)(object)proxy)._inner = inner ?? throw new ArgumentNullException(nameof(inner));
        return (IVsmcpRpc)proxy;
    }

    /// <summary>Convert any exception to an McpException carrying "{code}: {message}".</summary>
    public static McpException ToMcp(Exception ex)
    {
        var e = RpcError.FromException(ex);
        return new McpException($"{e.Code}: {e.Message}");
    }

    public static async Task WrapVoid(Task t)
    {
        try { await t.ConfigureAwait(false); }
        catch (Exception ex) { throw ToMcp(ex); }
    }

    public static async Task<T> WrapResult<T>(Task<T> t)
    {
        try { return await t.ConfigureAwait(false); }
        catch (Exception ex) { throw ToMcp(ex); }
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

        object? result;
        try
        {
            result = targetMethod.Invoke(_inner, args);
        }
        catch (TargetInvocationException tie) when (tie.InnerException is not null)
        {
            // Synchronous throw before the Task was produced.
            throw ToMcp(tie.InnerException);
        }

        // Every IVsmcpRpc method returns Task or Task<T>; object methods (ToString, etc.) don't.
        if (result is Task task)
        {
            var rt = targetMethod.ReturnType;
            if (rt.IsGenericType && rt.GetGenericTypeDefinition() == typeof(Task<>))
                return WrapResultMethod.MakeGenericMethod(rt.GetGenericArguments()[0]).Invoke(null, new object[] { task });
            return WrapVoid(task);
        }
        return result;
    }
}
