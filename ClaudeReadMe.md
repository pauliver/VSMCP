# VSMCP — For Claude

This file is aimed at Claude (or any MCP-capable agent) that's about to connect
to VSMCP for the first time, and at whoever needs to build/extend it. For
human-facing overview see [`README.md`](./README.md); for architecture see
[`DesignDoc.md`](./DesignDoc.md); for known traps see
[`docs/DEVELOPMENT_NOTES.md`](./docs/DEVELOPMENT_NOTES.md).

---

## 1. How to use

### Prerequisites

- Visual Studio 2022 (Enterprise tested; Professional/Community should work) with the VSMCP VSIX installed and an instance running.
- .NET 9 runtime (the bridge targets `net9.0`).
- One running VS instance. Multi-instance is supported but the default connection picks "the only one" — if you have two open, use `vs.list_instances` / `vs.select`.

### Registering VSMCP with Claude Code

VSMCP isn't published as a package yet, so point Claude Code at a source build
of the bridge:

```bash
# from the repo root
dotnet build -c Release src/VSMCP.Server/VSMCP.Server.csproj

# register — path is the built DLL, not the source
claude mcp add vsmcp -- dotnet P:/VSMCP/src/VSMCP.Server/bin/Release/net9.0/vsmcp-server.dll
```

Once `dotnet tool install -g VSMCP.Server` ships (M10, issue [#10](https://github.com/pauliver/VSMCP/issues/10)) the command simplifies to:

```bash
claude mcp add vsmcp -- vsmcp-server
```

### Verifying the connection

From any Claude conversation with the `vsmcp` server attached:

```
> What's the status of my Visual Studio instance?
```

Claude invokes `vs.status`. You should see the solution name, active
configuration, debug mode, and pipe name. The VSMCP tool window in VS (View →
Other Windows → VSMCP) should flip to "Connected (1 client)" and the status bar
should show `VSMCP: connected (1) · N RPCs`.

If you get `not-connected`: the VSIX isn't loaded, or VS isn't running, or the
named pipe isn't reachable. See `docs/DEVELOPMENT_NOTES.md` and
[`README.md`](./README.md#troubleshooting).

### Using the skills

The `skills/` directory contains opinionated playbooks for common jobs. Drop
them into `~/.claude/skills/` (or the equivalent for your client) and Claude
will pick the right one for the request:

| Skill         | When Claude should invoke it                                                                                          |
|---------------|-----------------------------------------------------------------------------------------------------------------------|
| `Project`     | User asks to create/edit a VS project, add files, change references                                                   |
| `Build`       | "Build the solution", "why isn't this compiling", any compile/link error triage                                       |
| `Debug`       | Attach, step, set breakpoints, inspect locals during a live debug session                                             |
| `DebugPerf`   | "It's slow" — CPU sampling, hot-path analysis                                                                         |
| `DebugMemory` | Allocation-rate or heap-growth questions; suspected leaks                                                             |
| `DebugCrash`  | User provides a `.dmp` file, or asks to capture one from a hung/crashed process                                       |
| `DebugNative` | Mixed-mode / unmanaged code, memory, registers, disassembly                                                           |

Each playbook enumerates the tools it uses and the order of operations — read
the skill's own frontmatter rather than guessing. See
[`DesignDoc.md §10`](./DesignDoc.md) for the playbook format.

### Tool surface at a glance

All tools are documented in [`DesignDoc.md §5`](./DesignDoc.md). Highlights:

- `vs.status`, `vs.list_instances`, `vs.select`, `vs.focus` — meta / instance control
- `solution.*`, `project.*`, `file.*`, `editor.*` — the 10% editing surface
- `build.*` — start/cancel/wait, errors, warnings, output window
- `debug.launch|attach|stop|continue|step_*|run_to_cursor|state` — debugger control
- `bp.set|remove|list|enable|disable|set_tracepoint` — breakpoints
- `threads.*`, `stack.*`, `frame.locals|arguments`, `eval.expression` — inspection
- `memory.read|write`, `registers.get`, `disasm.get`, `modules.list`, `symbols.*` — native debug
- `dump.open|summary|threads|stack|locals|save|dbgeng` — post-mortem analysis
- `profiler.start|stop|report`, `counters.*`, `trace.*`, `processes.list` — diagnostics
- `code.symbols|goto_definition|find_references|diagnostics|quick_info` — Roslyn assist

Side-effecting operations (`eval.expression`, `memory.write`, `dump.dbgeng`
passthrough) are gated behind config flags in
`%LOCALAPPDATA%\VSMCP\config.json`. If a tool returns `side-effects-disabled`,
that's why.

### Expectations about latency and state

- Every tool call marshals to the VS UI thread. Expect 10–200ms overhead per
  call. Batch variants (`bp.set_many`, `file.read_many`, etc.) exist for
  high-volume work.
- VS debugger state is authoritative. If a breakpoint is hit and you don't call
  `debug.continue`, VS stays stopped — don't assume the process resumed on its
  own.
- `debug.state.mode` transitions are asynchronous. After `debug.launch`, poll
  `debug.state` until `Mode == Break` or `Run` before assuming the target is
  ready.

---

## 2. How to build and extend

### Build from source

```bash
git clone https://github.com/pauliver/VSMCP.git
cd VSMCP

# VSIX (classic csproj, needs MSBuild with the VS SDK installed)
MSYS_NO_PATHCONV=1 MSBuild.exe src/VSMCP.Vsix/VSMCP.Vsix.csproj -p:Configuration=Release

# Bridge (SDK-style, .NET 9)
dotnet build -c Release src/VSMCP.Server/VSMCP.Server.csproj
```

Outputs:
- `src/VSMCP.Vsix/bin/Release/VSMCP.Vsix.vsix` — install via
  `VSIXInstaller.exe` (VS must be closed).
- `src/VSMCP.Server/bin/Release/net9.0/vsmcp-server.dll` — run with
  `dotnet vsmcp-server.dll`.

On Windows + Git Bash, `MSYS_NO_PATHCONV=1` is required when invoking native
Windows executables — without it, flags like `/p:Configuration=Release` get
rewritten as POSIX paths. Always use MSBuild's `-p:` form.

### Project layout

```
src/
  VSMCP.Vsix/        classic csproj, net472, runs inside devenv.exe
  VSMCP.Server/      sdk-style, net9.0, MCP stdio ↔ named-pipe bridge
  VSMCP.Shared/      netstandard2.0, DTOs + interface contracts
tests/
  Skills.E2E/        opt-in e2e suite (VSMCP_E2E=1), hits a live VS instance
  Skills/            fixture solutions (HelloCrash, HotLoop, etc.)
skills/              Claude skill playbooks
docs/DEVELOPMENT_NOTES.md  hard-won traps
DesignDoc.md         architecture and full tool catalog (authoritative)
```

The three processes communicate as:

```
AI client  ──stdio/MCP──▶  VSMCP.Server  ──named-pipe/JSON-RPC──▶  VSMCP.Vsix (in devenv.exe)
```

Pipe name: `VSMCP.<devenv-pid>`. One pipe per VS instance, ACL'd to the current
user. `VsConnection` in the bridge discovers pipes by enumerating.

### Adding a new tool

End-to-end, a new tool touches four files:

1. **`src/VSMCP.Shared/IVsmcpRpc.cs`** (or the appropriate partial) — add the RPC
   method signature and any request/response DTOs. This is the wire contract.
2. **`src/VSMCP.Vsix/RpcTarget.<Area>.cs`** — implement the VS-side handler.
   Marshal to the UI thread with `_jtf.SwitchToMainThreadAsync()` before
   touching `DTE`, `IVsDebugger`, any `IVs*` service, or any live text buffer.
3. **`src/VSMCP.Server/VsmcpTools.cs`** — add an `[McpServerTool]`-decorated
   method that calls through `_connection.GetOrConnectAsync()` + the RPC
   method. Keep this layer thin — prefer pushing logic to the VSIX where the
   SDK actually works.
4. **`DesignDoc.md §5`** — document the tool in the catalog.

Optionally:
- Add an entry to the relevant skill in `skills/` if the new tool fits an
  existing playbook.
- Add an e2e test in `tests/Skills.E2E/` if the behavior is non-trivial to
  verify by inspection.

### Testing

```bash
# Fast unit-ish suite (no VS required) — runs on CI
dotnet test

# Opt-in e2e suite — requires a running VS 2022 with VSMCP VSIX loaded
VSMCP_E2E=1 dotnet test tests/Skills.E2E/Skills.E2E.csproj
```

The e2e collection is serialized (`DisableParallelization = true`) because VS
is COM-STA and there's only one debugger session. Tests that need specific
fixture solutions open (`HelloCrash.sln`, `HotLoop`, etc.) skip cleanly when
their fixture isn't loaded — open the solution in VS before running those
tests.

### VSIX install/uninstall during development

Never `rm -rf` the installed extension folder at
`%LOCALAPPDATA%\Microsoft\VisualStudio\17.0_<hive>\Extensions\<hash>\` — VS
caches the folder hash in `ExtensionMetadataCache.sqlite` and will fail to
load on next startup with `FileNotFoundException`. Always uninstall through
the VS Extensions UI or re-run `VSIXInstaller.exe` with the new `.vsix` to
let it manage the replacement.

VSIXInstaller flags (`/quiet`, etc.) are mostly unsupported — run the GUI
installer and click through.

### Contributing via PR

Repo follows squash-merge:

```bash
git checkout -b feat/my-change
# edit, commit, push
gh pr create --title "..." --body "..."
# after review
gh pr merge --squash --delete-branch
```

PR body uses the `## Summary` + `## Test plan` template. After squash, local
feature branches need `git branch -D` to delete (squash severs the parent link
that `-d` relies on). See [`docs/DEVELOPMENT_NOTES.md`](./docs/DEVELOPMENT_NOTES.md)
for more.

### Where things are published (or aren't, yet)

- **VSIX**: not on the VS Marketplace yet; build from source. Tracked by
  milestone M10 / issue [#10](https://github.com/pauliver/VSMCP/issues/10).
- **`VSMCP.Server`**: not on NuGet as a `dotnet tool` yet; build from source.
  Same milestone.
- **GitHub Releases**: none published yet. No CI-built artifacts.
- **Code signing**: blocked on certificate procurement, issue
  [#34](https://github.com/pauliver/VSMCP/issues/34).

Contributors don't need to worry about signing or release cadence until those
issues close.
