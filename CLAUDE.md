# VSMCP — Claude Code Context

## What this is

VSMCP is a Visual Studio 2022 MCP (Model Context Protocol) server. It consists of:

- **`VSMCP.Vsix`** — A VS extension (VSIX) that runs in-process with Visual Studio and exposes build, debug, file, code-intelligence, and refactoring operations over a named pipe.
- **`VSMCP.Server`** — A .NET process that acts as the MCP server. It connects to the VSIX via named pipe (`\\.\pipe\VSMCP.<pid>`) and exposes tools to AI clients (Claude, etc.).
- **`VSMCP.Shared`** — DTOs and RPC interface shared between Server and Vsix.

## Platform constraint

**This project requires Windows + Visual Studio 2022.** The VSIX cannot be built or tested on macOS. Mac sessions should be used for planning, issue filing, and code authoring only — a Windows agent (or developer) must build and validate.

## Current status

M1–M20 substantially shipped. File layout was refactored from milestone-named files (`M*Dtos.cs`, `RpcTarget.FilesExtensions.cs`) to topical names on 2026-05-05 — see git log around `72f7dd6`. Open work is whatever's in GitHub issues; check there for the live list.

## Key files

| File | Purpose |
|---|---|
| `src/VSMCP.Shared/IVsmcpRpc.cs` | Full RPC contract — every method the Server can call on the VSIX |
| `src/VSMCP.Shared/*Dtos.cs` | DTOs grouped by topic: `FileDiscovery`, `Search`, `Edit`, `Cpp`, `Semantic`, `ActiveEditor`, `Build`, `Debug`, `Inspection`, etc. |
| `src/VSMCP.Vsix/RpcTarget.cs` | Main partial-class declaration; topical partials live in `RpcTarget.<Topic>.cs` |
| `src/VSMCP.Vsix/RpcTarget.FileDiscovery.cs` | File listing/glob/classes/members/dependencies (Roslyn + DTE) |
| `src/VSMCP.Vsix/RpcTarget.Code.cs` | Roslyn code intelligence (goto def, find refs, diagnostics, quickinfo) |
| `src/VSMCP.Vsix/RpcTarget.FileMove.cs` | Bulk file rename + .csproj `<Compile Include>` sync |
| `src/VSMCP.Server/VsmcpTools.cs` | MCP tool surface — add `[McpServerTool]` methods here (split into `VsmcpTools.<Topic>.cs` partials) |
| `src/VSMCP.RearchTool/Program.cs` | Direct-pipe IVsmcpRpc client for ops that bypass the MCP layer (rearch, bulk maintenance) |
| `src/VSMCP.Shared/ErrorCodes.cs` | Error code constants |
| `src/VSMCP.Shared/ProtocolVersion.cs` | Bump Minor as new RPC methods ship |
| `.claude/plans/` | Design specs for major arcs |

## Roslyn utilities (reuse these, don't duplicate)

Mostly in `RpcTarget.FileDiscovery.cs` and `RpcTarget.Code.cs`:

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
