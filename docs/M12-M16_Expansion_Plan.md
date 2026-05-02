# VSMCP M12-M16 Expansion Plan

**Project:** Model Context Protocol server for Microsoft Visual Studio 2022 Enterprise  
**Scope:** Code Navigation, Search, Bulk Operations, Refactoring & Editor Context  
**Status:** Draft - Ready for Implementation  
**Last Updated:** 2026-04-29

---

## Executive Summary

This document specifies the expansion plan for VSMCP to support **agentic workflows** for code navigation, search, bulk operations, refactoring, and editor context. The goal is to enable AI assistants (Ollama, Claude, etc.) to:

- **Discover** files, classes, namespaces, and symbols
- **Search** across codebases with grep-style and semantic search
- **Bulk edit** across multiple files efficiently
- **Refactor** code with symbol-aware operations
- **Navigate** code with follow-along editor context

**Scope Split:** ~15% file/project CRUD, ~85% code intelligence, search, and refactoring.

---

## Current Capabilities (Verified)

| Category | Implemented | Status |
|--|--|--|
| **Build** | Yes | M3 - Full (start, rebuild, cancel, status, errors) |
| **Debug** | Yes | M4 - Full (launch, attach, step, continue, state) |
| **Files** | Yes | M2 - Basic (read, write, replace-range, open, save) |
| **Code Intelligence** | Yes | M9 - Symbol outline, goto def, refs, diagnostics, quick info |
| **Breakpoints** | Yes | M5 - Full (set, remove, list, enable, disable) |
| **Inspection** | Yes | M6 - Threads, stack, frames, locals, eval.expression |
| **Modules** | Yes | M6b - List modules, symbol status |
| **Dump Analysis** | Yes | M7 - Open, summary, save, dbgeng |
| **Diagnostics** | Yes | M8 - Events, memory snapshot, CPU timeline |

### Gaps for Agentic Workflows

| Gap | Impact |
|--|--|
| **No file listing** | Cannot discover what exists in solution |
| **No class list** | Cannot find classes without knowing exact path |
| **No member search** | Cannot find methods/fields by name |
| **No inheritance tree** | Cannot understand type relationships |
| **No grep search** | Cannot search text across files |
| **No bulk operations** | Cannot scale to many files |
| **No refactoring** | Cannot rename symbols with cross-references |
| **No editor context** | AI cannot see context on edits |

---

## Non-Goals

- Replacing Visual Studio's UI (VSMCP remains headless)
- Running without Visual Studio (bridge architecture preserved)
- VS Code support (VSMCP is VS-specific)
- Pre-2022 VS versions (2022 only)
- Cross-machine remote debugging (v2 scope)
- Full LSP server implementation (leverage VS Roslyn instead)

---

## Phase 1: File & Symbol Discovery (M12)

### Rationale
Without discovery, search and refactoring are impossible. This is the foundational layer.

### Tools

#### `file.list`
List files in solution/project/folder with glob pattern filtering.

```csharp
// Input
{
  "projectId": "optional project id",
  "folder": "optional folder path (relative to project)",
  "pattern": "glob pattern (e.g., \"*.cpp\", \"*Test*.{cpp,hpp}\")",
  "kinds": ["file", "folder"],  // filter by type
  "maxResults": 1000
}

// Output
{
  "Files": [
    {
      "path": "src/MyClass.cpp",
      "kind": "file",
      "language": "cpp",
      "projectId": "unique-project-id"
    }
  ],
  "Total": 150,
  "Truncated": false
}
```

**Implementation:**
- Use `IVsSolution/EnvDTE` for project file enumeration
- Use `IVsProject.GetMimeItemType` to determine language
- Filter by glob pattern client-side (standard .NET `Match` API)
- Return truncated with `Total` and `Truncated` flags

---

#### `file.classes`
List all classes/namespaces in solution with Roslyn semantic models.

```csharp
// Input
{
  "projectId": "optional project id",
  "namespace": "optional namespace filter (e.g., \"MyApp::\")",
  "kinds": ["namespace", "class", "struct", "enum"],
  "maxResults": 500
}

// Output
{
  "Symbols": [
    {
      "name": "MyApp::Foo",
      "kind": "namespace",
      "location": {"file": "src/Foo.h", "startLine": 1, "startColumn": 1},
      "container": "",
      "children": [...]
    },
    {
      "name": "MyClass",
      "kind": "class",
      "location": {"file": "src/MyClass.h", "startLine": 10, "startColumn": 5},
      "container": "MyApp",
      "children": [...]
    }
  ],
  "Total": 50,
  "Truncated": false
}
```

**Implementation:**
- Use `CodeSymbolsAsync` for C#/VB (Roslyn)
- For C++: approximate via text parsing (header files, class declarations)
- Recursive walk of symbol tree
- Filter by namespace pattern

---

#### `file.members`
List members (methods, fields, properties, events) of a type.

