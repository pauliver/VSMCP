using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using VSMCP.Core;
using VSMCP.Shared;

namespace VSMCP.Server;

/// <summary>
/// MCP tool surface. One method per tool, decorated with <see cref="McpServerToolAttribute"/>.
/// Connection to VS is lazy; <see cref="VsConnection.GetOrConnectAsync"/> throws
/// <see cref="ErrorCodes.NotConnected"/> when no instance is reachable.
/// </summary>
[McpServerToolType]
public sealed partial class VsmcpTools
{
    private readonly VsConnection _connection;
    private readonly ProfilerHost _profiler;
    private readonly CountersSubscriptionHost _counters;
    private readonly TraceHost _trace;
    private readonly VsmcpConfig _config;

    public VsmcpTools(VsConnection connection, ProfilerHost profiler, CountersSubscriptionHost counters, TraceHost trace, VsmcpConfig config)
    {
        _connection = connection;
        _profiler = profiler;
        _counters = counters;
        _trace = trace;
        _config = config;
    }

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

    [McpServerTool(Name = "vs.focus")]
    [Description("Bring the connected Visual Studio main window to the foreground. Useful for teaching/demos when the human needs to see the IDE react to an AI tool call.")]
    public async Task<FocusResult> VsFocus(CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.VsFocusAsync(ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "vs.set_autofocus")]
    [Description("Toggle teaching-mode auto-focus. When enabled, every dispatched tool call raises the VS window so an observer always sees the effect. Default: enabled.")]
    public async Task<AutoFocusResult> VsSetAutoFocus(
        [Description("True to auto-focus after every tool call (teaching mode); false to suppress.")] bool enabled,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.VsSetAutoFocusAsync(enabled, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "vs.get_autofocus")]
    [Description("Return whether teaching-mode auto-focus is currently enabled on the connected VS instance.")]
    public async Task<AutoFocusResult> VsGetAutoFocus(CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.VsGetAutoFocusAsync(ct).ConfigureAwait(false);
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

    // -------- Solution --------

    [McpServerTool(Name = "solution.info")]
    [Description("Return details about the currently open solution: path, active configuration/platform, startup project, and loaded projects.")]
    public async Task<SolutionInfo> SolutionInfo(CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.SolutionInfoAsync(ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "solution.open")]
    [Description("Open a .sln file in the connected Visual Studio. Closes any currently open solution first.")]
    public async Task<SolutionInfo> SolutionOpen(
        [Description("Absolute path to the .sln file.")] string path,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.SolutionOpenAsync(path, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "solution.close")]
    [Description("Close the currently open solution.")]
    public async Task SolutionClose(
        [Description("Prompt to save modified documents before closing.")] bool saveFirst = true,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        await proxy.SolutionCloseAsync(saveFirst, ct).ConfigureAwait(false);
    }

    // -------- Project --------

    [McpServerTool(Name = "project.list")]
    [Description("Enumerate every concrete (non-folder) project in the current solution.")]
    public async Task<IReadOnlyList<ProjectInfo>> ProjectList(CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.ProjectListAsync(ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "project.add")]
    [Description("Add an existing project file to the current solution, or instantiate a project template.")]
    public async Task<ProjectInfo> ProjectAdd(
        [Description("Path to an existing .csproj/.vbproj/.fsproj/.vcxproj, or to a project template (.vstemplate).")] string templateOrProjectPath,
        [Description("Destination directory when adding from a template. Ignored when adding an existing project.")] string destinationPath = "",
        [Description("Name for the new project when adding from a template. Defaults to the destination folder name.")] string? projectName = null,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.ProjectAddAsync(templateOrProjectPath, destinationPath, projectName, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "project.remove")]
    [Description("Remove a project from the solution (does not delete files from disk).")]
    public async Task ProjectRemove(
        [Description("Project id (UniqueName), name, or full path.")] string projectId,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        await proxy.ProjectRemoveAsync(projectId, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "project.properties.get")]
    [Description("Read project properties. Pass an empty list to fetch all readable properties.")]
    public async Task<IReadOnlyList<PropertyValue>> ProjectPropertiesGet(
        [Description("Project id (UniqueName), name, or full path.")] string projectId,
        [Description("Property names to read; omit or pass an empty array for all.")] IReadOnlyList<string>? keys = null,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.ProjectPropertiesGetAsync(projectId, keys, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "project.properties.set")]
    [Description("Set one or more project properties. Values must be the string form expected by MSBuild.")]
    public async Task ProjectPropertiesSet(
        [Description("Project id (UniqueName), name, or full path.")] string projectId,
        [Description("Map of property name to new value. A null value clears the property.")] IReadOnlyDictionary<string, string?> values,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        await proxy.ProjectPropertiesSetAsync(projectId, values, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "project.file.add")]
    [Description("Add a file to a project. When linkOnly is true the file is referenced in-place; otherwise it is copied under the project folder.")]
    public async Task<ProjectItemRef> ProjectFileAdd(
        [Description("Project id (UniqueName), name, or full path.")] string projectId,
        [Description("Absolute or project-relative file path to add.")] string path,
        [Description("Add as a link rather than copying into the project folder.")] bool linkOnly = false,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.ProjectFileAddAsync(projectId, path, linkOnly, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "project.file.remove")]
    [Description("Remove a file from a project. Optionally delete the file from disk.")]
    public async Task ProjectFileRemove(
        [Description("Project id (UniqueName), name, or full path.")] string projectId,
        [Description("Absolute or project-relative file path to remove.")] string path,
        [Description("Also delete the file from disk. Default: false.")] bool deleteFromDisk = false,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        await proxy.ProjectFileRemoveAsync(projectId, path, deleteFromDisk, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "project.folder.create")]
    [Description("Create a (possibly nested) folder inside a project. Intermediate folders are created as needed.")]
    public async Task<ProjectItemRef> ProjectFolderCreate(
        [Description("Project id (UniqueName), name, or full path.")] string projectId,
        [Description("Relative folder path, using '/' or '\\' as separator.")] string path,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.ProjectFolderCreateAsync(projectId, path, ct).ConfigureAwait(false);
    }

    // -------- File / editor --------

    [McpServerTool(Name = "file.read")]
    [Description("Read a file's contents. If the file is open in the editor, returns the live (possibly unsaved) buffer contents.")]
    public async Task<FileReadResult> FileRead(
        [Description("Absolute file path.")] string path,
        [Description("Optional 1-based inclusive range. Omit to read the whole file.")] FileRange? range = null,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.FileReadAsync(path, range, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "file.write")]
    [Description("Overwrite a file. If the file is open in the editor, the write goes through the text buffer so VS undo/redo still works.")]
    public async Task<FileWriteResult> FileWrite(
        [Description("Absolute file path.")] string path,
        [Description("New file contents.")] string content,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.FileWriteAsync(path, content, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "file.replace_range")]
    [Description("Replace a 1-based inclusive range in a file with new text. Goes through the text buffer when the file is open.")]
    public async Task<FileWriteResult> FileReplaceRange(
        [Description("Absolute file path.")] string path,
        [Description("1-based inclusive range to replace.")] FileRange range,
        [Description("Replacement text. Empty string deletes the range.")] string text,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.FileReplaceRangeAsync(path, range, text, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "editor.open")]
    [Description("Open a file in the Visual Studio editor and optionally move the caret to a 1-based (line, column).")]
    public async Task EditorOpen(
        [Description("Absolute file path.")] string path,
        [Description("1-based line number to move the caret to.")] int? line = null,
        [Description("1-based column number to move the caret to.")] int? column = null,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        await proxy.EditorOpenAsync(path, line, column, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "editor.save")]
    [Description("Save a single open document by its file path.")]
    public async Task EditorSave(
        [Description("Absolute file path of the document to save.")] string path,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        await proxy.EditorSaveAsync(path, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "editor.save_all")]
    [Description("Save every open, dirty document in the connected Visual Studio.")]
    public async Task EditorSaveAll(CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        await proxy.EditorSaveAllAsync(ct).ConfigureAwait(false);
    }

    // -------- Build --------

    [McpServerTool(Name = "build.start")]
    [Description("Start building the current solution (or a subset of projects). Returns immediately with a buildId. Poll build.status or call build.wait.")]
    public async Task<BuildHandle> BuildStart(
        [Description("Solution configuration name (e.g. 'Debug', 'Release'). Omit to use the active configuration.")] string? configuration = null,
        [Description("Target platform (e.g. 'Any CPU', 'x64'). Omit to use the active platform.")] string? platform = null,
        [Description("Optional project ids (UniqueName/Name/FullPath) to limit the build. Omit to build the whole solution.")] IReadOnlyList<string>? projectIds = null,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.BuildStartAsync(configuration, platform, projectIds, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "build.rebuild")]
    [Description("Clean then build the solution (or selected projects). Returns a buildId to poll.")]
    public async Task<BuildHandle> BuildRebuild(
        [Description("Solution configuration name.")] string? configuration = null,
        [Description("Target platform.")] string? platform = null,
        [Description("Optional project ids to limit the rebuild.")] IReadOnlyList<string>? projectIds = null,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.BuildRebuildAsync(configuration, platform, projectIds, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "build.clean")]
    [Description("Clean the solution (or selected projects). Returns a buildId for parity with build.start.")]
    public async Task<BuildHandle> BuildClean(
        [Description("Solution configuration name.")] string? configuration = null,
        [Description("Target platform.")] string? platform = null,
        [Description("Optional project ids to limit the clean.")] IReadOnlyList<string>? projectIds = null,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.BuildCleanAsync(configuration, platform, projectIds, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "build.status")]
    [Description("Current status of a build started via build.start / build.rebuild / build.clean.")]
    public async Task<BuildStatus> BuildStatusQuery(
        [Description("Build id returned from build.start.")] string buildId,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.BuildStatusAsync(buildId, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "build.wait")]
    [Description("Block until the build reaches a terminal state or the timeout elapses. Returns TimedOut status cleanly when the timer wins.")]
    public async Task<BuildStatus> BuildWait(
        [Description("Build id returned from build.start.")] string buildId,
        [Description("Max milliseconds to wait. Omit or set to 0 for no timeout.")] int? timeoutMs = null,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.BuildWaitAsync(buildId, timeoutMs is > 0 ? timeoutMs : null, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "build.cancel")]
    [Description("Request cancellation of an in-flight build.")]
    public async Task<BuildStatus> BuildCancel(
        [Description("Build id returned from build.start.")] string buildId,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.BuildCancelAsync(buildId, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "build.errors")]
    [Description("Errors (severity=Error) produced by a build. Valid after the build has reached a terminal state.")]
    public async Task<IReadOnlyList<BuildDiagnostic>> BuildErrors(
        [Description("Build id returned from build.start.")] string buildId,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.BuildErrorsAsync(buildId, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "build.warnings")]
    [Description("Warnings (severity=Warning) produced by a build. Valid after the build has reached a terminal state.")]
    public async Task<IReadOnlyList<BuildDiagnostic>> BuildWarnings(
        [Description("Build id returned from build.start.")] string buildId,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.BuildWarningsAsync(buildId, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "build.output")]
    [Description("Raw text captured from an Output window pane (defaults to the Build pane) for a completed build.")]
    public async Task<BuildOutput> BuildOutputText(
        [Description("Build id returned from build.start.")] string buildId,
        [Description("Output window pane name. Defaults to 'Build'.")] string? pane = null,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.BuildOutputAsync(buildId, pane, ct).ConfigureAwait(false);
    }

    // -------- Code intelligence (Roslyn) --------

    [McpServerTool(Name = "code.symbols")]
    [Description("Return the document outline for a file: namespaces, types, and members with 1-based source spans. Requires the file to belong to a project loaded in the current solution. Works on any Roslyn-backed language (C#, VB).")]
    public async Task<SymbolsResult> CodeSymbols(
        [Description("Absolute path to the source file.")] string file,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.CodeSymbolsAsync(file, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "code.goto_definition")]
    [Description("Resolve the symbol at a 1-based (line, column) and return its declaration location(s). Returns an empty Locations list when no symbol is at the position. Metadata definitions are skipped (only in-source locations are returned).")]
    public async Task<LocationListResult> CodeGotoDefinition(
        [Description("Source position: { File, Line (1-based), Column (1-based) }.")] CodePosition position,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.CodeGotoDefinitionAsync(position, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "code.find_references")]
    [Description("Find all references (across the solution) to the symbol at a 1-based (line, column). Returns definition locations plus up to `maxResults` reference spans; sets Truncated when more exist.")]
    public async Task<ReferencesResult> CodeFindReferences(
        [Description("Source position: { File, Line (1-based), Column (1-based) }.")] CodePosition position,
        [Description("Max number of reference spans to return (1..5000, default 500).")] int maxResults = 500,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.CodeFindReferencesAsync(position, maxResults, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "code.diagnostics")]
    [Description("Report Roslyn diagnostics (errors, warnings, info) for a single file or the whole solution, without invoking MSBuild. When `file` is null/empty the whole solution is scanned (can be slow on large repos).")]
    public async Task<DiagnosticsResult> CodeDiagnostics(
        [Description("Absolute path to a file. Omit or empty to scan the whole solution.")] string? file = null,
        [Description("Max number of diagnostics to return (1..10000, default 500).")] int maxResults = 500,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.CodeDiagnosticsAsync(file, maxResults, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "code.quick_info")]
    [Description("Return quick-info for the symbol at a 1-based (line, column): display-form signature, kind, and documentation XML (if any). Returns an empty result when no symbol is at the position.")]
    public async Task<QuickInfoResult> CodeQuickInfo(
        [Description("Source position: { File, Line (1-based), Column (1-based) }.")] CodePosition position,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.CodeQuickInfoAsync(position, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "code.list_fixes")]
    [Description("List the Roslyn code fixes (lightbulb actions) available for the diagnostic(s) on the line at a 1-based position — e.g. 'using System;' for CS0103. Each fix has a Title (pass it to code.apply_fix) and the DiagnosticId it addresses.")]
    public async Task<ListFixesResult> CodeListFixes(
        [Description("Position on the line with the diagnostic: { File, Line, Column } (1-based).")] CodePosition position,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.CodeListFixesAsync(position, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "code.apply_fix")]
    [Description("Apply a Roslyn code fix at a 1-based position, by Title (from code.list_fixes) or the first available when title is omitted. Edits are applied through the workspace so VS undo works. Returns the applied title or an error.")]
    public async Task<ApplyFixResult> CodeApplyFix(
        [Description("Position on the line with the diagnostic: { File, Line, Column } (1-based).")] CodePosition position,
        [Description("Exact fix Title to apply; omit to apply the first available fix.")] string? title = null,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.CodeApplyFixAsync(position, title, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "code.call_hierarchy")]
    [Description("Call hierarchy for the symbol at a 1-based position. direction='callers' (default) finds everything that calls it (across the solution); direction='callees' lists the methods it calls. Each entry has the calling/called signature and in-source locations.")]
    public async Task<CallHierarchyResult> CodeCallHierarchy(
        [Description("Source position of the symbol: { File, Line, Column } (1-based).")] CodePosition position,
        [Description("'callers' or 'callees'. Default 'callers'.")] string direction = "callers",
        [Description("Max entries (1..2000, default 200).")] int maxResults = 200,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.CodeCallHierarchyAsync(position, direction, maxResults, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "code.type_surface")]
    [Description("Public API surface (member signatures) of the type at a 1-based position — including framework/metadata types that code.goto_definition can't open (FromMetadata=true). Use this to see what a 3rd-party/BCL type offers without its source.")]
    public async Task<TypeSurfaceResult> CodeTypeSurface(
        [Description("Position resolving to a type or a member of one: { File, Line, Column } (1-based).")] CodePosition position,
        [Description("Max members (1..3000, default 300).")] int maxResults = 300,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.CodeTypeSurfaceAsync(position, maxResults, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "code.complete")]
    [Description("Completion candidates at a 1-based position. After 'expr.' returns the accessible members of expr's type (including inherited); otherwise the symbols in scope. Each item has Name, Kind, and a display Signature. Useful for discovering an API surface without reading the type's source.")]
    public async Task<CompletionResult> CodeComplete(
        [Description("Source position: { File, Line, Column } (1-based).")] CodePosition position,
        [Description("Max candidates (1..500, default 100).")] int maxResults = 100,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.CodeCompleteAsync(position, maxResults, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "code.signature_help")]
    [Description("Overload signatures for the method call enclosing a 1-based position, plus the active parameter index (commas before the caret). Returns an empty list when the position isn't inside an invocation.")]
    public async Task<SignatureHelpResult> CodeSignatureHelp(
        [Description("Source position inside a call's argument list: { File, Line, Column } (1-based).")] CodePosition position,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.CodeSignatureHelpAsync(position, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "code.format")]
    [Description("Format a C#/VB document (or a 1-based line/column range) with the Roslyn Formatter, honoring the project's .editorconfig. Applied through the workspace so it groups with VS undo and shows in open buffers. Returns whether anything changed.")]
    public async Task<FormatResult> CodeFormat(
        [Description("Absolute path to the source file.")] string file,
        [Description("Optional 1-based inclusive range to format. Omit to format the whole file.")] FileRange? range = null,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.CodeFormatAsync(file, range, ct).ConfigureAwait(false);
    }

    // -------- Streaming counters (M8) --------

    [McpServerTool(Name = "counters.subscribe")]
    [Description("Start a background poller that samples process-level counters (CPU%, working set, handle/thread counts, …) at a fixed cadence. Samples are buffered server-side in a ring (up to 256); drain them with counters.read. Stops on process exit or counters.unsubscribe. Unlike counters.get, this does not block the caller for a sample window.")]
    public Task<CountersSubscriptionHandle> CountersSubscribe(
        [Description("Target process id.")] int pid,
        [Description("Sampling interval in milliseconds (100..60000). Default 500ms.")] int sampleMs = 500,
        CancellationToken ct = default)
    {
        return Task.FromResult(_counters.Subscribe(pid, sampleMs));
    }

    [McpServerTool(Name = "counters.read")]
    [Description("Drain buffered samples from a subscription. Returns up to `maxSamples` snapshots in FIFO order and clears them from the buffer. Reports how many samples were dropped because the ring wrapped and whether the subscription has ended.")]
    public Task<CountersReadResult> CountersRead(
        [Description("Subscription id from counters.subscribe.")] string subscriptionId,
        [Description("Max samples to return (1..256). Default 256.")] int maxSamples = 256,
        CancellationToken ct = default)
    {
        return Task.FromResult(_counters.Read(subscriptionId, maxSamples));
    }

    [McpServerTool(Name = "counters.unsubscribe")]
    [Description("Stop a counters subscription and free its buffer. Returns the total sample count, dropped count, and duration.")]
    public Task<CountersUnsubscribeResult> CountersUnsubscribe(
        [Description("Subscription id from counters.subscribe.")] string subscriptionId,
        CancellationToken ct = default)
    {
        return Task.FromResult(_counters.Unsubscribe(subscriptionId));
    }

    // -------- ETW tracing (M8) --------

    [McpServerTool(Name = "trace.start")]
    [Description("Start a system-wide ETW trace session. Requires Administrator — user-mode ETW needs SeSystemProfilePrivilege. Providers may be named (\"Microsoft-Windows-DotNETRuntime\") or GUID-formatted. Kernel keywords use KernelTraceEventParser.Keywords names (Process, ImageLoad, Thread, DiskIO, NetworkTCPIP, ContextSwitch, …). Events are streamed to the output .etl until trace.stop.")]
    public Task<TraceStartResult> TraceStart(
        [Description("Start options. Providers + optional kernel keywords + optional output path.")] TraceStartOptions options,
        CancellationToken ct = default)
    {
        return Task.FromResult(_trace.Start(options));
    }

    [McpServerTool(Name = "trace.stop")]
    [Description("Stop an ETW trace session by id. Flushes the .etl and returns the final file size and duration.")]
    public Task<TraceStopResult> TraceStop(
        [Description("Session id from trace.start.")] string sessionId,
        CancellationToken ct = default)
    {
        return Task.FromResult(_trace.Stop(sessionId));
    }

    [McpServerTool(Name = "trace.report")]
    [Description("Summarize a .etl file: total event count, wall-clock duration, per-provider event counts, and the top N (provider, event) pairs by frequency. Uses Microsoft.Diagnostics.Tracing.TraceEvent; does not require admin.")]
    public Task<TraceReport> TraceReport(
        [Description("Absolute path to a .etl file.")] string path,
        [Description("Max (provider, event) pairs to return (1..1000, default 20).")] int top = 20,
        CancellationToken ct = default)
    {
        return Task.FromResult(_trace.Report(path, top));
    }

    // -------- Diagnostic Tools (M11) --------

    [McpServerTool(Name = "diag.events_list")]
    [Description("List debug-session events captured by the VSIX during this VS session: exceptions (thrown / unhandled), breakpoint hits, and user breaks. Newest events appear last. Returns up to maxResults items. filter values: 'all' (default), 'exception' (both thrown and unhandled), 'exceptionthrown', 'exceptionunhandled', 'breakpoint', 'userbreak'.")]
    public async Task<DiagEventsResult> DiagEventsList(
        [Description("Event kind filter. 'exception' matches both thrown and unhandled. Omit for all.")] string? filter = null,
        [Description("Max events to return (1..200, default 100).")] int maxResults = 100,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.DiagEventsListAsync(filter, maxResults, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "diag.events_watch")]
    [Description("Long-poll for new debug events. Blocks until at least one event newer than sinceTimestampMs arrives, or timeoutMs elapses (max 30 s). On return, use LatestTimestampMs as sinceTimestampMs on the next call to receive only subsequent events. Ideal for tight watch loops: call repeatedly to stream events without polling diag.events_list at fixed intervals.")]
    public async Task<DiagEventsResult> DiagEventsWatch(
        [Description("Receive only events with TimestampMs > this value. Pass 0 on the first call; pass the LatestTimestampMs from the previous result on subsequent calls.")] long sinceTimestampMs = 0,
        [Description("Event kind filter (same values as diag.events_list).")] string? filter = null,
        [Description("Max events to return per call (1..200, default 50).")] int maxResults = 50,
        [Description("How long to wait for new events in ms (100..30000, default 10000).")] int timeoutMs = 10_000,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.DiagEventsWatchAsync(filter, maxResults, sinceTimestampMs, timeoutMs, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "diag.event_detail")]
    [Description("Return full detail for a single event by id (from diag.events_list): exception type, message, exception code, thread id/name, and the top stack frames captured at the moment the event fired. Equivalent to double-clicking an event in the VS Diagnostic Tools window.")]
    public async Task<DiagEventDetail> DiagEventDetail(
        [Description("Event id from diag.events_list.")] string eventId,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.DiagEventDetailAsync(eventId, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "diag.events_clear")]
    [Description("Clear the in-memory event buffer. Useful before starting a repro so the list only contains events from this run.")]
    public async Task DiagEventsClear(CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        await proxy.DiagEventsClearAsync(ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "diag.memory_snapshot")]
    [Description("Snapshot the debugged process's memory at this instant: working set, private bytes. Also reports the managed GC heap size of the VS host process (devenv.exe) as a cross-check. Full managed heap snapshot (by type) requires IVsDiagnosticsHub and is not yet implemented.")]
    public async Task<DiagMemorySnapshot> DiagMemorySnapshot(CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.DiagMemorySnapshotAsync(ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "diag.cpu_timeline")]
    [Description("Return CPU% and working-set samples collected by the background 1-second sampler for the debugged process. windowMs limits how far back to look (omit for all available history, up to 5 minutes). Use diag.events_list for event correlation; use counters.get for an on-demand one-shot sample.")]
    public async Task<DiagCpuTimelineResult> DiagCpuTimeline(
        [Description("Restrict samples to the last N milliseconds. Omit for all history.")] int? windowMs = null,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.DiagCpuTimelineAsync(windowMs, ct).ConfigureAwait(false);
    }
}
