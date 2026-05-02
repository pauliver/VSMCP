# VSMCP — Claude Code Context

## What this is

VSMCP is a Visual Studio 2022 MCP (Model Context Protocol) server. It consists of:

- **`VSMCP.Vsix`** — A VS extension (VSIX) that runs in-process with Visual Studio and exposes build, debug, file, code-intelligence, and refactoring operations over a named pipe.
- **`VSMCP.Server`** — A .NET process that acts as the MCP server. It connects to the VSIX via named pipe (`\\.\pipe\VSMCP.<pid>`) and exposes tools to AI clients (Claude, etc.).
- **`VSMCP.Shared`** — DTOs and RPC interface shared between Server and Vsix.

## Platform constraint

**This project requires Windows + Visual Studio 2022.** The VSIX cannot be built or tested on macOS. Mac sessions should be used for planning, issue filing, and code authoring only — a Windows agent (or developer) must build and validate.

## Current status (as of 2026-05-02)

| Milestone | Status |
|---|---|
| M1–M11 | Complete — build, debug, files, breakpoints, inspection, modules, dump, diagnostics, code intelligence |
| M12 | VSIX done; **#51 compile bug must be fixed first**; MCP tools not yet wired (#54) |
| M13–M17 | DTOs + interface declared; no implementations yet (#55–#59) |
| M18 (Semantic Layer) | Planned — see `.claude/plans/M18-semantic-layer.md`; issues #62–#69 filed |
| CLI | Planned — issue #70; single `vsmcp` dotnet tool with auto-discovery |

## Where to start on a fresh Windows session

1. Fix **#51** (compile error: `ContainerName` → `Container` in `RpcTarget.FilesExtensions.cs:305`) — this blocks all builds.
2. Fix **#52** and **#53** (M12 logic bugs).
3. Wire M12 MCP tools (#54) — VSIX implementations exist, just need `VsmcpTools` methods.
4. Then proceed through M13 → M14 → M15 → M18 in priority order.

## Key files

| File | Purpose |
|---|---|
| `src/VSMCP.Shared/IVsmcpRpc.cs` | Full RPC contract — M1–M18 methods declared here |
| `src/VSMCP.Vsix/RpcTarget.FilesExtensions.cs` | M12 VSIX implementations (working, with bugs #51–#53) |
| `src/VSMCP.Vsix/RpcTarget.Stubs.cs` | M13–M17 + C++ stubs (all "to be implemented") |
| `src/VSMCP.Server/VsmcpTools.cs` | MCP tool surface — add `[McpServerTool]` methods here |
| `src/VSMCP.Server/VsmcpTools.Batch.cs` | Batch tool variants |
| `src/VSMCP.Shared/M12Dtos.cs` – `M17Dtos.cs` | DTOs per milestone |
| `src/VSMCP.Shared/ErrorCodes.cs` | Error code constants |
| `src/VSMCP.Shared/ProtocolVersion.cs` | Bump Minor as milestones ship |
| `docs/M12-M16_Expansion_Plan.md` | Design spec for M12–M17 |
| `.claude/plans/M18-semantic-layer.md` | Design spec for M18 semantic layer |

## Roslyn utilities (reuse these, don't duplicate)

All in `RpcTarget.FilesExtensions.cs` and `RpcTarget.Code.cs`:

- `FileMembersAsync(file, className, ...)` — returns `MemberInfo` with `CodeSpan` per member ← **key asset**
- `GetCodeSpan(ISymbol)` — converts Roslyn symbol → 1-based file/line/col span
- `FindDocument(Solution, filePath)` — resolves path to Roslyn `Document`
- `GetWorkspaceAsync()` — entry point to `VisualStudioWorkspace`
- `WalkOutline(node, sm, ...)` — recursive symbol tree walker
- `FileReplaceRangeAsync(file, range, text)` — low-level edit workhorse

## GitHub issues

All open work is tracked at https://github.com/pauliver/VSMCP/issues  
Issues #51–#70 were filed 2026-05-02. Start with #51.

## VSIX packaging gotcha

`ProjectReference` alone does **not** pack dependency DLLs into the `.vsix`. Both `<Private>true</Private>` and `<IncludeInVSIX>true</IncludeInVSIX>` are required. See `docs/DEVELOPMENT_NOTES.md`.

## Git workflow

PRs are squash-merged: `gh pr merge --squash --delete-branch`  
Force-delete stale local branches after merge: `git branch -D <branch>`
