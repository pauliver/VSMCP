using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using VSMCP.Shared;

namespace VSMCP.Server;

public sealed partial class VsmcpTools
{
    [McpServerTool(Name = "editor.navigate_to")]
    [Description("Navigate the editor to a file, optionally at a specific line/column. When openInEditor=true (default), opens or activates the document.")]
    public async Task<NavigateResult> EditorNavigateTo(
        [Description("Absolute file path.")] string file,
        [Description("1-based line number.")] int? line = null,
        [Description("1-based column number.")] int? column = null,
        [Description("Open and focus the document. Default true.")] bool openInEditor = true,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.EditorNavigateToAsync(file, line, column, openInEditor, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "editor.snippet")]
    [Description("Return a window of lines around a target line: contextBefore lines before + the target line + contextAfter lines after. Use this for cheap, file-system-only context without opening the editor.")]
    public async Task<SnippetResult> EditorSnippet(
        [Description("Absolute file path.")] string file,
        [Description("1-based target line.")] int line,
        [Description("Lines of context before (default 5).")] int contextBefore = 5,
        [Description("Lines of context after (default 5).")] int contextAfter = 5,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.EditorSnippetAsync(file, line, contextBefore, contextAfter, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "editor.expand_region")]
    [Description("Expand the outlining region containing the target line in VS. Best-effort: invokes Edit.ToggleOutliningExpansion. Returns the #region/#endregion span when present in source.")]
    public async Task<RegionResult> EditorExpandRegion(
        [Description("Absolute file path.")] string file,
        [Description("1-based line within the region.")] int line,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.EditorExpandRegionAsync(file, line, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "editor.collapse_region")]
    [Description("Collapse the outlining region containing the target line in VS. Best-effort.")]
    public async Task<RegionResult> EditorCollapseRegion(
        [Description("Absolute file path.")] string file,
        [Description("1-based line within the region.")] int line,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.EditorCollapseRegionAsync(file, line, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "editor.navigate_to_include")]
    [Description("Resolve a #include in a C/C++ file and open the target header. Searches the file's #include list for a matching name.")]
    public async Task<IncludeNavigationResult> EditorNavigateToInclude(
        [Description("C/C++ source file.")] string file,
        [Description("Include name as it appears in the #include directive (e.g. 'foo.h').")] string includeName,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.EditorNavigateToIncludeAsync(file, includeName, ct).ConfigureAwait(false);
    }
}
