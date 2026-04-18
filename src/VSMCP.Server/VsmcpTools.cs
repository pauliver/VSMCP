using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using VSMCP.Shared;

namespace VSMCP.Server;

/// <summary>
/// MCP tool surface. One method per tool, decorated with <see cref="McpServerToolAttribute"/>.
/// Connection to VS is lazy; <see cref="VsConnection.GetOrConnectAsync"/> throws
/// <see cref="ErrorCodes.NotConnected"/> when no instance is reachable.
/// </summary>
[McpServerToolType]
public sealed class VsmcpTools
{
    private readonly VsConnection _connection;

    public VsmcpTools(VsConnection connection) => _connection = connection;

    [McpServerTool(Name = "ping")]
    [Description("Round-trip ping to the connected Visual Studio instance. Returns 'pong' and a server-side timestamp.")]
    public async Task<PingResult> Ping(CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.PingAsync(ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "vs.status")]
    [Description("Summary of the connected Visual Studio: solution path, active configuration, startup project, and debug mode.")]
    public async Task<VsStatus> VsStatus(CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.GetStatusAsync(ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "vs.list_instances")]
    [Description("Enumerate running Visual Studio instances that have the VSMCP extension loaded. Use this when multiple VS windows are open.")]
    public Task<System.Collections.Generic.IReadOnlyList<VsInstance>> VsListInstances(CancellationToken ct = default)
        => Task.FromResult(VsConnection.ListInstances());

    [McpServerTool(Name = "vs.select")]
    [Description("Bind future tool calls to a specific Visual Studio process (by PID). Call vs.list_instances first to see options.")]
    public async Task<VsStatus> VsSelect(
        [Description("Process id of the VS instance to target.")] int processId,
        CancellationToken ct = default)
    {
        await _connection.ConnectToAsync(processId, ct).ConfigureAwait(false);
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.GetStatusAsync(ct).ConfigureAwait(false);
    }
}
