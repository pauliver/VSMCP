using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using VSMCP.Shared;

namespace VSMCP.Server;

public sealed partial class VsmcpTools
{
    [McpServerTool(Name = "debug.enc_status")]
    [Description("Report Edit & Continue / Hot Reload availability: whether a debug session is active, whether the debugger is paused, and whether the Debug.ApplyCodeChanges command would currently be dispatched (i.e. the menu would be enabled). When unavailable, returns a human-readable Reason.")]
    public async Task<EncStatusResult> DebugEncStatus(CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.DebugEncStatusAsync(ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "debug.apply_code_changes")]
    [Description("Apply pending edits to the running debuggee via VS's Edit & Continue pipeline (the same machinery as the IDE's Debug → Apply Code Changes menu item). Saves all dirty documents first, then invokes the DTE command. From break mode, ENC patches the live IL; from run mode, takes the Hot Reload path. Failure typically means a 'rude edit' (signature change, new lambda, etc.) — Message includes the diagnostic.")]
    public async Task<EncApplyResult> DebugApplyCodeChanges(CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.DebugApplyCodeChangesAsync(ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "debug.hot_reload")]
    [Description("Apply edits to a running .NET debuggee via Hot Reload. On VS 17.4+ uses Debug.HotReloadApplyCodeChanges; otherwise falls back to Debug.ApplyCodeChanges. Saves all dirty documents first.")]
    public async Task<EncApplyResult> DebugHotReload(CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.DebugHotReloadAsync(ct).ConfigureAwait(false);
    }
}
