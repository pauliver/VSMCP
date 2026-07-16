using System.Collections.Generic;
using ModelContextProtocol;
using VSMCP.Server;
using VSMCP.Shared;
using Xunit;

namespace VSMCP.Tests.Server;

/// <summary>
/// Guards the connection-stage error contract. These faults are thrown OUTSIDE the
/// FaultTranslatingRpc proxy, so they must be <see cref="McpException"/> (not a plain
/// InvalidOperationException the MCP SDK would sanitize to "An error occurred"), and they must carry
/// the actionable remediation text an agent needs.
/// </summary>
public class VsConnectionTests
{
    [Fact]
    public void SelectSingleInstance_returns_pid_when_exactly_one()
    {
        var only = new[] { new VsInstance { ProcessId = 4242 } };
        Assert.Equal(4242, VsConnection.SelectSingleInstanceOrThrow(only));
    }

    [Fact]
    public void SelectSingleInstance_throws_McpException_when_none()
    {
        var ex = Assert.Throws<McpException>(
            () => VsConnection.SelectSingleInstanceOrThrow(new List<VsInstance>()));
        Assert.Contains(ErrorCodes.NotConnected, ex.Message);
        Assert.Contains("Open VS", ex.Message);
    }

    [Fact]
    public void SelectSingleInstance_directs_to_vs_select_when_multiple()
    {
        var many = new[]
        {
            new VsInstance { ProcessId = 1 },
            new VsInstance { ProcessId = 2 },
        };
        var ex = Assert.Throws<McpException>(() => VsConnection.SelectSingleInstanceOrThrow(many));
        Assert.Contains(ErrorCodes.NotConnected, ex.Message);
        Assert.Contains("vs.select", ex.Message);
    }
}