```csharp
// Input
{
  "file": "src/MyClass.cs",
  "className": "MyClass",
  "kinds": ["method", "field", "property", "event"],
  "excludeInherited": true  // default: false
}

// Output
{
  "Members": [
    {
      "name": "DoWork",
      "kind": "method",
      "signature": "void DoWork(int param1, string param2)",
      "location": {"file": "src/MyClass.cs", "startLine": 25, "startColumn": 13},
      "access": "public",
      "isStatic": false
    },
    {
      "name": "_field",
      "kind": "field",
      "signature": "private int _field",
      "location": {"file": "src/MyClass.cs", "startLine": 10, "startColumn": 19},
      "access": "private",
      "isStatic": false
    }
  ]
}
```

**Implementation:**
- Use Roslyn `SemantiModel.GetDeclaredSymbol` for C#/VB
- For C++: parse class definition block via regex or text parsing
- Return access modifiers and static flag

---

#### `file.inheritance`
Get inheritance hierarchy for a type.

```csharp
// Input
{
  "file": "src/MyClass.cpp",
  "className": "MyClass"
}

// Output
{
  "BaseTypes": [
    {"name": "BaseClass", "location": {"file": "src/BaseClass.h", "startLine": 5}}
  ],
  "DerivedTypes": [
    {"name": "ChildClass", "location": {"file": "src/ChildClass.h", "startLine": 10}},
    {"name": "AnotherChild", "location": {"file": "src/AnotherChild.h", "startLine": 12}}
  ],
  "ImplementedInterfaces": [
    {"name": "IMyInterface", "location": ...}
  ],
  "Hierarchy": {
    "depth": 3,
    "path": ["Object", "BaseClass", "MyClass"]
  }
}
```

**Implementation:**
- Use Roslyn `INamedTypeSymbol.BaseType` and `AllInterfaces` for C#/VB
- For C++: parse class declaration line (`class MyClass : public BaseClass`)
- Reverse lookup: find all classes that derive from given base

---

#### `file.glob`
Find files matching glob pattern (supports `*`, `?`, `**`, `[]`).

```csharp
// Input
{
  "patterns": ["*.cpp", "*Test*.{cpp,hpp}"],
  "projectId": "optional project id"
}

// Output
{
  "Matches": [
    {"path": "src/MyClass.cpp", "projectId": "proj1"},
    {"path": "tests/MyClassTest.cpp", "projectId": "proj1"}
  ],
  "Total": 2
}
```

**Implementation:**
- Client-side glob matching (Standard .NET `Match` with wildcards)
- Optionally integrate `IVsQueryEditQuerySave` for build status

---

#### `file.dependencies` *(C++ specific)*
List file dependencies (includes, imports).

```csharp
// Input
{
  "file": "src/MyClass.cpp"
}

// Output
{
  "Includes": [
    {"file": "src/BaseClass.h", "line": 5, "type": "local"},
    {"file": "iostream", "line": 3, "type": "system"}
  ],
  "Total": 2
}
```

**Implementation:**
- Parse `#include` directives from source
- For C++: resolve `#include "file"` vs `#include <file>`
- Track include chain depth

---

### M12 DTO Design

**New DTO files:** Create `M12Dtos.cs` (or split into `M12-FileDtos.cs`, `M12-SymbolDtos.cs`)

```csharp
namespace VSMCP.Shared;

// File list item
public sealed class FileListItem
{
    public string Path { get; set; } = "";
    public string Kind { get; set; } = "file";  // file, folder, project
    public string? Language { get; set; }
    public string? ProjectId { get; set; }
}

public sealed class FileListResult
{
    public List<FileListItem> Files { get; set; } = new();
    public int Total { get; set; }
    public bool Truncated { get; set; }
}

// Symbol info (classes, namespaces)
public sealed class SymbolInfo
{
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";
    public CodeSpan? Location { get; set; }
    public string? Container { get; set; }
    public List<SymbolInfo> Children { get; set; } = new();
    public List<SymbolInfo> BaseTypes { get; set; } = new();
    public List<SymbolInfo> DerivedTypes { get; set; } = new();
}

// Member info
public sealed class MemberInfo
{
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Signature { get; set; } = "";
    public CodeSpan? Location { get; set; }
    public string? Access { get; set; }  // public, private, protected, internal
    public bool IsStatic { get; set; }
}

// Inheritance info
public sealed class InheritanceInfo
{
    public string Name { get; set; } = "";
    public CodeSpan? Location { get; set; }
}

public sealed class InheritanceResult
{
    public List<InheritanceInfo> BaseTypes { get; set; } = new();
    public List<InheritanceInfo> DerivedTypes { get; set; } = new();
    public List<InheritanceInfo> ImplementedInterfaces { get; set; } = new();
    public HierarchyInfo? Hierarchy { get; set; }
}

public sealed class HierarchyInfo
{
    public int Depth { get; set; }
    public List<string> Path { get; set; } = new();
}

// Dependency info
public sealed class DependencyInfo
{
    public string File { get; set; } = "";
    public int Line { get; set; }
    public string Type { get; set; } = "";  // system, local
}

public sealed class DependencyListResult
{
    public List<DependencyInfo> Includes { get; set; } = new();
    public int Total { get; set; }
}
```

