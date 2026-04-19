# VSMCP — Design Document

**Project:** Model Context Protocol server for Microsoft Visual Studio 2022 Enterprise
**Status:** Draft (v0.2) — post-M11
**Last updated:** 2026-04-18

---

## 1. Motivation

LLM assistants are competent at code generation and refactoring. They are *not* competent at:

- Attaching to a running .NET or native process and stepping through it.
- Opening a crash dump, identifying the faulting thread, and reading locals at the crash frame.
- Running a CPU sampling session and reading back a hot-path summary.
- Setting a conditional breakpoint in a build they just produced and re-running to reproduce a bug.

Every one of those operations is already implemented — in Visual Studio. VSMCP's thesis is to expose that implementation over the Model Context Protocol so an AI assistant can *drive* a real VS 2022 Enterprise instance.

Scope split (per project brief): **~10%** solution/project/file CRUD, **~90%** debugging, crash-dump analysis, and performance diagnostics.

---

## 2. Non-goals

- Replacing Visual Studio's UI. The VSIX is a headless-ish server; humans keep using VS normally.
- Running without Visual Studio. VSMCP is a *bridge*, not a reimplementation of the VS debugger.
- Supporting VS Code. (A future sibling project could wrap VS Code's DAP — different architecture.)
- Supporting pre-2022 VS versions. 2022 only.
- Cross-machine remote debugging in v1. Local only.

---

## 3. Architecture

### 3.1 Process topology

```
┌────────────┐  MCP/stdio   ┌──────────────────┐  JSON-RPC    ┌────────────────────┐
│ AI client  │ ───────────► │ VSMCP.Server.exe │ ───────────► │ devenv.exe         │
│ (Claude)   │              │ (.NET 8 console) │  named pipe  │ + VSMCP.Vsix       │
└────────────┘              └──────────────────┘              └────────────────────┘
```

Three assemblies:

| Name            | TFM                 | Process             | Responsibility                               |
|-----------------|---------------------|---------------------|----------------------------------------------|
| `VSMCP.Shared`  | `netstandard2.0`    | both                | DTOs, tool contracts, error codes            |
| `VSMCP.Server`  | `net8.0`            | standalone console  | MCP stdio server; named-pipe client          |
| `VSMCP.Vsix`    | `net472` (VSSDK)    | in `devenv.exe`     | Named-pipe server; VS interop implementation |

### 3.2 Why three processes

- MCP stdio transport requires a console; `devenv.exe` is GUI and cannot expose stdio to the AI client.
- VSSDK interop requires `net472` and STA threading inside `devenv.exe`; MCP C# SDK targets modern .NET.
- Keeping the bridge thin means the extension owns all VS-coupled logic and can be developed/tested with the VSSDK's experimental hive.

### 3.3 Inter-process transport

- **Named pipe:** `\\.\pipe\VSMCP.<vs-process-id>`. One pipe per VS instance.
- **Framing:** `System.IO.Pipelines` + `StreamJsonRpc` (already shipped with VS).
- **Discovery:** `VSMCP.Server` on start looks for `VSMCP.*` pipes; if multiple VS instances are running, the server lists them and the AI picks via `vs.select` (or selects the only one).
- **Security:** pipe ACL restricted to the current Windows user SID. No network exposure.

### 3.4 Threading model inside the VSIX

- The pipe listener runs on a worker thread.
- Every VS API call is marshaled onto the UI thread via `JoinableTaskFactory.SwitchToMainThreadAsync()` (VS threading guidance).
- Long-running operations (build, debug event streams) emit progress/events back over the pipe using JSON-RPC notifications so the bridge can convert them to MCP progress/notifications.

### 3.5 MCP surface

- Transport: stdio (default) and optionally SSE for debugging.
- Tools: see §5 catalog.
- Resources: expose `vsmcp://solution/<guid>/*` for quick reads (solution file, loaded projects, recent output).
- Prompts: a small set of curated prompts ("analyze this crash dump", "profile this startup path") that chain the raw tools.

### 3.6 Teaching mode (auto-focus)

VSMCP is also a teaching tool — users often watch along as an AI drives VS. When teaching mode is enabled (default on), the pipe host activates the VS main window after every dispatched RPC so any observer sees the IDE react in real time. The flag is per-connection on `RpcTarget.AutoFocusEnabled`, seeded from the tool-window's checkbox (`VsmcpGlobalDefaults.AutoFocusDefault`), and can be toggled at runtime via `vs.set_autofocus`.

### 3.7 In-VS UI surface

The VSIX exposes two in-IDE surfaces so users can see live connection state without reading logs:

- **Tool window** — `Tools → VSMCP Panel`. Traffic-light status dot, pipe name with copy button, last-activity age, total RPCs + errors, last error, a 50-entry recent-RPC log, teaching-mode checkbox, and an "Open logs folder" button. Built as a `ToolWindowPane` + code-only WPF `UserControl` (no XAML) so the classic VSIX csproj doesn't need Page/Markup build actions.
- **Status bar** — a `StatusBarReporter` mirrors `HostActivity` onto the VS main status bar: `VSMCP: connected (N) · M RPCs · Ks idle` (or `waiting for client` / `idle`). Reasserts every 2s so other components can't silently stomp on the text.

Both surfaces subscribe to a single `HostActivity` observable fed by `PipeHost` (connect, disconnect, and per-RPC completion with elapsed ms + error).

---

## 4. Interaction with Visual Studio

### 4.1 APIs used

| Capability                | Primary API                                                            |
|---------------------------|------------------------------------------------------------------------|
| Solution / projects       | `IVsSolution`, `IVsSolution4`, `IVsHierarchy`, `EnvDTE80.DTE2`         |
| File / text edits         | `IVsRunningDocumentTable`, `IVsTextManager`, `IWpfTextView`, `ITextBuffer` |
| Build                     | `IVsBuildManagerAccessor`, `SolutionBuild2`, `IVsOutputWindow`         |
| Debug control             | `IVsDebugger`, `IVsDebugger4`, `IDebugEngine2`, `IDebugProgram2`       |
| Stepping / state          | `IDebugThread2`, `IDebugStackFrame2`, `IDebugProperty2`                |
| Breakpoints               | `IDebugBreakpointRequest2`, `EnvDTE.Debugger.Breakpoints`              |
| Dump load / save          | `IVsDebugger4.LaunchDebugTargets4` with `DLO_LoadDumpFile`             |
| Profiler / diagnostics    | `Microsoft.DiagnosticsHub.*`, VSPerf interop                           |
| Symbols / modules         | `IDebugModule2`, `IDebugSymbolProvider`                                |
| Editor semantics          | Roslyn workspace via `VisualStudioWorkspace`                           |

### 4.2 Event stream

The VSIX subscribes to `IVsDebuggerEvents`, `IDebugEventCallback2`, and `_dispBuildEvents` and forwards filtered events to the bridge. The bridge exposes them as MCP server notifications (`vsmcp/event`) with a typed payload:

```json
{ "kind": "debug.stopped", "reason": "breakpoint", "thread": 4120, "frame": {...} }
```

---

## 5. Tool catalog

Mirrors the implementation milestones. Every tool has a typed input and result schema declared in `VSMCP.Shared`.

### 5.1 Solution / project / file (M2)
- `solution.open(path)`, `solution.close()`, `solution.info()`
- `project.list()`, `project.add(kind, path)`, `project.remove(id)`
- `project.properties.get(id, keys[])`, `project.properties.set(id, map)`
- `project.file.add(projectId, path, linkOnly?)`, `project.file.remove(projectId, path)`
- `project.folder.create(projectId, path)`
- `file.read(path, range?)`, `file.write(path, content)`, `file.replace_range(path, range, text)`
- `editor.open(path, line?, column?)`, `editor.save(path)`, `editor.save_all()`

Edits to files that are open in VS flow through `ITextBuffer` so they participate in undo/redo.

### 5.2 Build (M3)
- `build.start({configuration, platform, projects?})` → returns `buildId`
- `build.cancel(buildId)`, `build.status(buildId)`, `build.wait(buildId, timeoutMs?)`
- `build.errors(buildId)`, `build.warnings(buildId)`, `build.output(buildId, pane?)`
- `build.clean(...)`, `build.rebuild(...)`

### 5.3 Debug control (M4)
- `debug.launch({projectId?, args?, env?, cwd?, noDebug?})`
- `debug.attach({pid? | processName?, engines?: ["managed","native","script",...]})`
- `debug.stop()`, `debug.detach()`, `debug.restart()`
- `debug.break_all()`, `debug.continue()`
- `debug.step_into()`, `debug.step_over()`, `debug.step_out()`
- `debug.run_to_cursor({file, line})`
- `debug.set_next_statement({file, line})`
- `debug.state()` → mode, stopped reason, current thread, current frame

### 5.4 Breakpoints (M5)
- `bp.set({file?, line?, function?, address?, condition?, hitCount?, data?})` → `bpId`
- `bp.list()`, `bp.remove(bpId)`, `bp.enable(bpId)`, `bp.disable(bpId)`
- `bp.set_tracepoint({..., message})` — logpoint, no break

### 5.5 Inspection (M6)
- `threads.list()`, `threads.freeze(tid)`, `threads.thaw(tid)`, `threads.switch(tid)`
- `stack.get({tid?, depth?})` → frames
- `frame.switch(frameId)`, `frame.locals(frameId)`, `frame.arguments(frameId)`
- `eval.expression({expression, frameId?, allowSideEffects?})`
- `memory.read({address, length})`, `memory.write({address, bytes})`
- `registers.get({tid?})`, `disasm.get({address, count})`
- `modules.list()`, `symbols.load(moduleId)`, `symbols.status(moduleId)`

### 5.6 Crash-dump analysis (M7)
- `dump.open({path, symbolPath?, sourcePath?})` → session
- `dump.summary()` — exception code, faulting thread, loaded modules, OS build
- `dump.save({pid, path, full?})`
- Inspection tools (threads/stack/locals/eval) work on dump sessions unchanged.
- `dump.dbgeng({command})` — escape hatch to run DbgEng commands (`!analyze -v`, `!heap`, ...) when VS lacks a native equivalent.

### 5.7 Diagnostics & performance (M8)
- `processes.list()` — running processes visible to the debugger, used to drive `debug.attach`
- `counters.get({pid, names[]})` — one-shot read of perf counters
- `counters.subscribe({pid, names[], intervalMs})` → sessionId; server polls counters in the background
- `counters.read(sessionId)` → samples collected since last read
- `counters.unsubscribe(sessionId)`
- `profiler.start({mode: "cpu-sampling"|"instrumentation"|"allocations"|"gpu"|"database", targetPid?})` → sessionId
- `profiler.stop(sessionId)` → diagsession path
- `profiler.report(sessionId | path)` → hot functions, call tree, allocations, summary stats
- `trace.start({providers[], output})` → sessionId
- `trace.stop(sessionId)`
- `trace.report(sessionId | path)` — ETW event summary (counts, top stacks)

### 5.8 Code intelligence (M9)
- `code.symbols({file})` — document outline via Roslyn
- `code.goto_definition({file, position})`
- `code.find_references({file, position})`
- `code.diagnostics({file?})` — live analyzer diagnostics, no build required
- `code.quick_info({file, position})`

### 5.9 Meta
- `ping()`, `vs.status()`, `vs.list_instances()`, `vs.select(instanceId)`
- `vs.focus()` — raise the VS main window to the foreground (also used internally by teaching mode)
- `vs.set_autofocus({enabled})` / `vs.get_autofocus()` — toggle per-connection teaching mode
- `events.subscribe({kinds[]})` / `events.unsubscribe()` *(planned; not yet implemented)*

### 5.10 Batch variants

High-fanout operations have `_many` companions that run the inner op sequentially on the VSIX (VS APIs are UI-thread serialized, so parallelism offers no speedup) and return a `BatchResult<T>` with per-item success/error so a single bad input doesn't fail the whole batch:

- `bp.set_many`, `bp.remove_many`, `bp.enable_many`, `bp.disable_many`
- `eval.expression_many`
- `file.read_many`
- `memory.read_many`
- `symbols.load_many`

---

## 6. Error model

All tool errors map to MCP tool errors with an `VSMCP-<code>` string:

| Code             | Meaning                                                     |
|------------------|-------------------------------------------------------------|
| `not-connected`  | No VS instance attached                                     |
| `not-debugging`  | Tool requires an active debug session                       |
| `wrong-state`    | e.g. `step_over` while running                              |
| `target-busy`    | Build already in progress                                   |
| `not-found`      | File / project / breakpoint / thread / frame id unknown     |
| `timeout`        | Operation exceeded requested or default timeout             |
| `interop-fault`  | Underlying VS API returned `HRESULT` failure — details in msg |
| `unsupported`    | Capability not implemented for the current language/engine  |

---

## 7. Security

- Named pipe ACL = current user SID only.
- No inbound network listener.
- `eval.expression` and `memory.write` are gated by a capability flag (`allowSideEffects`) defaulting to `false`; the AI must request it explicitly.
- `dump.dbgeng` requires `allowDbgEng` capability to be enabled in `%LOCALAPPDATA%\VSMCP\config.json` (off by default).
- No telemetry. Logs are local, opt-in, and written to `%LOCALAPPDATA%\VSMCP\logs\*.log`.

---

## 8. Versioning & packaging

- VSIX published to the Visual Studio Marketplace and as a direct `.vsix` artifact on GitHub Releases.
- `VSMCP.Server` published as a `dotnet tool` (`dotnet tool install -g VSMCP.Server`) and a self-contained zip.
- Version handshake on pipe connect: bridge and VSIX exchange semver; mismatch → `upgrade-required` error with a readable remediation message.

---

## 9. Testing strategy

- `VSMCP.Shared` → unit tests (xUnit).
- `VSMCP.Server` → integration tests using a fake pipe server implementing the contracts.
- `VSMCP.Vsix` → VS SDK integration tests in the experimental hive, plus end-to-end scenarios against a sample solution under `tests/Fixtures/`:
  - `HelloCrash` — throws `NullReferenceException`; used for attach/step/dump tests.
  - `HotLoop` — CPU-bound, used for profiler tests.
  - `NativeConsole` — C++ executable; used for native-debug and minidump tests.

CI: GitHub Actions on `windows-latest` with VS 2022 Build Tools and the VS SDK.

---

## 10. Skills (higher-level workflows for the AI)

The raw MCP tool catalog (§5) is deliberately low-level — one op per call. Skills bundle those ops into opinionated, reusable *workflows* shipped as Claude Skills (Markdown files with a `description` frontmatter). An agent loads a skill when its description matches the user's intent, and the skill prescribes *how* to sequence the underlying VSMCP tools for that class of problem.

Skills ship in `skills/` at the repo root and are installable for Claude Code (`~/.claude/skills/`) and Claude Desktop.

### 10.1 Skill set (M11 — shipped)

| Skill            | When to trigger                                               | Tools composed                                                                 |
|------------------|---------------------------------------------------------------|--------------------------------------------------------------------------------|
| **Project**      | "create a project", "add a file to", "edit the .csproj"       | `solution.*`, `project.*`, `file.*`, `editor.*`                                |
| **Build**        | "build", "fix compile errors", "why won't this link"          | `build.start/status/wait`, `build.errors/warnings/output`, `code.diagnostics`  |
| **Debug**        | "step through", "attach to", "set a breakpoint", "reproduce"  | `debug.*`, `bp.*`, `threads.*`, `stack.*`, `frame.*`, `eval.expression`        |
| **DebugPerf**    | "it's slow", "profile", "find the hot path"                   | `profiler.start/stop/report`, `counters.subscribe`, `trace.collect`            |
| **DebugMemory**  | "leak", "high RAM", "GC pressure", "allocations"              | `profiler.start(mode=allocations)`, `counters.subscribe(gc/...)`, `eval`       |
| **DebugCrash**   | "analyze this dump", "why did it crash", ".dmp"               | `dump.open/summary`, `threads.*`, `stack.*`, `modules.*`, `dump.dbgeng`        |
| **DebugNative**  | "unmanaged", "C++ side", "access violation", "mixed-mode"     | `debug.attach({engines:["native"]})`, `memory.*`, `registers.get`, `disasm.*` |

### 10.2 Skill file shape

Each skill is a Markdown file with frontmatter and a playbook body:

```markdown
---
name: DebugCrash
description: Analyze a Windows crash dump (.dmp) produced by a .NET or native process. Use when the user provides a dump path, asks "why did it crash", or mentions access violation / unhandled exception in post-mortem.
---

# DebugCrash playbook

## 1. Open the dump
Call `dump.open({path, symbolPath?})`. If symbols unresolved, ask the user for a symbol path
or PDB location before continuing.

## 2. Summarize
Call `dump.summary()`. Report: exception code, faulting module, OS build, managed vs native.

## 3. Walk the faulting thread
`threads.list()` → pick the one in `dump.summary().faultingTid`. `stack.get({tid})`.
For each of the top 8 frames, `frame.locals(frameId)`; stop early if you find the obvious
culprit.

## 4. If native and VS can't resolve it
Fallback to `dump.dbgeng({command: "!analyze -v"})` (requires `allowDbgEng`).

## 5. Hand back
Summarize the root cause in 3 sentences max; link file/line for each culpable frame.
```

### 10.3 Skill design rules

- **One job per skill.** If the playbook branches between "live debugging" and "dump analysis" at step 1, it's two skills.
- **No tool invention.** Skills may only call tools listed in §5. If a workflow needs a tool we don't have, that's a gap — file a tool issue first, then build the skill.
- **Read-only by default.** Any skill step that calls a side-effecting tool (`memory.write`, `eval.expression` with side effects, `debug.set_next_statement`) must explicitly confirm with the user before firing.
- **Be terse in playbooks.** Skills are prompts consumed by an LLM — every paragraph costs tokens at invocation time.
- **Testable.** Each skill ships with a fixture-based e2e test under `tests/Skills/` that runs the playbook against `HelloCrash`, `HotLoop`, or `NativeConsole`.

### 10.4 Relationship to tools

```
 AI intent → Skill (markdown playbook) → VSMCP tools (MCP) → VSIX (VS interop)
```

Skills are *optional*. A sufficiently capable agent can drive VSMCP tools directly. Skills are the in-box recipes for common jobs so users don't have to reinvent "how do I debug a crash dump" each session.

---

## 11. Open questions

1. Should the VSIX auto-start the pipe on VS launch, or require user opt-in via a menu command? (Leaning: opt-in first run, remembered after.)
2. How to surface LiveShare/remote attach scenarios — punt to v2?
3. Profiler report format: stick with VS `.diagsession` only, or also emit a portable JSON summary? (Leaning: both.)
4. Roslyn `VisualStudioWorkspace` vs spinning our own `MSBuildWorkspace` — which gives the AI closer fidelity to what the user sees in VS? (Leaning: `VisualStudioWorkspace` via MEF inside the VSIX.)

These will be resolved in the relevant milestone issues.
