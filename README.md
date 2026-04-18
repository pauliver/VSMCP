# VSMCP

**Give your AI assistant the full power of Visual Studio 2022 Enterprise — debugger, profiler, crash-dump analyzer, and all.**

VSMCP is a [Model Context Protocol](https://modelcontextprotocol.io) server that drives a live Visual Studio 2022 instance. An LLM client (Claude Code, Claude Desktop, any MCP-capable agent) can open solutions, build, attach to processes, set breakpoints, step, inspect locals, crack crash dumps, and run the VS profiler — all through structured tool calls.

> **Status:** pre-alpha. Design only. See [`DesignDoc.md`](./DesignDoc.md) and the [issue tracker](https://github.com/pauliver/VSMCP/issues) for the roadmap.

---

## Why

LLMs write code well. They *don't* debug well, because most of the hard tooling — the debugger, the profiler, the dump analyzer — lives behind GUIs. VSMCP exposes that tooling as MCP tools so an AI can:

- Reproduce a reported bug by building, launching, and attaching to a target.
- Set a conditional breakpoint and inspect locals at the moment a bad value appears.
- Open a customer-provided `.dmp` file, identify the faulting thread, and walk the stack.
- Run a CPU profile on a slow startup path and report the hot functions.
- Do the boring 10% (create project, add file, edit, save) too.

---

## Architecture (one-paragraph)

Three processes: your AI client speaks MCP over stdio to `VSMCP.Server.exe`, which forwards JSON-RPC over a per-user named pipe to `VSMCP.Vsix` running inside `devenv.exe`. The VSIX owns all Visual Studio interop (`IVsDebugger`, `IDebugEngine2`, `EnvDTE`, etc.) and marshals every call to the VS UI thread. Full details in [`DesignDoc.md`](./DesignDoc.md).

---

## Requirements

- Windows 10 / 11
- Visual Studio 2022 Enterprise 17.9 or newer (Professional/Community should work; only Enterprise is targeted for testing)
- .NET 8 runtime (for `VSMCP.Server`)
- Visual Studio 2022 SDK (only for building VSMCP from source)

---

## Install

> Not yet published — packages land with milestone **M10**. Tracking: [#10](https://github.com/pauliver/VSMCP/issues).

Once shipped:

```powershell
# 1. Install the VS extension
vsixinstaller VSMCP.vsix                # or install from the VS Marketplace

# 2. Install the MCP bridge
dotnet tool install -g VSMCP.Server

# 3. Register with Claude Code
claude mcp add vsmcp -- vsmcp-server
```

---

## Quick start

1. Open any solution in Visual Studio 2022.
2. `Tools → VSMCP → Enable` (first run only — it remembers).
3. In Claude Code:
   ```
   > What solution is VS attached to?
   ```
   Claude calls `vs.status` and reports back.
4. Try:
   ```
   > Build the active configuration and summarize any errors.
   > Attach to process "myapp.exe" and set a breakpoint on Foo.Bar.
   > Open C:\dumps\crash.dmp, show me the faulting thread's stack.
   > Profile the startup of the Api project for 15 seconds and report hot functions.
   ```

---

## Configuration

Optional file: `%LOCALAPPDATA%\VSMCP\config.json`

```json
{
  "logLevel": "info",
  "allowSideEffects": false,
  "allowDbgEng": false,
  "defaultTimeoutMs": 30000
}
```

- `allowSideEffects` — gate `eval.expression` and `memory.write`. Off by default.
- `allowDbgEng` — allow the `dump.dbgeng` DbgEng passthrough (`!analyze -v`, etc.). Off by default.

---

## Troubleshooting

| Symptom                                        | Fix                                                                         |
|------------------------------------------------|-----------------------------------------------------------------------------|
| `not-connected` on every tool call             | Is VS 2022 running with VSMCP enabled? Check `Tools → VSMCP → Status`.      |
| Multiple VS instances, wrong one picked        | `vs.list_instances()` then `vs.select(id)`.                                 |
| `upgrade-required` on connect                  | Bridge and VSIX versions diverged — update both.                            |
| Named-pipe permission denied                   | Only the current Windows user can connect. Run VS and Claude as same user.  |
| Dump loads but all frames are `???`            | Set a symbol path in `dump.open({symbolPath})` or your VS Symbol settings.  |

Full logs: `%LOCALAPPDATA%\VSMCP\logs\*.log`.

---

## Skills

VSMCP ships a set of [Claude Skills](https://docs.anthropic.com/en/docs/claude-code/skills) that bundle the raw MCP tools into opinionated workflows. Install them into `~/.claude/skills/` and the agent will pick the right one for the job:

| Skill          | For                                                          |
|----------------|--------------------------------------------------------------|
| `Project`      | Create/edit projects, manage files and folders               |
| `Build`        | Build, diagnose compile/link errors                          |
| `Debug`        | Attach, step, breakpoints, inspect live state                |
| `DebugPerf`    | CPU profiling and hot-path analysis                          |
| `DebugMemory`  | Allocation profiling, GC/leak investigation                  |
| `DebugCrash`   | Crash-dump (`.dmp`) triage and root-cause                    |
| `DebugNative`  | Mixed-mode / unmanaged debugging, memory, registers, disasm  |

See [`DesignDoc.md §10`](./DesignDoc.md) for the playbook format.

## Contributing

See [`DesignDoc.md`](./DesignDoc.md) for architecture, then pick an open milestone issue. The project is organized into ten milestones (M1–M10); each issue has acceptance criteria and a tool checklist.

## License

TBD — likely MIT.
