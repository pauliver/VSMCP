# VSMCP skill fixtures

Tiny projects that give each Claude Skill playbook a deterministic target. Not part of the product; not wired into `VSMCP.sln`. Build them only when you want to exercise the skills end-to-end against a known repro.

| Fixture | Language | Binds to skill | Repro |
| --- | --- | --- | --- |
| `HelloCrash/` | .NET 9 | `Debug`, `DebugCrash` | Null deref / out-of-range / stack overflow (`--` `nre` \| `aoor` \| `stack`) |
| `HotLoop/` | .NET 9 | `DebugPerf`, `DebugMemory` | CPU burn / alloc churn / mixed (`cpu` \| `alloc` \| `mixed`) |
| `NativeConsole/` | C++17 (v143, x64) | `DebugNative`, `DebugCrash` | Null deref / stack overflow / use-after-free / heap corruption (`null` \| `stack` \| `uaf` \| `heap`) |

## Build & run

```bash
# Managed fixtures
dotnet run --project tests/Skills/HelloCrash -- nre
dotnet run --project tests/Skills/HotLoop    -- cpu

# Native fixture (requires the "Desktop development with C++" workload)
msbuild tests/Skills/NativeConsole/NativeConsole.vcxproj -p:Configuration=Debug -p:Platform=x64
.\tests\Skills\NativeConsole\x64\Debug\NativeConsole.exe null
```

## Intended skill exercises

### `Debug` against HelloCrash
1. `bp.set({kind:"Line", file:"…/HelloCrash/Program.cs", line:<CrashWithNullDeref open brace>})`.
2. `debug.launch({projectId:"HelloCrash"})`.
3. When `Mode=Break`, `frame.locals` should show `s` as `null`.

### `DebugCrash` against HelloCrash
1. Run HelloCrash with WER dump capture enabled (or `dump.save` from a sibling VS).
2. `dump.open({path})` → `dump.summary` surfaces the NRE.
3. `stack.get` + `frame.locals` on top frame confirms the null.
4. With `allowDbgEng:true`, `dump.dbgeng({dumpPath, command:"!analyze -v"})` should label the fault as `NULL_POINTER_READ` or equivalent.

### `DebugPerf` against HotLoop `cpu`
1. `processes.list({nameContains:"HotLoop"})` → pid.
2. `profiler.start({pid, mode:"CpuSampling"})` → run ~10s → `profiler.stop` → `profiler.report`.
3. `BurnCpu` should dominate the hot list.

### `DebugMemory` against HotLoop `alloc`
1. `counters.subscribe({pid, sampleMs:500})`.
2. After ~30s, `counters.read` — working set/private bytes should be climbing and settling (not a true leak, it's capped).
3. `profiler.start({pid, mode:"Allocations"})` → `Allocate` should dominate allocation samples.

### `DebugNative` against NativeConsole `null`
1. `debug.attach({pid, engines:["Native"]})` (or launch with the native engine).
2. On the AV, `registers.get` → RIP in `crash_null_deref`.
3. `memory.read` around the faulting address shows zero page.

## What this is not

These fixtures are not an automated test suite — they're **manual skill exercises**. Success criteria is "the skill playbook reaches the expected hand-back state without guesswork". A proper automated suite would drive VSMCP over its pipe and assert on tool results; that's out of scope for M11.
