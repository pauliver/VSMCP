# VSMCP — AI Operator's Guide

This document is for an AI agent (Claude, etc.) that has VSMCP's MCP tools available. It tells you **what to call, in what order, and how to stay efficient on context**.

If you are a human reading this: see [`README.md`](./README.md). Everything below assumes the reader is an LLM with `mcp__vsmcp__*` tools loaded.

---

## What VSMCP gives you

A live Visual Studio 2022 (or 2026) running on the user's machine, accessible over MCP. You can:

- Open / close solutions, list and modify projects, NuGet, build configurations
- Build, capture errors and warnings, run tests
- Read and search code (Roslyn-aware, semantic)
- Edit code at multiple granularities — line range, member, type, file
- Debug: launch/attach, breakpoints, stepping, locals, watch expressions, threads, modules, memory
- Crash dumps: open `.dmp`, summarize, walk threads, run DbgEng commands
- Profile CPU and memory, capture and read trace events
- Drive the editor itself: open files, scroll, focus, toggle outlining

All tools are namespaced `mcp__vsmcp__<name>`. Tool count is high (~200) — use the discovery patterns below instead of trying to memorize them.

---

## First call, every session

Before anything else:

```
mcp__vsmcp__vs_status        # is VS attached? what solution?
```

If `solution_open` is false, ask the user what to open or call `mcp__vsmcp__solution_open`.

If the user has multiple VS windows open, your tools may be hitting the wrong one:

```
mcp__vsmcp__vs_list_instances    # see all running VSMCP-enabled VS
mcp__vsmcp__vs_select(processId) # bind future calls to one
```

---

## Follow mode (a.k.a. teaching mode / AutoFocus)

When the user is **watching the IDE** and wants to see what you're doing:

```
mcp__vsmcp__vs_set_autofocus(enabled: true)
```

With it on:

1. Every file read or edit opens that file in the editor and scrolls to the relevant line.
2. The IDE window is brought to the foreground after each tool call.
3. Files VSMCP opened are saved (if you edited them) and **closed automatically 10 seconds later**.
4. Re-touching a file before the timer fires cancels the close and re-arms it.
5. Files the user already had open are scrolled to but **never auto-closed**.

When to turn it off:

- The user is not watching (background work, autonomous loops).
- You're doing many quick reads — the tab churn is noisy.
- You want the file to stay open after you finish.

```
mcp__vsmcp__vs_set_autofocus(enabled: false)
mcp__vsmcp__vs_get_autofocus()
```

Default is **on**. Don't toggle it without a reason.

---

## Reading code efficiently

Picking the right read tool saves order-of-magnitude tokens. Match granularity to the question:

