using System;
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

    // -------- Debug control --------

    [McpServerTool(Name = "debug.launch")]
    [Description("Start a debug session for a project (or the solution's startup project). Returns immediately; poll debug.state to track the transition.")]
    public async Task<DebugActionResult> DebugLaunch(
        [Description("Project id (UniqueName/Name/FullPath). Omit for the configured startup project.")] string? projectId = null,
        [Description("Override command-line arguments.")] string? args = null,
        [Description("Environment variables to layer on top of the project's configured values.")] Dictionary<string, string>? env = null,
        [Description("Override the working directory.")] string? cwd = null,
        [Description("Start without the debugger (equivalent to Ctrl+F5).")] bool noDebug = false,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.DebugLaunchAsync(
            new DebugLaunchOptions { ProjectId = projectId, Args = args, Env = env, Cwd = cwd, NoDebug = noDebug },
            ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "debug.attach")]
    [Description("Attach the VS debugger to an already-running process by pid or name.")]
    public async Task<DebugActionResult> DebugAttach(
        [Description("Process id. Either pid or processName must be provided.")] int? pid = null,
        [Description("Process name without extension (case-insensitive). First match wins.")] string? processName = null,
        [Description("Optional debug engine names (e.g. 'Managed (.NET Core)', 'Native'). Omit to let VS pick.")] List<string>? engines = null,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.DebugAttachAsync(
            new DebugAttachOptions { Pid = pid, ProcessName = processName, Engines = engines },
            ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "debug.stop")]
    [Description("Terminate the debuggee via DTE.Debugger.Stop. May show a modal confirmation dialog on VS 2022+; prefer debug.stop_command or debug.kill_and_stop if that is a problem.")]
    public async Task<DebugActionResult> DebugStop(CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.DebugStopAsync(ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "debug.stop_command")]
    [Description("Stop debugging via ExecuteCommand(\"Debug.StopDebugging\") — fires the same action as the toolbar button, which VS handles asynchronously and is less likely to block on a modal dialog.")]
    public async Task<DebugActionResult> DebugStopCommand(CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.DebugStopCommandAsync(ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "debug.kill_and_stop")]
    [Description("Kill all debugged processes via System.Diagnostics.Process.Kill, then call Stop. Eliminates any VS modal prompts caused by a live process. Use when debug.stop or debug.stop_command hangs on a dialog.")]
    public async Task<DebugActionResult> DebugKillAndStop(CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.DebugKillAndStopAsync(ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "debug.detach")]
    [Description("Detach from all debuggees without terminating them.")]
    public async Task<DebugActionResult> DebugDetach(CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.DebugDetachAsync(ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "debug.restart")]
    [Description("Restart the current debug session.")]
    public async Task<DebugActionResult> DebugRestart(CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.DebugRestartAsync(ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "debug.break_all")]
    [Description("Break into all threads and enter break mode.")]
    public async Task<DebugActionResult> DebugBreakAll(CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.DebugBreakAllAsync(ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "debug.continue")]
    [Description("Resume execution from break mode.")]
    public async Task<DebugActionResult> DebugContinue(CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.DebugContinueAsync(ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "debug.step_into")]
    [Description("Step into the next call on the current thread.")]
    public async Task<DebugActionResult> DebugStepInto(CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.DebugStepIntoAsync(ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "debug.step_over")]
    [Description("Step over the next statement, treating calls as atomic.")]
    public async Task<DebugActionResult> DebugStepOver(CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.DebugStepOverAsync(ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "debug.step_out")]
    [Description("Run until the current function returns.")]
    public async Task<DebugActionResult> DebugStepOut(CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.DebugStepOutAsync(ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "debug.run_to_cursor")]
    [Description("Resume execution and break when the specified line is about to execute.")]
    public async Task<DebugActionResult> DebugRunToCursor(
        [Description("Absolute path of the file containing the target line.")] string file,
        [Description("1-based line number to run to.")] int line,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.DebugRunToCursorAsync(file, line, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "debug.set_next_statement")]
    [Description("Move the instruction pointer to a different line. Requires Break mode and explicit allowSideEffects=true; can corrupt program state.")]
    public async Task<DebugActionResult> DebugSetNextStatement(
        [Description("Absolute path of the file.")] string file,
        [Description("1-based line to jump to. Must be in the current function.")] int line,
        [Description("Must be true. Acknowledges that constructors/resources may be skipped.")] bool allowSideEffects = false,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.DebugSetNextStatementAsync(file, line, allowSideEffects, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "debug.state")]
    [Description("Snapshot of the debugger: mode, stopped reason, current process/thread/frame, last exception if any.")]
    public async Task<DebugInfo> DebugState(CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.DebugStateAsync(ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "bp.set")]
    [Description("Set a breakpoint. Supports line (file+line), function, address, and data breakpoints, with optional condition, hit count, and disabled-on-create.")]
    public async Task<BreakpointInfo> BreakpointSet(
        [Description("Breakpoint options. For a line breakpoint set Kind=Line, File, and Line. See BreakpointSetOptions for the full schema.")] BreakpointSetOptions options,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.BreakpointSetAsync(options, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "bp.set_tracepoint")]
    [Description("Set a tracepoint (logpoint): emits a message to the Output window when hit without breaking. Use {expr} inside the message to evaluate expressions.")]
    public async Task<BreakpointInfo> BreakpointSetTracepoint(
        [Description("Absolute file path.")] string file,
        [Description("1-based line number.")] int line,
        [Description("Message to log. Use {expression} tokens for runtime values.")] string message,
        [Description("Optional condition expression (break/log only when true).")] string? condition = null,
        CancellationToken ct = default)
    {
        var options = new BreakpointSetOptions
        {
            Kind = BreakpointKind.Line,
            File = file,
            Line = line,
            TracepointMessage = message,
            ConditionKind = string.IsNullOrWhiteSpace(condition) ? BreakpointConditionKind.None : BreakpointConditionKind.WhenTrue,
            ConditionExpression = condition,
        };
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.BreakpointSetAsync(options, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "bp.list")]
    [Description("List all breakpoints created through VSMCP in this session.")]
    public async Task<BreakpointListResult> BreakpointList(CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.BreakpointListAsync(ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "bp.remove")]
    [Description("Remove a breakpoint by its VSMCP-minted id.")]
    public async Task BreakpointRemove(
        [Description("Breakpoint id returned from bp.set / bp.list.")] string bpId,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        await proxy.BreakpointRemoveAsync(bpId, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "bp.enable")]
    [Description("Enable a previously-disabled breakpoint.")]
    public async Task<BreakpointInfo> BreakpointEnable(
        [Description("Breakpoint id.")] string bpId,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.BreakpointEnableAsync(bpId, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "bp.disable")]
    [Description("Disable a breakpoint without removing it.")]
    public async Task<BreakpointInfo> BreakpointDisable(
        [Description("Breakpoint id.")] string bpId,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.BreakpointDisableAsync(bpId, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "threads.list")]
    [Description("List all threads in the debuggee (paused or running). Requires an active debug session.")]
    public async Task<ThreadListResult> ThreadsList(CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.ThreadsListAsync(ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "threads.freeze")]
    [Description("Freeze a thread so it won't run when the debugger continues.")]
    public async Task<ThreadInfo> ThreadsFreeze(
        [Description("Thread id (as reported by threads.list).")] int threadId,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.ThreadsFreezeAsync(threadId, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "threads.thaw")]
    [Description("Unfreeze a previously frozen thread.")]
    public async Task<ThreadInfo> ThreadsThaw(
        [Description("Thread id.")] int threadId,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.ThreadsThawAsync(threadId, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "threads.switch")]
    [Description("Make the given thread the debugger's current thread. Subsequent stack/locals/eval calls default to this thread.")]
    public async Task<ThreadInfo> ThreadsSwitch(
        [Description("Thread id.")] int threadId,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.ThreadsSwitchAsync(threadId, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "stack.get")]
    [Description("Get the call stack for a thread (defaults to the current thread). Pass depth to cap the number of returned frames.")]
    public async Task<StackGetResult> StackGet(
        [Description("Optional thread id. Omit to use the current thread.")] int? threadId = null,
        [Description("Optional max number of frames (from top of stack).")] int? depth = null,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.StackGetAsync(threadId, depth, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "frame.switch")]
    [Description("Select a stack frame on a thread. Defaults to the current thread.")]
    public async Task<StackFrameInfo> FrameSwitch(
        [Description("Frame index (0 = top of stack).")] int frameIndex,
        [Description("Optional thread id. Omit for the current thread.")] int? threadId = null,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.FrameSwitchAsync(threadId, frameIndex, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "frame.locals")]
    [Description("Get local variables for a frame. expandDepth controls lazy expansion of composite values (0 = no children).")]
    public async Task<VariableListResult> FrameLocals(
        [Description("Optional thread id (default: current).")] int? threadId = null,
        [Description("Optional frame index (default: current frame).")] int? frameIndex = null,
        [Description("How many levels of children to expand. 0 disables expansion.")] int expandDepth = 0,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.FrameLocalsAsync(threadId, frameIndex, expandDepth, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "frame.arguments")]
    [Description("Get the arguments of the currently-selected frame (or specified frame).")]
    public async Task<VariableListResult> FrameArguments(
        [Description("Optional thread id (default: current).")] int? threadId = null,
        [Description("Optional frame index (default: current frame).")] int? frameIndex = null,
        [Description("How many levels of children to expand. 0 disables expansion.")] int expandDepth = 0,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.FrameArgumentsAsync(threadId, frameIndex, expandDepth, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "eval.expression")]
    [Description("Evaluate an expression in the current (or specified) frame. Refuses side-effecting calls unless allowSideEffects=true.")]
    public async Task<EvalResult> EvalExpression(
        [Description("Evaluation options. Expression is required. See EvalOptions for the full schema.")] EvalOptions options,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.EvalExpressionAsync(options, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "modules.list")]
    [Description("List modules loaded into the debuggee, with symbol state, load address, and version. Requires an active debug session; modules are tracked starting from when VS loads this extension.")]
    public async Task<ModuleListResult> ModulesList(CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.ModulesListAsync(ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "symbols.load")]
    [Description("Force-load symbols for a module (using the current symbol search paths and servers).")]
    public async Task<SymbolStatusResult> SymbolsLoad(
        [Description("Module id from modules.list.")] string moduleId,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.SymbolsLoadAsync(moduleId, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "symbols.status")]
    [Description("Report symbol-load status for a module (Loaded/NotLoaded/Stripped) with a verbose search log when available.")]
    public async Task<SymbolStatusResult> SymbolsStatus(
        [Description("Module id from modules.list.")] string moduleId,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.SymbolsStatusAsync(moduleId, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "memory.read")]
    [Description("Read raw bytes from the debuggee's address space. Requires an active debug session broken into a frame where the address is resolvable (native/C++ frames work best). Reads are capped at 64 KiB per call.")]
    public async Task<MemoryReadResult> MemoryRead(
        [Description("Address to read from. Decimal or 0x-prefixed hex (e.g. '0x7ff6abcd1234').")] string address,
        [Description("Number of bytes to read (1..65536).")] int length,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.MemoryReadAsync(address, length, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "memory.write")]
    [Description("Write raw bytes into the debuggee's address space. Destructive: requires allowSideEffects=true. Writes are capped at 64 KiB per call.")]
    public async Task<MemoryWriteResult> MemoryWrite(
        [Description("Address to write to. Decimal or 0x-prefixed hex.")] string address,
        [Description("Payload as hex (whitespace, '-', ',', ':' are ignored; even nibble count required).")] string hex,
        [Description("Must be true to actually perform the write. Defaults to false.")] bool allowSideEffects = false,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.MemoryWriteAsync(address, hex, allowSideEffects, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "registers.get")]
    [Description("Return CPU registers for a thread/frame, grouped (e.g. CPU, CPU Segments, Floating Point, SSE). Defaults to the current thread and current frame.")]
    public async Task<RegistersResult> RegistersGet(
        [Description("Thread id. Omit to use the current thread.")] int? threadId = null,
        [Description("Frame index (0 = innermost). Omit to use the current frame.")] int? frameIndex = null,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.RegistersGetAsync(threadId, frameIndex, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "disasm.get")]
    [Description("Return disassembled instructions starting at an address. Includes opcode bytes, mnemonic, operands, nearest symbol, and source file/line when available. Capped at 4096 instructions per call.")]
    public async Task<DisasmResult> DisasmGet(
        [Description("Start address. Decimal or 0x-prefixed hex.")] string address,
        [Description("Number of instructions to return (1..4096).")] int count,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.DisasmGetAsync(address, count, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "dump.open")]
    [Description("Load a crash dump (.dmp / minidump / full dump) into Visual Studio and start a dump debug session. After this call, the standard threads.*/stack.*/frame.*/eval.*/modules.* tools operate on the dump.")]
    public async Task<DumpOpenResult> DumpOpen(
        [Description("Absolute path to the dump file.")] string path,
        [Description("Optional extra symbol search paths (semicolon-separated). Reserved; VS's configured SymbolPath is used in v1.")] string? symbolPath = null,
        [Description("Optional extra source search paths. Reserved in v1.")] string? sourcePath = null,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.DumpOpenAsync(new DumpOpenOptions { Path = path, SymbolPath = symbolPath, SourcePath = sourcePath }, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "dump.summary")]
    [Description("Summarize the current dump/debug session: faulting thread, exception text, process id, and the loaded-module count split by managed/native. Requires an active debug session (dump or live).")]
    public async Task<DumpSummaryResult> DumpSummary(CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.DumpSummaryAsync(ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "dump.dbgeng")]
    [Description("Run a DbgEng command (e.g. '!analyze -v', 'k', 'lm', '~*k') against a dump file by invoking cdb.exe with -z. Returns the captured output. Gated: requires allowDbgEng=true in %LOCALAPPDATA%\\VSMCP\\config.json. cdb.exe is auto-discovered under the Windows Kits install; override via cdbPath.")]
    public async Task<DumpDbgEngResult> DumpDbgEng(
        [Description("Absolute path to the dump file.")] string dumpPath,
        [Description("DbgEng command to run (semicolon-separated is OK; do not append 'q' — the wrapper does that).")] string command,
        [Description("Optional extra symbol search path (semicolon-separated); overrides _NT_SYMBOL_PATH for this call.")] string? symbolPath = null,
        [Description("Timeout in milliseconds (5000..600000, default 120000).")] int? timeoutMs = null,
        [Description("Override path to cdb.exe. Default: auto-discover under Windows Kits.")] string? cdbPath = null,
        CancellationToken ct = default)
    {
        if (!_config.AllowDbgEng)
            throw new InvalidOperationException("dump.dbgeng is disabled. Set \"allowDbgEng\": true in %LOCALAPPDATA%\\VSMCP\\config.json to enable.");
        return await DbgEngInvoker.RunAsync(new DumpDbgEngOptions
        {
            DumpPath = dumpPath,
            Command = command,
            SymbolPath = symbolPath,
            TimeoutMs = timeoutMs,
            CdbPath = cdbPath,
        }, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "dump.save")]
    [Description("Capture a memory dump of a running process (not necessarily the debuggee) using dbghelp!MiniDumpWriteDump. Writes to the given path; parent directory must exist and Visual Studio must have rights to read the target process.")]
    public async Task<DumpSaveResult> DumpSave(
        [Description("Target process id.")] int pid,
        [Description("Absolute destination path for the dump file.")] string path,
        [Description("When true (default), write a full-memory dump. False writes a smaller minidump (stacks + modules + handles + thread info).")] bool full = true,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.DumpSaveAsync(new DumpSaveOptions { Pid = pid, Path = path, Full = full }, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "processes.list")]
    [Description("Enumerate local processes visible to devenv.exe. Useful for picking a PID before debug.attach, dump.save, or counters.get. By default restricts to devenv's Windows session.")]
    public async Task<ProcessListResult> ProcessesList(
        [Description("Case-insensitive substring filter on process name. Null = no filter.")] string? nameContains = null,
        [Description("When true (default), only processes in the same Windows session as Visual Studio are returned.")] bool currentSessionOnly = true,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.ProcessesListAsync(new ProcessListFilter { NameContains = nameContains, CurrentSessionOnly = currentSessionOnly }, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "counters.get")]
    [Description("One-shot snapshot of process-level counters: CPU% (sampled across `sampleMs`), working set, private/virtual memory, thread/handle counts, and uptime. Uses System.Diagnostics.Process — no profiler attachment required. Poll this from the MCP client to build a timeline.")]
    public async Task<CountersSnapshot> CountersGet(
        [Description("Target process id.")] int pid,
        [Description("Sampling window in milliseconds for CPU% (clamped to 50..10000). Default 200ms.")] int sampleMs = 200,
        CancellationToken ct = default)
    {
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.CountersGetAsync(pid, sampleMs, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "profiler.start")]
    [Description("Start an EventPipe profiling session against a .NET 5+ process. Returns a session id and an output path; the .nettrace is streamed to that file until profiler.stop is called. Does not require Visual Studio to be running or attached. Modes: CpuSampling (default) captures CPU samples + JIT map; Allocations captures sampled object allocations + GC.")]
    public Task<ProfilerStartResult> ProfilerStart(
        [Description("Target process id. Must be a running .NET 5+ process reachable via its diagnostic port.")] int pid,
        [Description("Profiler mode. 0 = CpuSampling (default), 1 = Allocations.")] ProfilerMode mode = ProfilerMode.CpuSampling,
        [Description("Optional absolute output path for the .nettrace. Default: %TEMP%\\vsmcp-<ts>-<pid>-<mode>.nettrace.")] string? outputPath = null,
        CancellationToken ct = default)
    {
        return Task.FromResult(_profiler.Start(pid, mode, outputPath));
    }

    [McpServerTool(Name = "profiler.stop")]
    [Description("Stop a running EventPipe session by id. Flushes the .nettrace to disk and returns the final file size and wall-clock duration.")]
    public async Task<ProfilerStopResult> ProfilerStop(
        [Description("Session id from profiler.start.")] string sessionId,
        CancellationToken ct = default)
    {
        return await _profiler.StopAsync(sessionId, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "profiler.report")]
    [Description("Summarize a .nettrace file: total sample count, session duration, and the top N hot functions by self-time (leaf-of-stack) sample count. Uses Microsoft.Diagnostics.Tracing.TraceEvent; symbols come from whatever is on disk or in the trace's JIT rundown.")]
    public Task<ProfilerReport> ProfilerReport(
        [Description("Absolute path to a .nettrace file (from profiler.stop).")] string path,
        [Description("Max number of hot functions to return (1..1000, default 20).")] int top = 20,
        CancellationToken ct = default)
    {
        return Task.FromResult(_profiler.Report(path, top));
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
}
