using System.Collections.Generic;
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
}
