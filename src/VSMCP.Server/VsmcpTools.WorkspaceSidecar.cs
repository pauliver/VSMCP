using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using VSMCP.Shared;

namespace VSMCP.Server;

public sealed partial class VsmcpTools
{
    [McpServerTool(Name = "project.load")]
    [Description("Load a single .csproj into the in-process sidecar workspace. Use this in VS Open Folder mode (no .sln) so Roslyn-aware tools (file_outline, code_find_symbol, file_classes, etc.) can resolve the project's documents. Globs *.cs under the csproj's directory; skips bin/obj/.vs/etc. Re-running on the same csproj reloads it. Pure read-side: edit_* tools (rename, move_type, move_method, etc.) still need a real .sln-loaded VS workspace.")]
    public async Task<ProjectLoadResult> ProjectLoad(
        [Description("Absolute path to the .csproj file.")] string csprojPath,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.ProjectLoadAsync(csprojPath, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "project.load_workspace_folder")]
    [Description("Scan the open Folder workspace (or a specified rootPath) for .csproj files and load each into the sidecar workspace. Single-call setup for an Open Folder session: after this returns, code_find_symbol / file_outline / file_classes etc. resolve the workspace's projects.")]
    public async Task<ProjectLoadFolderResult> ProjectLoadWorkspaceFolder(
        [Description("Root directory to scan. Omit to use the currently-open Folder workspace path.")] string? rootPath = null,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.ProjectLoadWorkspaceFolderAsync(rootPath, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "project.sidecar_status")]
    [Description("Return what's currently loaded in the sidecar workspace: list of .csproj paths and total document count.")]
    public async Task<SidecarStatusResult> ProjectSidecarStatus(CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.ProjectSidecarStatusAsync(ct).ConfigureAwait(false);
    }
}
