# Plan: VSMCP Semantic Code Layer (M18)

## Context

AI agents using VSMCP today are forced to think at the **file/line-number level**:
to replace a method they must discover the file, read it, parse the outline, extract line numbers, then finally call `file.replace_range`. That's 3–4 round trips, hundreds of wasted tokens on large-file reads, and brittle line-number math that breaks when files are reformatted.

The goal of this plan is a **semantic layer** — a new set of tools where the AI expresses intent in code terms (class names, method names, qualified symbols) and the server resolves them to locations internally. This cuts typical edit workflows from 4 calls to 1, reduces token consumption by 80–95% on large files, and makes edits robust to line-number drift.

The exploration found that **all the Roslyn infrastructure needed already exists** (`FileMembersAsync`, `GetCodeSpan`, `FindDocument`, `WalkOutline`) — what's missing is the semantic API surface on top of it.

---

## What Exists That We Build On

| Utility | File | Notes |
|---|---|---|
| `FileMembersAsync` | `RpcTarget.FilesExtensions.cs:326` | Returns `MemberInfo` with `CodeSpan` per member |
| `FileClassesAsync` | `RpcTarget.FilesExtensions.cs:227` | Walks Roslyn AST for types + namespaces |
| `FileInheritanceAsync` | `RpcTarget.FilesExtensions.cs:376` | Base types, interfaces, derived types |
| `GetCodeSpan(ISymbol)` | `RpcTarget.FilesExtensions.cs:310` | ISymbol → 1-based file/line/col span |
| `FindDocument(Solution, path)` | `RpcTarget.FilesExtensions.cs:525` | Resolves file path to Roslyn Document |
| `GetWorkspaceAsync` | `RpcTarget.FilesExtensions.cs:515` | Entry to VisualStudioWorkspace |
| `WalkOutline` | `RpcTarget.Code.cs` | Recursive symbol tree walker |
| `FileReplaceRangeAsync` | `RpcTarget.Files.cs:107` | The low-level edit workhorse |
| `ProjectFileAddAsync` | `RpcTarget.Project.cs:171` | Creates file on disk + adds to VS project |
| `ProjectPropertiesGetAsync` | `RpcTarget.Project.cs` | Can read `RootNamespace` property |

---

## New Capabilities (M18 Semantic Layer)

### 1. `code.find_symbol` — Cornerstone Enabler

**Problem**: Every position-based tool (`code.goto_definition`, `code.find_references`, `code.quick_info`) creates a circular dependency: you need to know where a symbol is to look it up.

**Solution**: Walk `VisualStudioWorkspace.CurrentSolution` across all projects/documents, find symbols by name or qualified name. Returns file + span.

```
code.find_symbol(name: "Parser.DoWork", kind?: "method") 
→ { QualifiedName, Kind, Signature, Location: { File, StartLine, ... } }
```

**Implementation path**: Extend `WalkOutline` to accept a name filter; iterate all solution documents. Reuse `GetCodeSpan` + `ToCodeSymbol`.

**Impact**: Unlocks #2–#8 below as thin wrappers. Replaces 3-call discovery chains with a single lookup.

---

### 2. `code.read_member` — Token Savings

**Problem**: AI reads a 500-line file to find a 20-line method. 96% of tokens wasted.

**Solution**: Given class + member name (or qualified name), return only that member's source code.

```
code.read_member(file?: "/path/to/Parser.cs", className: "Parser", memberName: "DoWork")
→ FileReadResult { Content: "public void DoWork() { ... }", StartLine: 45, EndLine: 64 }
```

**Implementation path**: `FileMembersAsync` → find member `CodeSpan` → `FileReadAsync(file, CodeSpan→FileRange)`. ~15 lines of new code.

**Token impact**: Typical method: 30 lines. Typical file: 400 lines. **93% token reduction** per member read.

---

### 3. `edit.add_member` — Add Code to an Existing Class

**Problem**: No way to add a new method/property/field to a class without knowing its exact closing-brace line.

**Solution**: Find the class's closing `}`, insert the new member before it.

```
edit.add_member(file?: null, className: "Parser", memberCode: "public void Validate() { ... }", 
                insertBefore?: "DoWork")
→ { InsertedAtLine: 88, ClassName: "Parser", File: "/path/Parser.cs" }
```

**Implementation path**: Roslyn `BaseTypeDeclarationSyntax.CloseBraceToken` gives exact span of closing brace → `FileReplaceRangeAsync` with empty source range at that line - 1 to insert. If `insertBefore` given, use `FileMembersAsync` to find that member's start line instead.

---

### 4. Using/Include Directive Management

