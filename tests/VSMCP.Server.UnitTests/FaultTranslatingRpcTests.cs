using System;
using System.Reflection;
using System.Threading.Tasks;
using ModelContextProtocol;
using VSMCP.Server;
using VSMCP.Shared;
using Xunit;

namespace VSMCP.Tests.Server;

/// <summary>
/// Covers the RPC fault -> McpException translation (#145) that lets VSMCP-* codes/messages
/// reach MCP clients instead of the SDK's generic "An error occurred invoking 'X'.".
/// </summary>
public class FaultTranslatingRpcTests
{
    [Fact]
    public void ToMcp_TypedVsmcpException_PreservesCodeAndMessage()
    {
        var mcp = FaultTranslatingRpc.ToMcp(new VsmcpException(ErrorCodes.NotConnected, "no VS"));
        Assert.IsType<McpException>(mcp);
        Assert.Equal("VSMCP-not-connected: no VS", mcp.Message);
    }

    [Fact]
    public void ToMcp_PrefixedRemoteMessage_IsParsed()
    {
        // What a deserialized StreamJsonRpc RemoteInvocationException carries from the VSIX.
        var mcp = FaultTranslatingRpc.ToMcp(new InvalidOperationException("VSMCP-not-found: File not part of any loaded project: x.cs"));
        Assert.Equal("VSMCP-not-found: File not part of any loaded project: x.cs", mcp.Message);
    }

    [Fact]
    public void ToMcp_UnrecognizedException_FallsBackToInteropFault()
    {
        var mcp = FaultTranslatingRpc.ToMcp(new Exception("kaboom"));
        Assert.StartsWith("VSMCP-interop-fault: kaboom", mcp.Message);
    }

    [Fact]
    public async Task WrapResult_FaultedTask_ThrowsMcpException()
    {
        var faulted = Task.FromException<int>(new VsmcpException(ErrorCodes.WrongState, "not in break mode"));
        var ex = await Assert.ThrowsAsync<McpException>(() => FaultTranslatingRpc.WrapResult(faulted));
        Assert.Equal("VSMCP-wrong-state: not in break mode", ex.Message);
    }

    [Fact]
    public async Task WrapResult_SuccessfulTask_PassesThrough()
    {
        var ok = Task.FromResult(42);
        Assert.Equal(42, await FaultTranslatingRpc.WrapResult(ok));
    }

    [Fact]
    public async Task WrapVoid_FaultedTask_ThrowsMcpException()
    {
        var faulted = Task.FromException(new VsmcpException(ErrorCodes.NotDebugging, "no session"));
        var ex = await Assert.ThrowsAsync<McpException>(() => FaultTranslatingRpc.WrapVoid(faulted));
        Assert.Equal("VSMCP-not-debugging: no session", ex.Message);
    }

    [Fact]
    public async Task WrapVoid_SuccessfulTask_PassesThrough()
    {
        await FaultTranslatingRpc.WrapVoid(Task.CompletedTask);   // must not throw
    }

    // ---- Regression: the live path my earlier tests missed (#145 follow-up). ----
    // FaultTranslatingRpc was `sealed`, so Wrap() -> DispatchProxy.Create<,> threw
    // "The base type ... cannot be sealed." on EVERY connect, breaking all cross-pipe
    // tools while the (compile- and unit-clean) #145 fix looked fine. These tests
    // exercise the real Wrap()/Invoke path, not just the static helpers.

    [Fact]
    public void FaultTranslatingRpc_IsNotSealed_SoDispatchProxyCanSubclassIt()
        => Assert.False(typeof(FaultTranslatingRpc).IsSealed);

    [Fact]
    public void Wrap_OverAProxy_DoesNotThrow()
    {
        var inner = MakeStub(_ => Task.FromResult<object?>(null));
        var wrapped = FaultTranslatingRpc.Wrap(inner);   // threw ArgumentException while sealed
        Assert.NotNull(wrapped);
    }

    [Fact]
    public async Task Wrap_FaultingProxiedCall_SurfacesMcpException()
    {
        // Routes through DispatchProxy.Invoke -> MakeGenericMethod -> WrapResult -> ToMcp.
        var inner = MakeStub(_ => Task.FromException<PingResult>(
            new VsmcpException(ErrorCodes.NotDebugging, "no session")));
        var wrapped = FaultTranslatingRpc.Wrap(inner);

        var ex = await Assert.ThrowsAsync<McpException>(() => wrapped.PingAsync());
        // Proxied calls stamp a per-call correlation id ("[vsmcp:xxxxxxxx]") so the client error,
        // the server log line, and the VSIX vsix.log lines are joinable by one id.
        Assert.StartsWith("VSMCP-not-debugging: no session [vsmcp:", ex.Message);
        Assert.Matches(@"\[vsmcp:[0-9a-f]{8}\]$", ex.Message);
    }

    // ---- Local()/LocalAsync(): server-local (non-proxy) tool bodies translate too (#145 follow-up). ----

    [Fact]
    public async Task Local_SuccessfulBody_ReturnsValue()
        => Assert.Equal(42, await FaultTranslatingRpc.Local(() => 42));

    [Fact]
    public async Task Local_ThrowingBody_SurfacesMcpException()
    {
        var ex = await Assert.ThrowsAsync<McpException>(
            () => FaultTranslatingRpc.Local<int>(() => throw new VsmcpException(ErrorCodes.NotFound, "missing")));
        Assert.Equal("VSMCP-not-found: missing", ex.Message);
    }

    [Fact]
    public async Task LocalAsync_SuccessfulBody_ReturnsValue()
        => Assert.Equal(7, await FaultTranslatingRpc.LocalAsync(() => Task.FromResult(7)));

    [Fact]
    public async Task LocalAsync_SynchronousThrowInBody_SurfacesMcpException()
    {
        // The gate-style throw (e.g. dump.dbgeng disabled) happens before any Task is returned.
        var ex = await Assert.ThrowsAsync<McpException>(
            () => FaultTranslatingRpc.LocalAsync<int>(() => throw new VsmcpException(ErrorCodes.Unsupported, "disabled")));
        Assert.Equal("VSMCP-unsupported: disabled", ex.Message);
    }

    [Fact]
    public async Task LocalAsync_FaultedTaskFromBody_SurfacesMcpException()
    {
        var ex = await Assert.ThrowsAsync<McpException>(
            () => FaultTranslatingRpc.LocalAsync(() => Task.FromException<int>(
                new VsmcpException(ErrorCodes.WrongState, "bad state"))));
        Assert.Equal("VSMCP-wrong-state: bad state", ex.Message);
    }

    private static IVsmcpRpc MakeStub(Func<MethodInfo, object?> onInvoke)
    {
        var proxy = DispatchProxy.Create<IVsmcpRpc, StubRpc>();
        ((StubRpc)(object)proxy).OnInvoke = onInvoke;
        return (IVsmcpRpc)proxy;
    }

    public class StubRpc : DispatchProxy
    {
        public Func<MethodInfo, object?>? OnInvoke;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            => OnInvoke?.Invoke(targetMethod!);
    }
}
