---
name: DebugNative
description: Debug the unmanaged / C++ side of a process through VSMCP — inspect memory, CPU registers, and disassembly; attach with the native engine; handle mixed-mode scenarios. Use when the user says "unmanaged", "C++", "native heap", "access violation at 0x…", "disassembly", "registers", or is debugging a P/Invoke boundary. Not for pure managed frames (use Debug), not for post-mortem (use DebugCrash, which can fall back here).
---

# DebugNative playbook

## 1. Attach with the native engine
`debug.attach({pid, engines:["Native"]})` (or `["Managed (.NET Core)","Native"]` for mixed-mode). Omit `engines` only when VS's auto-pick has already DTRT in this session.

## 2. Reach the faulting frame
If already broken, `debug.state()`. Otherwise `debug.break_all`, then `threads.list` → `threads.switch` to the interesting thread. `stack.get({depth:32})`.

## 3. Walk the native frame
For the frame of interest:
- `frame.switch({frameIndex})`.
- `registers.get()` — full group tree (CPU, CPU Segments, Floating Point, SSE). Read RIP/RSP/RBP and the first-4-args calling-convention registers (RCX/RDX/R8/R9 on x64 Windows).
- `disasm.get({address:rip, count:64})` — includes symbol names + source mapping when PDBs are available.

## 4. Memory probes
- Read: `memory.read({address, length})`. Cap at 64 KiB — for larger ranges, call repeatedly.
- Write: `memory.write({address, hex, allowSideEffects:true})`. **Always** confirm with the user first; writing into a running process corrupts state.
- If the address came from a register or a pointer deref, sanity-check it's inside a loaded module by cross-referencing `modules.list` base+size.

## 5. Symbols
- `modules.list` → find the faulting module → `symbols.status({moduleId})`. If `Loaded=false`, `symbols.load({moduleId})` and ask the user for a `.pdb` path / symbol server if that fails.
- A stripped module means disasm without source. Prefer `disasm.get` over guessing; use `dump.dbgeng({command:"!analyze -v"})` (requires `allowDbgEng`) for broader heuristics.

## 6. Common patterns
- **Access violation**: check the faulting address in `registers.get` then `memory.read` near it — pattern-match for `0x00000000` (null), `0xCCCCCCCC` (uninit stack), `0xDDDDDDDD` / `0xFEEEFEEE` (freed heap).
- **P/Invoke boundary**: walk the stack until you cross from managed → native; compare argument registers at the boundary against the P/Invoke signature in source.
- **Stack corruption**: RBP/RSP that don't match the frame layout, `stack.get` truncating early — suspect buffer overrun; inspect neighbors with `memory.read`.

## 7. Hand back
State: function + module + offset + likely cause (null deref / use-after-free / UB pattern / ABI mismatch). Suggest the source change only on request.
