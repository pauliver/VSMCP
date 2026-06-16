using System;
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
}
