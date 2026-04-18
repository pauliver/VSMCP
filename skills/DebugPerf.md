---
name: DebugPerf
description: Profile CPU usage of a running .NET process through VSMCP and identify hot functions. Use when the user says "slow", "where is the time going", "profile", "find the hot path", or provides a .nettrace to analyze. Not for memory/allocations (use DebugMemory), not for crashes (use DebugCrash).
---

# DebugPerf playbook

## 1. Identify the target
If the user already has a PID, use it. Otherwise: `processes.list({nameContains})` (scoped to the current Windows session by default). Confirm a match before starting a session.

## 2. Baseline
Optional one-liner before profiling: `counters.get({pid, sampleMs:500})` to record CPU% / working set now — useful for "was it even busy?" calibration.

## 3. Start the session
`profiler.start({pid, mode:"CpuSampling"})`. Returns a `sessionId` and an `outputPath` (a `.nettrace` being streamed to disk). Target must be .NET 5+ with an open diagnostic port.

## 4. Drive the workload
Tell the user to reproduce the slow behavior now. Wait the minimum time to capture the hot path — ~10s for CPU-bound loops, ~30s for cold-start measurements. Don't poll the trace during capture.

## 5. Stop and report
- `profiler.stop({sessionId})` → captures `.nettrace` size and duration.
- `profiler.report({path:outputPath, top:20})` → leaf-of-stack sample counts by function, with `PercentOfSamples`.
- Report: duration, total samples, top 5–10 hot functions with percent and module. If `Empty=true`, the trace failed to resolve — check target was still running when `stop` fired and symbols are available.

## 6. When one session isn't enough
- Symbols missing / obfuscated frames → ensure the target has PDBs on disk and re-run.
- Work happens in a child process → profile that PID instead.
- Long tail suggests many small callers → ask to drill by calling `profiler.report` with a larger `top` (capped 1000), then group by module.

## 7. Hand back
Root cause in 3 sentences max: the hot function, why it's hot (algorithmic / repeated allocation / sync I/O on hot path), and a concrete source location to change. Do not propose edits without the user asking — switch to Build/Project skills when they do.
