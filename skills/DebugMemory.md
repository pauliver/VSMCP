---
name: DebugMemory
description: Investigate memory behavior in a running .NET process through VSMCP — leaks, high working set, GC pressure, allocation hot paths. Use when the user says "leak", "OOM", "high RAM", "GC pressure", "allocating too much", or asks what's allocating. Not for CPU time (use DebugPerf), not for post-mortem (use DebugCrash).
---

# DebugMemory playbook

## 1. Frame the question
- "Is it growing?" → longitudinal counters (§2).
- "What's allocating?" → allocation profile (§3).
- "What's still alive?" → inspection via live debug (§4).

## 2. Longitudinal counters
`counters.get({pid, sampleMs:500})` at t0, then again at t=30s, 60s. Compare `WorkingSetBytes`, `PrivateMemoryBytes`. A steady climb is the leak signal; a flat-but-large value is usually just warm caches.

## 3. Allocation profile
`profiler.start({pid, mode:"Allocations"})` → drive the suspect workload → `profiler.stop` → `profiler.report({path, top:30})`. The hot "functions" in an allocation trace are the *allocating* frames, not CPU hot paths. Look for: high-frequency small allocs on a tight loop, surprising `string.Concat` / boxing, or large arrays created per request.

## 4. Live inspection when you need object identity
Only if steps 2–3 don't localize it:
1. Attach: `debug.attach({pid})`.
2. `debug.break_all`.
3. `eval.expression({options:{expression:"System.GC.GetTotalMemory(false)"}})` — with `allowSideEffects:false` unless the user accepts that `GC.Collect`-style probes run code.
4. Ask the user to point at the suspect type; use `eval` to count instances (`AppDomain`-style queries require side-effects).
5. `debug.detach` when done — don't `stop`, we don't want to kill the process.

## 5. Hand back
Say one of: (a) not actually a leak — memory is bounded and settles; (b) leak localized to allocator `X` at `file:line` in type `Y`; (c) inconclusive — request a longer reproduction or a .dmp for offline inspection (switch to DebugCrash).