| You want…                                | Use this                                                  |
|-----------------------------------------|-----------------------------------------------------------|
| Whole file, you'll process all of it    | `file_read(path)`                                         |
| Specific line range                     | `file_read(path, range:{startLine,endLine,...})`          |
| Top-level structure of a C# file        | `file_outline(path)` — types + members, no bodies         |
| Top-level structure of a C/C++ file     | `cpp_outline(path)` — namespaces, types, functions; tokenizer-based |
| Members of a named C++ class            | `cpp_class_members(file, className)`                       |
| Read a C++ member's body                | `cpp_read_member(file, className, memberName)` — outline + brace-walk |
| All classes/structs in the solution     | `cpp_classes(namePattern?, kinds?)` — solution-wide aggregation |
| Find a C++ symbol by name               | `cpp_find_symbol(name, kind?)` — returns Container chain   |
| C++ symbol summary                      | `cpp_symbol_summary(symbol)` — aggregates find_symbol + quick_info |
| Type / decl text at a C++ cursor        | `cpp_quick_info(file, line, col)` — libclang-backed        |
| References (single-TU)                  | `cpp_find_references(file, line, col)` — libclang, fast    |
| References (whole solution)             | `cpp_find_references_solution(file, line, col)` — walks every TU |
| Jump to C++ definition                  | `cpp_goto_definition(file, line, col)` — libclang-backed   |
| Real C++ compile errors / warnings      | `cpp_diagnostics(file)` — libclang diagnostics             |
| Rename a C++ symbol                     | `cpp_rename(file, line, col, newName)` — single-TU rewrite |
| Replace a C++ method body               | `cpp_replace_member(file, className, memberName, newCode)` |
| Generate a C++ constructor              | `cpp_generate_constructor(file, className, memberNames?)`  |
| Override a virtual member               | `cpp_override_member(file, className, methodName, retType, params)` |
| Sort/dedupe/group C++ includes          | `cpp_organize_includes(file)`                              |
| Bundled C++ symbol context              | `cpp_investigate(symbol)` — decl + type + body + callers   |
| Solution-wide C++ rename                | `cpp_rename_solution(file, line, col, newName)` — multi-TU |
| Move a C++ type to another file         | `cpp_move_type(src, typeName, dest)` — header-only v1      |
| Move a C++ method to another file       | `cpp_move_method(src, class, method, dest)` — auto-qualify |
| C++ class hierarchy tree                | `cpp_inheritance(file, className, depth?)`                 |
| Generate C++ operator==/!=              | `cpp_generate_equality(file, className)`                   |
| Implement all base-class virtuals       | `cpp_implement_interface(derivedFile, derivedClass, base)` |
| Scaffold a new .h/.cpp pair             | `cpp_scaffold_file(headerPath, className, ns?)`            |
| Suggest #include for a missing symbol   | `cpp_suggest_includes(symbol)`                             |
| Batch C++ outline                       | `cpp_outline_many(files)`                                  |
| Open Folder mode (no .sln)              | `project.load_workspace_folder()` once, then Roslyn tools work |
| Just the members of one class           | `file_members(path, className)`                           |
| One method's body                       | `code_read_member(path, className, memberName)`           |
| Quick info on a symbol at a position    | `code_quick_info(path, line, column)`                     |
| All references to a symbol              | `code_find_references(path, line, column)`                |
| Search for a string across the solution | `search_text_compact(query)` (NOT `file_glob` + read)     |
| Symbol fuzzy search                     | `search_symbol(query)` / `code_find_symbol(name)`         |
| Diagnostics for a file                  | `code_diagnostics_grouped(path)`                          |

**Default to the smallest tool that answers the question.** `file_outline` before `file_read`. `search_text_compact` before grepping by hand. Batch tools (`file_read_many`, `code_symbols_many`, `eval_expression_many`) when you have N independent lookups.

---

## Editing code

Order of preference (most surgical → least):

1. **`edit_replace_member`** — replace one method/property/field by name. Roslyn-aware. Best for "rewrite this function."
2. **`edit_insert_before` / `edit_insert_after`** — line-anchored insertions.
3. **`edit_add_member`** — add a new member to an existing type.
4. **`edit_rename`** — semantic rename (all references).
5. **`file_replace_range`** — line/column range replacement. Use when none of the above fit.
6. **`file_write`** — full-file overwrite. Last resort; loses cursor position and editor state.

After structural changes:

- `code_diagnostics(path)` to verify it still parses
- `build_start` + `build_wait` + `build_errors_grouped` for a real check

For **using directives / includes**, prefer the dedicated helpers (`edit_add_using`, `edit_organize_usings`, `edit_add_include`) over hand-editing the top of the file.

---

## Build / test loop

```
mcp__vsmcp__build_start              # kick a build
mcp__vsmcp__build_wait               # block until it finishes (or use build_status to poll)
mcp__vsmcp__build_summary            # one-line result
mcp__vsmcp__build_errors_grouped     # if it failed
```

`build_summary` first — it tells you if you even need the errors. `build_errors_grouped` collapses repeated errors and is much cheaper than `build_output` (raw log).

Tests:

```
mcp__vsmcp__test_run_summary         # run all + summarize (cheap)
mcp__vsmcp__test_run(filter)         # run a subset
mcp__vsmcp__test_discover            # list available tests first
```

---

## Debugging

Typical attach-and-inspect flow:

