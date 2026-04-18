---
name: Build
description: Build (or rebuild, clean) the VS solution through VSMCP and diagnose compile/link errors. Use when the user says "build", "compile", "why won't this build", "fix the errors", or asks to clean/rebuild. Not for runtime debugging — use Debug once the binaries exist.
---

# Build playbook

## 1. Pick the scope
- Whole solution (default): `build.start()`.
- Specific projects: `build.start({projectIds:[...]})` — resolve names via `project.list()` first if the user used informal names.
- Rebuild (clean + build): `build.rebuild(...)`. Clean only: `build.clean(...)`.
- Respect any `configuration` / `platform` override the user mentioned ("debug", "release", "x64").

## 2. Wait
`build.wait({buildId, timeoutMs: 600000})`. If `TimedOut`, report progress via `build.status` and ask whether to keep waiting or cancel with `build.cancel`.

## 3. Triage on failure
If `Status=Failed`:
1. `build.errors({buildId})` — primary signal. Group by project/file, pick the first N unique diagnostics.
2. `build.warnings({buildId})` — only if errors empty or user asked.
3. If the errors look like configuration drift rather than real compile failures (missing SDK, bad target framework), `build.output({buildId})` for the raw MSBuild log.

## 4. Faster loop for iterative fixes
For "keep rebuilding until it's green", prefer `code.diagnostics({file?})` between edits — it runs Roslyn without invoking MSBuild, so it catches 90% of C#/VB errors in milliseconds. Fall back to `build.start` when native/SDK/linker errors are suspected or after major project-file edits.

## 5. Hand back
Report: Succeeded/Failed, elapsed, count of errors+warnings, and the top 3 error messages with `file:line`. If you fixed errors via edits (through the Project skill), say which.