**RPC Interface (IVsmcpRpc.cs):**
```csharp
// M12: File & Symbol Discovery
Task<FileListResult> FileListAsync(string? projectId, string? folder, 
    string? pattern, IReadOnlyList<string>? kinds, int maxResults, 
    CancellationToken cancellationToken = default);

Task<SymbolsResult> FileClassesAsync(string? projectId, string? @namespace,
    IReadOnlyList<string>? kinds, int maxResults, CancellationToken cancellationToken = default);

Task<MembersResult> FileMembersAsync(string file, string className,
    IReadOnlyList<string>? kinds, bool excludeInherited,
    CancellationToken cancellationToken = default);

Task<InheritanceResult> FileInheritanceAsync(string file, string className,
    CancellationToken cancellationToken = default);

Task<FileListResult> FileGlobAsync(IReadOnlyList<string> patterns, 
    string? projectId, CancellationToken cancellationToken = default);

Task<DependencyListResult> FileDependenciesAsync(string file,
    CancellationToken cancellationToken = default);
```

---

## Phase 2: Search Operations (M13)

### Rationale
Once files/symbols are discoverable, search enables efficient navigation of large codebases.

### Tools

#### `search.text`
Grep-style text search across files.

```csharp
// Input
{
  "pattern": "Foo\\s*\\(",  // regex pattern
  "filePattern": "*.cpp",  // optional glob filter
  "projectId": "optional",
  "kinds": ["file", "header"],
  "maxResults": 100
}

// Output
{
  "Matches": [
    {
      "file": "src/MyClass.cpp",
      "line": 42,
      "column": 8,
      "lineText": "void Foo(int x) {",
      "contextBefore": ["#include \"MyClass.h\"", "", "class MyClass"],
      "contextAfter": ["    // implementation", "}"]
    }
  ],
  "Total": 15,
  "Truncated": false
}
```

**Implementation:**
- Use `file.read_many` to get file contents
- Apply regex to each file
- Return context lines (before/after)
- Support both regex and plain text search (configurable)

---

#### `search.symbol`
Find symbols by name/kind using Roslyn.

```csharp
// Input
{
  "namePattern": "Foo.*",  // glob or regex
  "kinds": ["method", "class"],
  "container": "MyApp::"  // optional namespace filter
}

// Output
{
  "Symbols": [
    {
      "name": "MyApp::Foo",
      "kind": "class",
      "location": {"file": "src/Foo.h", "startLine": 10},
      "container": "MyApp",
      "signature": "class Foo"
    }
  ],
  "Total": 5,
  "Truncated": false
}
```

**Implementation:**
- Build symbol index from `file.classes` + `file.members`
- Use regex/glob matching on symbol names
- For C++: parse header files

---

#### `search.classes`
Find classes by inheritance relationship.

```csharp
// Input
{
  "namePattern": "*Test",  // classes ending in Test
  "baseType": "CppUnitTest",  // derived from
  "interface": "IMyInterface",  // implements
}

// Output
{
  "Classes": [
    {
      "name": "MyTestClass",
      "location": {"file": "tests/MyTestClass.cpp", "startLine": 5},
      "base": "CppUnitTest::TestClass",
      "interfaces": ["IMyInterface"]
    }
  ],
  "Total": 12
}
```

**Implementation:**
- Use `file.inheritance` to find classes by base/interface
- Filter by name pattern

---

#### `search.members`
Find members by name/kind across solution.

```csharp
// Input
{
  "namePattern": "Get.*",
  "kinds": ["method"],
  "container": "MyApp::Foo"  // optional: limit to class
}

// Output
{
  "Members": [
    {
      "name": "GetValue",
      "kind": "method",
      "signature": "int GetValue()",
      "location": {"file": "src/Foo.cpp", "startLine": 15},
      "container": "MyApp::Foo"
    }
  ],
  "Total": 3
}
```

**Implementation:**
- Iterate over files and use Roslyn for C#/VB
- Text parsing for C++

---

#### `search.find_usages`
Find all usages (both Roslyn references AND text matches).

```csharp
// Input
{
  "file": "src/Foo.h",
  "position": {"line": 10, "column": 5}  // position of symbol
}

// Output
{
  "Usages": [
    {
      "file": "src/Bar.cpp",
      "line": 25,
      "column": 8,
      "type": "reference"  // or "text_match"
    }
  ],
  "Total": 10
}
```

