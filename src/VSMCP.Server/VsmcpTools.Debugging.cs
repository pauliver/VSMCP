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
/// Debug control, breakpoints, inspection (threads/stack/frame/eval), modules/symbols,
/// memory/registers/disasm, crash dumps, process enumeration, one-shot counters, and profiler
/// tools — split out of the VsmcpTools base for topical organization (#128).
/// </summary>
public sealed partial class VsmcpTools
{
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
        allowSideEffects = SideEffectPolicy.Enforce(allowSideEffects, _config.AllowSideEffects, "debug.set_next_statement");
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
        if (options is not null)
            options.AllowSideEffects = SideEffectPolicy.Enforce(options.AllowSideEffects, _config.AllowSideEffects, "eval.expression");
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.EvalExpressionAsync(options, ct).ConfigureAwait(false);
    }

    [McpServerTool(Name = "debug.set_variable")]
    [Description("Set a variable/expression in the current debug frame to a new value (evaluates 'name = value' with side effects). Requires break mode. Gated by the global allowSideEffects config like other side-effecting ops. Returns the resulting value/type.")]
    public async Task<SetVariableResult> DebugSetVariable(
        [Description("Variable or assignable expression, e.g. 'count' or 'this->size'.")] string name,
        [Description("New value expression, e.g. '42' or '\"hello\"'.")] string value,
        CancellationToken ct = default)
    {
        SideEffectPolicy.Enforce(true, _config.AllowSideEffects, "debug.set_variable");
        var proxy = await _connection.GetOrConnectAsync(ct).ConfigureAwait(false);
        return await proxy.DebugSetVariableAsync(name, value, ct).ConfigureAwait(false);
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
        allowSideEffects = SideEffectPolicy.Enforce(allowSideEffects, _config.AllowSideEffects, "memory.write");
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
}