```
mcp__vsmcp__processes_list                                 # find the target
mcp__vsmcp__debug_attach(processId)
mcp__vsmcp__bp_set(file, line, condition?)
# trigger the bug, then:
mcp__vsmcp__debug_state                                    # are we stopped? where?
mcp__vsmcp__stack_get
mcp__vsmcp__frame_locals_summary                           # cheaper than frame_locals
mcp__vsmcp__eval_expression("expr")
mcp__vsmcp__debug_step_over / step_into / step_out / continue
mcp__vsmcp__debug_detach
```

Crash dumps:

```
mcp__vsmcp__dump_open(path, symbolPath?)
mcp__vsmcp__dump_summary
mcp__vsmcp__threads_list
mcp__vsmcp__threads_switch(threadId)
mcp__vsmcp__stack_get
```

For dumps with no managed symbols, ask the user for a symbol path before walking stacks.

---

## Token-efficiency rules of thumb

- Prefer `*_summary` and `*_grouped` over raw lists when you only need an overview.
- Prefer `*_compact` variants over their non-compact counterparts.
- Use `*_many` batch tools instead of N separate calls.
- For symbol/member walks, `file_outline` → `code_read_member` beats `file_read` of the whole file.
- Don't re-read a file you just edited unless you have a reason to verify; the edit tools tell you what changed.

---

## Pitfalls

- **Multiple VS instances**: if a tool reports "wrong solution," call `vs_list_instances` then `vs_select`.
- **Stale buffers**: `file_read` reads the live editor buffer if open (may be dirty). The result includes `hasUnsavedChanges` — check it.
- **Builds that never finish**: `build_wait` has a default timeout; if it returns "running", call `build_status` periodically rather than waiting forever. `build_cancel` if needed.
- **Side-effecting tools** (`eval_expression` with effects, `memory_write`, `dump.dbgeng`) may be gated by user config (`allowSideEffects`, `allowDbgEng`). Check the error if a call is rejected.
- **C++ feature parity with C#** as of 2026-05-05. **Discovery**: `cpp_outline` (+ `_many`), `cpp_classes`, `cpp_find_symbol`, `cpp_class_members`, `cpp_read_member`, `cpp_symbol_summary`, `cpp_inheritance`, `cpp_header_lookup`, `cpp_include_chain`, `cpp_macro_lookup`, `cpp_preprocess`, `cpp_suggest_includes`. **Semantic** (libclang-backed, out-of-process sidecar): `cpp_diagnostics`, `cpp_quick_info`, `cpp_find_references` (single-TU fast / `_solution` whole-solution), `cpp_goto_definition`, `cpp_invalidate`, `cpp_investigate`. **Mutation / refactoring**: `cpp_rename` (single-TU) / `cpp_rename_solution` (cross-TU), `cpp_replace_member`, `cpp_move_type`, `cpp_move_method`, `cpp_organize_includes`. **Code generation**: `cpp_generate_constructor`, `cpp_generate_equality`, `cpp_override_member`, `cpp_implement_interface`, `cpp_scaffold_file`, `cpp_create_class`. **Remaining genuinely C#-only**: nothing substantive — only Roslyn-driven niceties that don't apply to C++. **Cold-start**: the libclang sidecar spawns on first cpp_* semantic call (~1-3s); subsequent calls warm. Always `cpp_invalidate(file)` after editor saves, since the analyzer parses on-disk content.
- **Open Folder mode**: VS without a .sln only sees `<MiscFiles>`. Run `project.load_workspace_folder()` at the start of the session — it scans the folder for .csproj files and loads them into an in-process sidecar workspace so symbol lookups, file outlines, and class searches work. Edit tools (`edit_rename`, `edit_move_type`, etc.) still need a real .sln.
- **Don't disable follow mode silently**. If you turn it off mid-session, the user may stop seeing your work in the IDE. Tell them.

---

## When you're stuck

1. `vs_status` — is everything still connected?
2. `ping` — round-trip sanity check.
3. `solution_info` — what does VS think it has loaded?
4. Look at `code_diagnostics_grouped` for the file you're editing.
5. As a last resort, ask the user to check `Tools → VSMCP → Status` in the IDE.