**Implementation:**
- Use `CodeFindReferencesAsync` for accurate references (C#/VB)
- Fallback to text match for C++ or unsupported languages
- Combine both result sets

---

### M13 DTO Design

**New DTO files:** `M13Dtos.cs`

```csharp
// Text search match
public sealed class TextMatch
{
    public string File { get; set; } = "";
    public int Line { get; set; }
    public int Column { get; set; }
    public string LineText { get; set; } = "";
    public List<string> ContextBefore { get; set; } = new();
    public List<string> ContextAfter { get; set; } = new();
}

public sealed class TextSearchResult
{
    public List<TextMatch> Matches { get; set; } = new();
    public int Total { get; set; }
    public bool Truncated { get; set; }
}

// Symbol search result
public sealed class SymbolSearchResult
{
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";
    public CodeSpan? Location { get; set; }
    public string? Container { get; set; }
    public string? Signature { get; set; }
}

public sealed class SymbolSearchResultContainer
{
    public List<SymbolSearchResult> Symbols { get; set; } = new();
    public int Total { get; set; }
    public bool Truncated { get; set; }
}

// Class search result
public sealed class ClassSearchResult
{
    public string Name { get; set; } = "";
    public CodeSpan? Location { get; set; }
    public string? Base { get; set; }
    public List<string> Interfaces { get; set; } = new();
}

public sealed class ClassSearchResultContainer
{
    public List<ClassSearchResult> Classes { get; set; } = new();
    public int Total { get; set; }
}

// Member search result
public sealed class MemberSearchResult
{
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Signature { get; set; } = "";
    public CodeSpan? Location { get; set; }
    public string? Container { get; set; }
}

public sealed class MemberSearchResultContainer
{
    public List<MemberSearchResult> Members { get; set; } = new();
    public int Total { get; set; }
}

// Usage result
public sealed class Usage
{
    public string File { get; set; } = "";
    public int Line { get; set; }
    public int Column { get; set; }
    public string Type { get; set; } = "";  // "reference", "text_match"
}

public sealed class UsageResult
{
    public List<Usage> Usages { get; set; } = new();
    public int Total { get; set; }
}
```

**RPC Interface:**
```csharp
// M13: Search Operations
Task<TextSearchResult> SearchTextAsync(string pattern, string? filePattern, 
    string? projectId, IReadOnlyList<string>? kinds, int maxResults,
    CancellationToken cancellationToken = default);

Task<SymbolSearchResultContainer> SearchSymbolAsync(string namePattern,
    IReadOnlyList<string>? kinds, string? container, int maxResults,
    CancellationToken cancellationToken = default);

Task<ClassSearchResultContainer> SearchClassesAsync(string? namePattern,
    string? baseType, string? @interface, int maxResults,
    CancellationToken cancellationToken = default);

Task<MemberSearchResultContainer> SearchMembersAsync(string namePattern,
    IReadOnlyList<string>? kinds, string? container,
    CancellationToken cancellationToken = default);

Task<UsageResult> SearchFindUsagesAsync(string file, CodePosition position,
    CancellationToken cancellationToken = default);
```

---

## Phase 3: Bulk Operations (M14)

### Rationale
Single-file operations don't scale to large codebases. Bulk operations enable efficient multi-file workflows.

### Tools

#### `file.read_many`
Read multiple files in one call.

```csharp
// Input
{
  "paths": ["src/Foo.cpp", "src/Bar.cpp", "tests/Baz.cpp"]
}

// Output
{
  "Results": [
    {
      "path": "src/Foo.cpp",
      "content": "...",
      "error": null
    },
    {
      "path": "src/Bar.cpp",
      "content": "...",
      "error": null
    }
  ]
}
```

**Implementation:**
- Sequential reads (VS APIs are UI-thread serialized)
- Return error per-item (don't fail whole batch on single error)
- Support optional range parameter

---

#### `file.write_many`
Write to multiple files in one call.

```csharp
// Input
{
  "entries": [
    {
      "path": "src/Foo.cpp",
      "content": "new content",
      "range": null
    },
    {
      "path": "src/Bar.cpp",
      "range": {"startLine": 10, "startColumn": 1, "endLine": 15, "endColumn": 1},
      "text": "replacement text"
    }
  ],
  "openInEditor": true
}

// Output
{
  "Results": [
    {
      "path": "src/Foo.cpp",
      "bytes": 1234,
      "openInEditor": true,
      "error": null
    }
  ]
}
```

**Implementation:**
- Use `ITextBuffer.CreateEdit()` for open files
- Fallback to `File.WriteAllText` for closed files
- Track which writes go through editor

---

#### `search.replace_many`
Replace pattern across multiple files.

```csharp
// Input
{
  "pattern": "Foo",
  "replacement": "Bar",
  "filePattern": "*.cpp",
  "maxFiles": 100,
  "dryRun": false
}

// Output
{
  "Matched": 10,
  "Replaced": 10,
  "Files": [
    {"path": "src/Foo.cpp", "replacements": 2},
    {"path": "tests/Bar.cpp", "replacements": 1}
  ]
}
```

**Implementation:**
- Use `file.read_many` to get contents
- Apply regexreplacement
- Use `file.write_many` to write back

---

#### `code.*_many`
Batch code lookups.

```csharp
// Input
{
  "files": ["src/Foo.cpp", "src/Bar.cpp"]
}

// Output
{
  "Results": [
    {
      "file": "src/Foo.cpp",
      "symbols": [...],
      "language": "cpp"
    }
  ]
}
```

**Implementation:**
- Iterate over files, call `CodeSymbolsAsync` for each
- Return results in order

---

### M14 DTO Design

**Leverage existing `BatchDtos.cs`** for consistency:

```csharp
// File read request
public sealed class FileReadRequest
{
    public string Path { get; set; } = "";
    public FileRange? Range { get; set; }
}

// File write entry
public sealed class FileWriteEntry
{
    public string Path { get; set; } = "";
    public string? Content { get; set; }  // full content
    public FileRange? Range { get; set; }  // or range replacement
    public string? Text { get; set; }  // replacement text for range
}

// Result entries
public sealed class FileReadResultItem
{
    public string Path { get; set; } = "";
    public string Content { get; set; } = "";
    public BatchItemError? Error { get; set; }
}

public sealed class FileWriteResultItem
{
    public string Path { get; set; } = "";
    public int Bytes { get; set; }
    public bool OpenInEditor { get; set; }
    public BatchItemError? Error { get; set; }
}

// Replace many result
public sealed class ReplaceManyResult
{
    public int Matched { get; set; }
    public int Replaced { get; set; }
    public List<ReplaceManyFileResult> Files { get; set; } = new();
}

public sealed class ReplaceManyFileResult
{
    public string Path { get; set; } = "";
    public int Replacements { get; set; }
}

// Code batch result
public sealed class CodeBatchResult
{
    public string File { get; set; } = "";
    public List<CodeSymbol> Symbols { get; set; } = new();
    public string? Language { get; set; }
    public BatchItemError? Error { get; set; }
}
```

**RPC Interface:**
```csharp
// M14: Bulk Operations
Task<BatchResult<FileReadResultItem>> FileReadManyAsync(
    IReadOnlyList<FileReadRequest> requests,
    CancellationToken cancellationToken = default);

Task<BatchResult<FileWriteResultItem>> FileWriteManyAsync(
    IReadOnlyList<FileWriteEntry> entries, bool openInEditor,
    CancellationToken cancellationToken = default);

Task<ReplaceManyResult> SearchReplaceManyAsync(string pattern, string replacement,
    string? filePattern, int maxFiles, bool dryRun,
    CancellationToken cancellationToken = default);

Task<BatchResult<CodeBatchResult>> CodeSymbolsManyAsync(
    IReadOnlyList<string> files,
    CancellationToken cancellationToken = default);

Task<BatchResult<ReferencesResult>> CodeFindReferencesManyAsync(
    IReadOnlyList<CodePosition> positions, int maxResults,
    CancellationToken cancellationToken = default);
```

---

## Phase 4: Refactoring & Editing (M15)

### Rationale
Refactoring enables AI to modify code intelligently, not just textually.

### Tools

#### `edit.replace_all`
Replace all occurrences of pattern in file.

```csharp
// Input
{
  "file": "src/Foo.cpp",
  "pattern": "Foo",
  "replacement": "Bar",
  "maxReplacements": 100,
  "regex": true
}

// Output
{
  "Replacements": 5,
  "Text": "new file content with Bar instead of Foo"
}
```

**Implementation:**
- Use `file.read` to get content
- Apply `Regex.Replace` or string replace
- Use `file.write` to save

---

#### `edit.rename`
Rename symbol + all references (Roslyn-based).

```csharp
// Input
{
  "file": "src/Foo.h",
  "position": {"line": 10, "column": 15},  // position of symbol
  "newName": "Bar",
  "dryRun": false
}

// Output
{
  "Locations": [
    {"file": "src/Foo.h", "line": 10, "column": 15, "currentText": "Foo"},
    {"file": "src/Bar.cpp", "line": 25, "column": 10, "currentText": "Foo"}
  ],
  "Conflicts": []  // rename would create conflict
}
```

**Implementation:**
- Use `CodeFindReferencesAsync` to find all references
- Use Roslyn `Project.Solution.WithDocumentText()` for precise edits
- Dry-run mode to show changes before committing

---

#### `edit.organize_usings`
Add/remove using directives.

```csharp
// Input
{
  "file": "src/Foo.cs",
  "addMissing": true,
  "removeUnused": true
}

// Output
{
  "Changes": 3,
  "Added": ["System.Collections.Generic"],
  "Removed": ["SystemDiagnostic"]
}
```

**Implementation:**
- Use Roslyn formatting options
- Call `SyntaxTree.WithUsingDirectives()`

---

#### `edit.insert_*`
Insert text before/after line.

```csharp
// Input
{
  "file": "src/Foo.cpp",
  "line": 10,
  "text": "new line to insert",
  "openInEditor": true
}

// Output
{
  "Line": 10,
  "Text": "new line to insert",
  "OpenInEditor": true
}
```

**Implementation:**
- Use `file.read` to get lines
- Insert at line index
- Use `file.write` to save

---

#### `edit.replace_member`
Replace content by semantic selector (class + member).

```csharp
// Input
{
  "file": "src/Foo.cpp",
  "className": "Foo",
  "memberName": "DoWork",
  "newText": "void DoWork() { ... }",
  "openInEditor": true
}

// Output
{
  "Replaced": true,
  "Line": 25,
  "OpenInEditor": true
}
```

**Implementation:**
- Use `file.members` to find member location
- Use `file.replace_range` to replace

---

#### `edit.move_type`
Move type to different namespace or file.

```csharp
// Input
{
  "file": "src/Foo.cpp",
  "typeName": "Foo",
  "newNamespace": "MyApp::NewNS",
  "newFile": "src/NewFoo.cpp"
}

// Output
{
  "Success": true,
  "NewLocation": {"file": "src/NewFoo.cpp", "line": 10},
  "Conflict": false
}
```

**Implementation:**
- Parse type definition block
- Rewrite with new namespace/file
- Update references in other files

---

### M15 DTO Design

**New DTO files:** `M15Dtos.cs`

```csharp
// Rename result
public sealed class RenameLocation
{
    public string File { get; set; } = "";
    public int Line { get; set; }
    public int Column { get; set; }
    public string CurrentText { get; set; } = "";
}

public sealed class RenameResult
{
    public List<RenameLocation> Locations { get; set; } = new();
    public List<RenameLocation> Conflicts { get; set; } = new();
}

// Organize usings result
public sealed class OrganizeUsingsResult
{
    public int Changes { get; set; }
    public List<string> Added { get; set; } = new();
    public List<string> Removed { get; set; } = new();
}

// Insert result
public sealed class InsertResult
{
    public int Line { get; set; }
    public string Text { get; set; } = "";
    public bool OpenInEditor { get; set; }
}

// Replace member result
public sealed class ReplaceMemberResult
{
    public bool Replaced { get; set; }
    public int Line { get; set; }
    public bool OpenInEditor { get; set; }
}

// Move type result
public sealed class MoveTypeResult
{
    public bool Success { get; set; }
    public CodeSpan? NewLocation { get; set; }
    public bool Conflict { get; set; }
}
```

**RPC Interface:**
```csharp
// M15: Refactoring & Editing
Task<(int Replacements, string Text)> EditReplaceAllAsync(string file,
    string pattern, string replacement, int? maxReplacements, bool regex,
    CancellationToken cancellationToken = default);

Task<RenameResult> EditRenameAsync(string file, CodePosition position,
    string newName, bool dryRun,
    CancellationToken cancellationToken = default);

Task<OrganizeUsingsResult> EditOrganizeUsingsAsync(string file,
    bool addMissing, bool removeUnused,
    CancellationToken cancellationToken = default);

Task<InsertResult> EditInsertBeforeAsync(string file, int line, string text,
    bool openInEditor,
    CancellationToken cancellationToken = default);

Task<InsertResult> EditInsertAfterAsync(string file, int line, string text,
    bool openInEditor,
    CancellationToken cancellationToken = default);

Task<ReplaceMemberResult> EditReplaceMemberAsync(string file, string className,
    string memberName, string newText, bool openInEditor,
    CancellationToken cancellationToken = default);

Task<MoveTypeResult> EditMoveTypeAsync(string file, string typeName,
    string? newNamespace, string? newFile,
    CancellationToken cancellationToken = default);
```

---

## Phase 5: Navigation Context (M16)

### Rationale
"Follow-along" model: AI should see context on edits, files should open on navigation.

### Tools

#### `editor.navigate_to`
Open file and scroll to location.

```csharp
// Input
{
  "file": "src/Foo.cpp",
  "line": 25,
  "column": 10,
  "highlightRange": {"startLine": 25, "endLine": 30}
}

// Output
{
  "Opened": true,
  "Line": 25,
  "Column": 10
}
```

**Implementation:**
- Leverage existing `EditorOpenAsync`
- Add highlight support if possible (VS SDK allows selection)

---

#### `editor.snippet`
Get code snippet with context lines.

```csharp
// Input
{
  "file": "src/Foo.cpp",
  "line": 25,
  "contextBefore": 3,
  "contextAfter": 3
}

// Output
{
  "Before": ["// Line 22", "// Line 23", "// Line 24"],
  "Line": {"text": "void DoWork() {", "number": 25},
  "After": ["    // implementation", "}"]
}
```

**Implementation:**
- Use `file.read` to get file
- Extract context lines around target line
- Return structured snippet

---

#### `editor.expand/collapse_region`
Expand/collapse code regions.

```csharp
// Input (expand)
{
  "file": "src/Foo.cpp",
  "line": 25  // any line in region
}

// Output
{
  "Expanded": true,
  "Range": {"startLine": 20, "endLine": 40}
}
```

**Implementation:**
- VS has code folding APIs (need to investigate)
- May require DTE command execution

---

### M16 DTO Design

**New DTO files:** `M16Dtos.cs`

```csharp
// Navigate result
public sealed class NavigateResult
{
    public bool Opened { get; set; }
    public int Line { get; set; }
    public int Column { get; set; }
}

// Snippet result
public sealed class SnippetLine
{
    public string Text { get; set; } = "";
    public int Number { get; set; }
}

public sealed class SnippetResult
{
    public List<string> Before { get; set; } = new();
    public SnippetLine Line { get; set; } = new();
    public List<string> After { get; set; } = new();
}

// Region result
public sealed class RegionRange
{
    public int StartLine { get; set; }
    public int EndLine { get; set; }
}

public sealed class RegionResult
{
    public bool Expanded { get; set; }
    public bool Collapsed { get; set; }
    public RegionRange Range { get; set; } = new();
}

// Include navigation result
public sealed class IncludeNavigationResult
{
    public IncludeNavigationResultFound Found { get; set; } = new();
    public IncludeNavigationNavigation Navigation { get; set; } = new();
}

public sealed class IncludeNavigationResultFound
{
    public string File { get; set; } = "";
    public int Line { get; set; }
}

public sealed class IncludeNavigationNavigation
{
    public int FromLine { get; set; }
    public int ToLine { get; set; }
}
```

**RPC Interface:**
```csharp
// M16: Navigation Context
Task<NavigateResult> EditorNavigateToAsync(string file, int? line, int? column,
    bool openInEditor, CancellationToken cancellationToken = default);

Task<SnippetResult> EditorSnippetAsync(string file, int line,
    int contextBefore, int contextAfter,
    CancellationToken cancellationToken = default);

Task<RegionResult> EditorExpandRegionAsync(string file, int line,
    CancellationToken cancellationToken = default);

Task<RegionResult> EditorCollapseRegionAsync(string file, int line,
    CancellationToken cancellationToken = default);

Task<IncludeNavigationResult> EditorNavigateToIncludeAsync(string file,
    string includeName, CancellationToken cancellationToken = default);
```

---

## C++ Specific Extensions (M12+)

### Rationale
C++ has unique requirements: headers, includes, macros, preprocessor.

### Tools

#### `cpp.header_lookup`
Lookup symbol in header files.

```csharp
// Input
{
  "file": "src/MyClass.cpp",
  "symbolName": "MyClass"
}

// Output
{
  "Header": {"file": "src/MyClass.h", "line": 10},
  "Type": "class"  // class, struct, function, macro
}
```

**Implementation:**
- Parse include chain from `.cpp` file
- Search header files for symbol definition

---

#### `cpp.include_chain`
Visualize include chain for a file.

```csharp
// Input
{
  "file": "src/MyClass.cpp"
}

// Output
{
  "Chain": [
    {"file": "src/MyClass.cpp", "line": 0, "type": "entry"},
    {"file": "src/MyClass.h", "line": 5, "type": "local"},
    {"file": "iostream", "line": 3, "type": "system"}
  ]
}
```

**Implementation:**
- Parse `#include` directives recursively
- Track file/line/type for each include

---

#### `cpp.macro_lookup`
Lookup macro definition.

```csharp
// Input
{
  "name": "MAX_SIZE"
}

// Output
{
  "Definition": {"file": "src/Config.h", "line": 15, "expansion": "1024"},
  "Users": [
    {"file": "src/MyClass.cpp", "line": 25}
  ]
}
```

**Implementation:**
- Text search for `#define`
- Track all usages

---

#### `cpp.preprocess`
Get preprocessor output (like `cl /E`).

```csharp
// Input
{
  "file": "src/MyClass.cpp",
  "defines": ["DEBUG=1", "VERSION=2"]
}

// Output
{
  "Source": "preprocessed C++ source code",
  "LineMap": [
    {"sourceLine": 10, "preprocLine": 5}
  ]
}
```

**Implementation:**
- Execute `cl /E` (or compiler-specific)
- Parse output
- Return preprocessed source

---

#### `cpp.api_ref`
Lookup Windows/Win32 API from headers.

```csharp
// Input
{
  "apiName": "CreateFileW"
}

// Output
{
  "Name": "CreateFileW",
  "Type": "function",
  "Declaration": "HANDLE CreateFileW(...)",
  "Documentation": "...",
  "HeaderFile": "fileapi.h"
}
```

**Implementation:**
- Maintain API database or parse Windows SDK headers
- Support common APIs first

---

#### `cpp.generated_file`
Navigate to generated files (moc, ui, qrc).

```csharp
// Input
{
  "file": "src/MyWidget.ui",
  "type": "ui"
}

// Output
{
  "GeneratedFile": "ui_MyWidget.h",
  "GeneratedFrom": "src/MyWidget.ui",
  "LineMap": [
    {"sourceLine": 10, "generatedLine": 5}
  ]
}
```

**Implementation:**
- Match file types to generator
- Parse `.pro` files for Qt
- Track output locations

---

### C++ DTO Design

**New DTO files:** `M12-CppDtos.cs` (or `M17Dtos.cs` if adding later)

```csharp
// Header lookup result
public sealed class HeaderLookupResult
{
    public CodeSpan? Header { get; set; }
    public string Type { get; set; } = "";  // class, struct, function, macro
}

// Include chain result
public sealed class IncludeChainItem
{
    public string File { get; set; } = "";
    public int Line { get; set; }
    public string Type { get; set; } = "";  // entry, local, system
}

public sealed class IncludeChainResult
{
    public List<IncludeChainItem> Chain { get; set; } = new();
}

// Macro result
public sealed class MacroDefinition
{
    public CodeSpan? Location { get; set; }
    public string Expansion { get; set; } = "";
}

public sealed class MacroResult
{
    public MacroDefinition Definition { get; set; } = new();
    public List<CodeSpan> Users { get; set; } = new();
}

// Preprocess result
public sealed class PreprocessResult
{
    public string Source { get; set; } = "";
    public List<LineMapItem> LineMap { get; set; } = new();
}

public sealed class LineMapItem
{
    public int SourceLine { get; set; }
    public int PreprocLine { get; set; }
}

// API reference result
public sealed class ApiReferenceResult
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";  // function, struct, macro
    public string Declaration { get; set; } = "";
    public string? Documentation { get; set; }
    public string? HeaderFile { get; set; }
}

// Generated file result
public sealed class GeneratedFileInfo
{
    public string GeneratedFile { get; set; } = "";
    public string GeneratedFrom { get; set; } = "";
    public List<LineMapItem> LineMap { get; set; } = new();
}
```

**RPC Interface:**
```csharp
// C++ Extensions
Task<HeaderLookupResult> CppHeaderLookupAsync(string file, string symbolName,
    CancellationToken cancellationToken = default);

Task<IncludeChainResult> CppIncludeChainAsync(string file,
    CancellationToken cancellationToken = default);

Task<MacroResult> CppMacroLookupAsync(string name,
    CancellationToken cancellationToken = default);

Task<PreprocessResult> CppPreprocessAsync(string file,
    IReadOnlyList<string>? defines,
    CancellationToken cancellationToken = default);

Task<ApiReferenceResult> CppApiRefAsync(string apiName,
    CancellationToken cancellationToken = default);

Task<GeneratedFileInfo> CppGeneratedFileAsync(string file, string type,
    CancellationToken cancellationToken = default);
```

---

## Protocol Changes

### New Error Codes
```csharp
// ErrorCodes.cs
public const string PatternNotFound = "pattern-not-found";  // search
public const string SymbolNotFound = "symbol-not-found";    // rename
public const string RefactorConflict = "refactor-conflict"; // rename conflict
```

### Protocol Version
Increment **minor** version for non-breaking additions:
- `ProtocolMajor`: 1 (unchanged)
- `ProtocolMinor`: 12 → 17 (as M12-M16 are added)

---

## Implementation Order Summary

| Phase | Tools | Files | Priority |
|--|--|--|--|
| **M12** | 6 tools | 3-4 DTO files + RpcTarget.Files.cs | ** Highest ** |
| **M13** | 5 tools | 2 DTO files + RpcTarget.Search.cs | High |
| **M14** | 5 tools | 1 DTO file + RpcTarget.Bulk.cs | Medium |
| **M15** | 6 tools | 2 DTO files + RpcTarget.Refactor.cs | Medium |
| **M16** | 4 tools | 1 DTO file + RpcTarget.Navigation.cs | Low |
| **C++** | 6 tools | 2 DTO files + RpcTarget.Cpp.cs | Medium |

**Total:** 32 tools across 15+ files

---

## Testing Strategy

### Unit Tests
- DTO serialization (xUnit)
- Pattern matching (glob, regex)
- Batch result aggregation

### Integration Tests
- C# solution with namespaces, classes, inheritance
- C++ solution with headers, includes, macros
- Large file tests (4K+ lines)

### E2E Tests
- "Find all classes that implement IMyInterface"
- "Rename all usages of X to Y"
- "List inheritance tree for MyClass"
- "Search for pattern across solution"

---

## Non-Goals (Reiterated)

1. **VS Code support** - VSMCP is VS-specific
2. **Pre-2022 VS** - 2022 only
3. **Cross-machine debugging** - v2 scope
4. **Full LSP server** - Use VS Roslyn instead

---

## Next Steps

1. ✅ **Create M12 DTOs** - DTOs for file listing, class/member discovery
2. ✅ **Implement `file.list`** - Core file enumeration
3. ✅ **Implement `file.classes`** - Symbol listing via Roslyn
4. ✅ **Implement `file.members`** - Member discovery
5. ✅ **Implement `file.inheritance`** - Inheritance tree
6. ✅ **Implement `file.glob`** - Pattern matching
7. ✅ **Implement `file.dependencies`** - Include chains (C++)

**Then:**
- M13: Search (grep, symbol search)
- M14: Bulk ops
- M15: Refactoring
- M16: Navigation
- C++: Extensions

---

## References

- **Architecture:** See `DesignDoc.md` for VSMCP architecture
- **Current Tools:** See `IVsmcpRpc.cs` for existing RPC interface
- **DTO Pattern:** See `BatchDtos.cs` for batch result pattern
- **Testing:** See `tests/Skills.E2E/` for e2e test structure

---

**Document Version:** 1.0  
**Next Review:** After M12 implementation complete  
**Contributors:** AI-assisted agentic workflow planning
