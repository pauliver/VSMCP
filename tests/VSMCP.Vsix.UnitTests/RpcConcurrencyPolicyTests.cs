using VSMCP.Core;
using Xunit;

namespace VSMCP.Tests.Vsix;

/// <summary>
/// Locks the serialize-vs-exempt decision for the host-wide RPC gate. Mutating and ordinary tools
/// must be serialized; long-poll waiters and trivial meta calls must be exempt or they would
/// head-of-line-block the entire tool surface.
/// </summary>
public class RpcConcurrencyPolicyTests
{
    [Theory]
    [InlineData("EditReplaceMemberAsync")]
    [InlineData("BuildStartAsync")]
    [InlineData("FileWriteAsync")]
    [InlineData("CppRenameAsync")]
    public void Mutating_and_ordinary_tools_are_exclusive(string method)
        => Assert.True(RpcConcurrencyPolicy.RequiresExclusive(method));

    [Theory]
    [InlineData("PingAsync")]
    [InlineData("HandshakeAsync")]
    [InlineData("BuildWaitAsync")]
    [InlineData("DiagEventsWatchAsync")]
    [InlineData("WorkspaceWatchAsync")]
    public void Meta_and_longpoll_methods_are_exempt(string method)
        => Assert.False(RpcConcurrencyPolicy.RequiresExclusive(method));
}