**Problem**: Adding `using System.Linq;` or `#include "MyHeader.h"` requires knowing the line number of the last directive. Removing an unused one requires reading + searching.

**Solution**: Atomic add/remove that handles placement automatically.

**Tools**:
- `edit.add_using(file, namespaceName)` → `{ Added, AlreadyPresent, InsertedAtLine }`
- `edit.remove_using(file, namespaceName)` → `{ Removed, WasPresent }`
- `code.suggest_usings(file, symbolNames[])` → `[{ SymbolName, Namespace, Confidence }]`
- `edit.add_include(file, headerPath, isSystem: bool)` → `{ Added, AlreadyPresent, InsertedAtLine }` (C++)

**Implementation paths**:
- Add using: Roslyn `CompilationUnitSyntax.Usings` → find last using's end line → insert after. Deduplicate.
- Remove using: find `UsingDirectiveSyntax` matching namespace → `FileReplaceRangeAsync` with empty replacement.
- Suggest usings: `SemanticModel.GetDiagnostics()` filtered to CS0246/CS0103 → `SymbolFinder.FindDeclarationsAsync` to resolve symbol → return namespace candidates.
- Add include: regex find last `#include` line in file → insert after. Deduplicate.

---

### 5. Code Scaffolding — `project.namespace_for_path` + `code.scaffold_file`

**Problem**: When creating a new file, the AI doesn't know: what namespace to use, where to put the file, or how to add it to the project.

**Tools**:

`project.namespace_for_path(projectId, relativePath)`:
- Reads `RootNamespace` via `ProjectPropertiesGetAsync`
- Maps folder segments to namespace: `src/Services/Auth/` → `MyApp.Services.Auth`
- Returns `{ Namespace, RootNamespace, SuggestedAbsolutePath }`

`code.scaffold_file(projectId, relPath, content?, language?)`:
- Infers namespace via `project.namespace_for_path`
- Wraps `content` (or empty body) in namespace block + standard boilerplate
- Calls `FileWriteAsync` then `ProjectFileAddAsync`
- Returns `{ FilePath, Namespace, AddedToProject }`

**Implementation path**: `ProjectPropertiesGetAsync` for `RootNamespace` → string manipulation for folder→namespace mapping → compose with existing `FileWriteAsync` + `ProjectFileAddAsync`.

---

### 6. `code.create_class` — C# Class Creation with Auto-Wiring

**Problem**: Creating a subclass requires knowing: target file path, correct namespace, which usings to add, and what abstract members need stubs. That's 5+ calls today.

**Solution**: One call that handles everything.

```
code.create_class(
  name: "FastParser",
  baseClass?: "Parser",         // fully or partially qualified
  interfaces?: ["IDisposable"],
  projectId?: "MyProject",
  folder?: "src/Services",       // relative to project; defaults to project root
  generateStubs?: true           // generate abstract/virtual member overrides
)
→ {
    FilePath: "C:/repo/src/Services/FastParser.cs",
    Namespace: "MyApp.Services",
    ClassName: "FastParser",
    GeneratedUsings: ["using MyApp.Core;", "using System;"],
    GeneratedMembers: ["DoWork", "Validate"],   // stubs created
    AddedToProject: true
  }
```

**Implementation path**:
1. `code.find_symbol(baseClass)` → resolve to file + namespace
2. `project.namespace_for_path(projectId, folder)` → get target namespace
3. `FileInheritanceAsync` on base class → get abstract/virtual members → generate stubs
4. Generate C# file content (namespace block, using directives, class declaration, member stubs)
5. `code.scaffold_file` → write + add to project

---

### 7. `cpp.create_class` — C++ Header+Source Pair

**Problem**: C++ class creation requires: `.h` with `#pragma once` + class declaration, `.cpp` with `#include "ClassName.h"` + method bodies, both added to project.

```
cpp.create_class(
  name: "FastParser",
  baseClass?: "Parser",          // will add #include for base class header
  headerFolder?: "include/",
  sourceFolder?: "src/",
  projectId?: "MyProject",
  generateVirtualStubs?: true
)
→ {
    HeaderPath: "include/FastParser.h",
    SourcePath: "src/FastParser.cpp",
    AddedToProject: true
  }
```

**Implementation path**:
1. `CppHeaderLookupAsync(baseClass)` (M17, to be implemented) → find base class header path
2. Generate `.h`: `#pragma once` + `#include "BaseClass.h"` + class declaration with virtual method stubs
3. Generate `.cpp`: `#include "FastParser.h"` + method body skeletons
4. `ProjectFileAddAsync` for both files

