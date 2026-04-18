---
name: DebugCrash
description: Analyze a Windows crash dump (.dmp / minidump / full dump) of a .NET or native process through VSMCP. Use when the user provides a dump path, says "why did it crash", mentions "access violation" / "unhandled exception" in post-mortem, or asks to capture a dump of a running process. Not for live debugging (use Debug), not for profiling (use DebugPerf).
---

# DebugCrash playbook

## 1. Get the dump
- User has a `.dmp` → go to §2.
- User wants to capture from a live PID → `dump.save({pid, path, full:true})`. `full:true` for root-cause analysis; `full:false` if disk space / privacy matters.

## 2. Open it
`dump.open({path})`. VS enters a dump debug session; the rest of this playbook uses the normal debug tools against that session.

## 3. Summarize
`dump.summary()`. Expected fields: `FaultingThreadId`, `ExceptionMessage`, process id/name, module counts (managed vs native). If `FaultingThreadId` is null and there's no exception, it's likely a hang/cancel dump — report that and ask for a full dump captured during the actual fault.

## 4. Walk the faulting thread
1. `threads.switch({threadId: faultingTid})`.
2. `stack.get({depth:32})`.
3. For the top 3–8 frames: `frame.locals({frameIndex, expandDepth:1})`. Stop early if an obvious bad value shows up (null, `-1`, wrong handle, corrupted pointer).
4. If frames show `???` or wrong offsets, call `modules.list` → `symbols.status` on the faulting module. Ask the user for a symbol path if stripped.

## 5. Native / unresolved
If the faulting frame is native and stack doesn't resolve:
- Try `dump.dbgeng({command:"!analyze -v"})` — requires `allowDbgEng:true` in `%LOCALAPPDATA%\VSMCP\config.json`. If disabled, ask the user to enable it before continuing.
- Fallback: switch to DebugNative for disasm / memory / register inspection of the frame.

## 6. Managed exceptions
If `ExceptionMessage` is non-empty, don't stop at it — that's the symptom. Walk back 3–5 frames to find the caller that passed bad state in.

## 7. Hand back
Three sentences max: what threw, where the bad state came from (`file:line`), and the smallest code change that would prevent it. Propose the edit only if asked (then switch to Project/Build).
