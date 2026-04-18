---
name: Debug
description: Drive a live debug session in Visual Studio through VSMCP — launch or attach, set breakpoints, step, inspect threads/stacks/locals, evaluate expressions. Use when the user wants to reproduce a bug, step through code, stop at a line, check a variable's value at runtime, or understand what a process is doing right now. Not for crash dumps (use DebugCrash), not for performance (use DebugPerf), not for native-only workflows (use DebugNative).
---

# Debug playbook

## 1. Start or attach
- "run / launch the app" → `debug.launch({projectId?, args?, env?, cwd?, noDebug?})`. Omit `projectId` to use the configured startup project.
- "attach to X" → `processes.list({nameContains:"X"})` then `debug.attach({pid})`. Prefer `pid` over `processName` if both are known.
- After either call, poll `debug.state()` until `Debugging=true` and `Mode` settles (Run or Break).

## 2. Set breakpoints before the code runs
`bp.set({kind:"Line", file, line, conditionExpression?, tracepointMessage?})`. For "log this value" use a tracepoint via `bp.set_tracepoint`; don't add `Console.WriteLine` edits. `bp.list()` to confirm.

## 3. Break mode — inspect
When `Mode=Break`:
- `threads.list()` → pick the stopped thread. `threads.switch({threadId})` if needed.
- `stack.get({depth:32})`.
- `frame.locals({expandDepth:1})` on the top frame. Drop to `frame.arguments` if locals look empty.
- Expression values: `eval.expression({options:{expression, frameIndex?, allowSideEffects:false}})`. Flip `allowSideEffects=true` only after the user confirms they want to run code.

## 4. Step
`debug.step_into` (into calls) / `debug.step_over` (treat calls atomic) / `debug.step_out` (run to caller). `debug.run_to_cursor({file, line})` skips the boring middle. `debug.continue` resumes until next break/exception.

## 5. Destructive / unusual ops — confirm first
- `debug.set_next_statement` — can skip constructors; confirm and pass `allowSideEffects:true`.
- `threads.freeze` / `threads.thaw` — useful for race repros; undo before continuing.

## 6. Finish
- "stop" → `debug.stop`. "leave it running" → `debug.detach`.
- Report: what broke where, the value that was wrong, and which source span you'd change.