**Note**: Depends on M17 `cpp.header_lookup` (issue #59). Can ship a simplified version without base-class stub generation first.

---

### 8. Semantic Wrappers — `editor.open_at_symbol` + `bp.set_on_entry`

Thin wrappers that call `code.find_symbol` then an existing positional tool.

- `editor.open_at_symbol(symbolPath, kind?)` → find symbol → `EditorOpenAsync(file, line, col)`
- `bp.set_on_entry(className, methodName, condition?)` → find method first line → `BreakpointSetAsync(Kind=Line, file, line)`

---

## New Files to Create

| File | Purpose |
|---|---|
| `src/VSMCP.Shared/M18Dtos.cs` | New DTOs: `SymbolMatch`, `SymbolMatchResult`, `NamespaceInfo`, `ScaffoldResult`, `CreateClassResult`, `CppCreateClassResult`, `AddUsingResult`, `RemoveUsingResult`, `UsingSuggestion`, `UsingSuggestionsResult` |
| `src/VSMCP.Vsix/RpcTarget.Semantic.cs` | `FindSymbolAsync`, `ReadMemberAsync`, `AddMemberAsync`, `OpenAtSymbolAsync`, `BpSetOnEntryAsync` |
| `src/VSMCP.Vsix/RpcTarget.UsingManagement.cs` | `AddUsingAsync`, `RemoveUsingAsync`, `SuggestUsingsAsync`, `AddIncludeAsync` |
| `src/VSMCP.Vsix/RpcTarget.Scaffolding.cs` | `NamespaceForPathAsync`, `ScaffoldFileAsync`, `CreateClassAsync`, `CppCreateClassAsync` |
| `src/VSMCP.Server/VsmcpTools.Semantic.cs` | 12 new `[McpServerTool]` methods |

## Files to Modify

| File | Change |
|---|---|
| `src/VSMCP.Shared/IVsmcpRpc.cs` | Add 12 new method signatures in M18 section |
| `src/VSMCP.Shared/ProtocolVersion.cs` | Bump `Minor` to 18 when this ships |
| `src/VSMCP.Shared/ErrorCodes.cs` | Add `SymbolAmbiguous = "VSMCP-symbol-ambiguous"` (multiple matches for unqualified name) |

## GitHub Issues to File

1. **[M18] `code.find_symbol` — solution-wide symbol lookup by name** (cornerstone; unblocks #2–#8)
2. **[M18] `code.read_member` — read a single member's code by class+name**
3. **[M18] `edit.add_member` — add a method/property/field to an existing class by name**
4. **[M18] Using/include directive management** (`edit.add_using`, `edit.remove_using`, `code.suggest_usings`, `edit.add_include`)
5. **[M18] Code scaffolding** (`project.namespace_for_path` + `code.scaffold_file`)
6. **[M18] `code.create_class` — C# class creation with auto-namespace, auto-usings, base class stubs**
7. **[M18] `cpp.create_class` — C++ header+source pair creation with base class wiring**
8. **[M18] Semantic wrappers** (`editor.open_at_symbol`, `bp.set_on_entry`)

## Implementation Order

```
Phase 1 (unblock AI code reading — 1–2 days):
  code.find_symbol → code.read_member

Phase 2 (unblock AI code writing — 2–3 days):
  edit.add_member → edit.add_using / edit.add_include → code.suggest_usings

Phase 3 (new file creation — 3–4 days):
  project.namespace_for_path → code.scaffold_file → code.create_class

Phase 4 (C++ + wrappers — 2–3 days):
  cpp.create_class → editor.open_at_symbol → bp.set_on_entry
```

## Verification

Since this is Windows-only (VS 2022 required), verification must happen on a Windows agent:

1. **`code.find_symbol`**: Call with `"Parser.DoWork"` → assert `Location.File` is non-empty and `Location.StartLine > 0`
2. **`code.read_member`**: Call on a known method → assert returned `Content` matches expected source, `Content` does NOT contain sibling methods
3. **`edit.add_member`**: Add `public void TestMethod() {}` to a class → call `file.read` on whole file → assert new method appears inside class braces
4. **`edit.add_using`**: Add `using System.Linq;` to a file → assert it appears at top, is not duplicated on second call
5. **`code.create_class`**: Create `FastParser : Parser` → assert file exists on disk, is added to project (`file.list` shows it), namespace matches folder convention, base class using is present
6. **`cpp.create_class`**: Create `FastParser : Parser` → assert `.h` has `#pragma once`, `.cpp` has `#include "FastParser.h"`, both appear in `file.list`
7. **E2E**: "Create a class MathParser that extends Parser and implements IValidator" → single `code.create_class` call → buildable C# class (run `build.start` + `build.wait` → no errors)
