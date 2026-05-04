using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using VSMCP.Shared;

namespace VSMCP.Server;

public sealed partial class VsmcpTools
{
    [McpServerTool(Name = "editor.active")]
    [Description("Return information about the currently active document in VS: file path, language, dirty state, and cursor position. Use this for 'fix this'-style requests where 'this' is whatever the user is looking at.")]
    public async Task<ActiveEditorInfo> EditorActive(CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.EditorActiveAsync(ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "editor.selection")]
    [Description("Return the active document's current text selection (file, range, and selected text). Returns null if there is no active selection.")]
    public async Task<EditorSelection?> EditorSelection(CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.EditorSelectionAsync(ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "editor.cursor")]
    [Description("Return the active document's cursor position (file/line/column, 1-based). Returns null if no document is active.")]
    public async Task<CodePosition?> EditorCursor(CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.EditorCursorAsync(ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "editor.insert_at_cursor")]
    [Description("Insert text at the active document's cursor position. The edit goes through the editor buffer so it's grouped with VS undo.")]
    public async Task<FileWriteResult> EditorInsertAtCursor(
        [Description("Text to insert.")] string text,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.EditorInsertAtCursorAsync(text, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "workspace.events_list")]
    [Description("Snapshot of recent workspace events (build, save, document open/close, debug state changes). Newest events appear last. Use workspace.watch instead if you want to stream new events as they arrive.")]
    public async Task<WorkspaceEventsResult> WorkspaceEventsList(
        [Description("Max events to return (default 100).")] int maxResults = 100,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.WorkspaceEventsListAsync(maxResults, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "workspace.watch")]
    [Description("Long-poll for new workspace events. Blocks until at least one event newer than sinceTimestampMs arrives, or timeoutMs elapses (max 30s). Cursor pattern mirrors diag.events_watch.")]
    public async Task<WorkspaceEventsResult> WorkspaceWatch(
        [Description("Cursor: pass 0 on first call, then result.LatestTimestampMs.")] long sinceTimestampMs = 0,
        [Description("Wait timeout in ms (100..30000, default 10000).")] int timeoutMs = 10_000,
        [Description("Max events per call (default 50).")] int maxResults = 50,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.WorkspaceWatchAsync(sinceTimestampMs, timeoutMs, maxResults, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "test.discover")]
    [Description("Enumerate tests in built test assemblies via vstest.console.exe. Reads each project's bin output (auto-detects net4x and net6+/net9 layouts). Tests must be built before discovery.")]
    public async Task<TestDiscoveryResult> TestDiscover(
        [Description("Project to scope. Omit for all test projects in the solution.")] string? projectId = null,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.TestDiscoverAsync(projectId, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "test.run")]
    [Description("Run tests via vstest.console.exe. Filter format is the standard /TestCaseFilter syntax (e.g. 'FullyQualifiedName~MyTest', 'TestCategory=fast'). Returns pass/fail counts plus the raw output.")]
    public async Task<TestRunResult> TestRun(
        [Description("vstest /TestCaseFilter expression. Omit to run all tests.")] string? filter = null,
        [Description("Project to scope. Omit for all test projects.")] string? projectId = null,
        [Description("Configuration (Debug/Release). Omit for the project's active configuration.")] string? configuration = null,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.TestRunAsync(filter, projectId, configuration, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "nuget.list")]
    [Description("List NuGet PackageReferences across the solution (or one project). Reads .csproj XML directly + falls back to packages.config for legacy projects. Each entry includes id, version, and project id.")]
    public async Task<NuGetListResult> NugetList(
        [Description("Project to scope. Omit for whole solution.")] string? projectId = null,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.NugetListAsync(projectId, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "nuget.add")]
    [Description("Add a PackageReference to a project (or update version if it already exists). Edits the .csproj XML directly; VS will reload and restore on next interaction.")]
    public async Task<NuGetActionResult> NugetAdd(
        [Description("Project unique-name.")] string projectId,
        [Description("NuGet package id (e.g. 'Newtonsoft.Json').")] string packageId,
        [Description("Version (e.g. '13.0.3'). Omit to add without an explicit version (NuGet will float).")] string? version = null,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.NugetAddAsync(projectId, packageId, version, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "nuget.remove")]
    [Description("Remove a PackageReference from a project (.csproj edit). Returns Success=false if the package wasn't referenced.")]
    public async Task<NuGetActionResult> NugetRemove(
        [Description("Project unique-name.")] string projectId,
        [Description("NuGet package id.")] string packageId,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.NugetRemoveAsync(projectId, packageId, ct).ConfigureAwait(false);
    }
}
