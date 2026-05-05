using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using VSMCP.Shared;

namespace VSMCP.Server;

public sealed partial class VsmcpTools
{
    [McpServerTool(Name = "file.move_many")]
    [Description("Bulk rename / move files on disk. For each pair, optionally updates the owning .csproj's <Compile Include> entry (legacy non-SDK projects only — SDK-style projects auto-glob and need no edit). Cross-project moves are rejected. Same-path no-op pairs are skipped. Set dryRun=true to preview the planned outcomes without touching disk or csproj. Validation is fail-fast: if any pair is invalid, NO mutations are applied. Run editor.save_all first if any of the files are open in VS — moving an open file leaves a stale buffer.")]
    public async Task<FileMoveManyResult> FileMoveMany(
        [Description("List of {from, to} pairs. Paths may be absolute or relative; resolved via Path.GetFullPath at the VSIX side.")] IReadOnlyList<FileMovePair> moves,
        [Description("When true (default), edit the owning .csproj's <Compile Include> entry to reflect the new path. No-op for SDK-style csprojs.")] bool updateProject = true,
        [Description("When true, validate and report planned outcomes WITHOUT moving files or editing csprojs. Use this to verify the plan before pulling the trigger on bulk renames.")] bool dryRun = false,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.FileMoveManyAsync(moves, updateProject, dryRun, ct).ConfigureAwait(false);
    }
}
