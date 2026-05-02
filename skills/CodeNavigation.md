---
name: CodeNavigation
description: Navigate and explore code structure. Use when the user asks to "list", "show", "display", "explain", or "navigate" code. Supports file listing, class/member exploration, inheritance tree viewing, and symbol lookup.
---

# CodeNavigation playbook

## 1. Understand the navigation scope

Ask the user for:
- **Scope**: Entire solution, specific project, or folder
- **File patterns**: Filter by extension (e.g., "*.cpp", "*.h")
- **Symbol kinds**: Filter by type (class, method, property, etc.)
- **Depth**: How detailed (directory list vs full class contents)

If user didn't specify:
- Default to entire solution
- Default to no file pattern filter
- Default to showing all symbol kinds

## 2. Choose navigation strategy

### Option A: List files (folder navigation)
Use `file.list` when:
- See what files exist in a folder
- Filter by pattern (e.g., "*.test.*")
- Explore project structure

Call: `file.list({projectId?, folder?, pattern?, kinds[], maxResults?})`

### Option B: List classes (type exploration)
Use `file.classes` when:
- See all classes/namespaces in solution
- Filter by namespace
- Explore type hierarchy

Call: `file.classes({projectId?, namespace?, kinds[], maxResults?})`

### Option C: List class members (method/field exploration)
Use `file.members` when:
- See what methods/fields a class has
- Filter by member kind
- Understand class structure

Call: `file.members({file, className, kinds[], excludeInherited?})`

### Option D: Show inheritance tree (hierarchy visualization)
Use `file.inheritance` when:
- See base classes of a type
- See derived classes of a type
- Understand inheritance relationships

Call: `file.inheritance({file, className})`

### Option E: Find files by pattern (glob navigation)
Use `file.glob` when:
- Find all test files ("*Test*.{cpp,hpp}")
- Find all header files in a path
- Locate files by naming convention

Call: `file.glob({patterns[], projectId?})`

## 3. For C++: Special navigation

### Include chain visualization
Use `cpp.include_chain` to:
- See what headers a file includes
- Trace dependency chain
- Understand compilation unit structure

Call: `cpp.include_chain({file})`

### Header lookup
Use `cpp.header_lookup` to:
- Find header from .cpp file
- Locate symbol definition in headers
- Bridge between implementation and interface

Call: `cpp.header_lookup({file, symbolName})`

### Navigate to include
Use `editor.navigate_to_include` to:
- Jump from `#include` directive to header file
- Follow include chain
- Open header at correct line

Call: `editor.navigate_to_include({file, includeName})`

## 4. Navigate step by step

### For class exploration:
```
1. file.classes({projectId?, maxResults: 50})
   → Get list of all classes
2. user selects class of interest
3. file.members({file, className})
   → See class members (methods, fields)
4. file.inheritance({file, className})
   → See inheritance tree
5. If needed: cpp.header_lookup({file, className})
   → Find header location (C++)
```

### For file exploration:
```
1. file.list({projectId?, folder?, pattern?})
   → Get file list
2. user selects file of interest
3. file.classes({file})  (if not C++)
   → See classes in file
4. file.members({file, className})
   → See members
5. editor.navigate_to({file, line})
   → Open file in editor
```

### For inheritance investigation:
```
1. file.inheritance({file, className})
   → Get base types, derived types, interfaces
2. For each base: file.members({file, baseClassName})
   → See what base provides
3. For each derived: file.members({file, derivedClassName})
   → See what derived adds
4. Use cpp.include_chain if C++ to see full context
```

## 5. Open files for inspection

After navigation, open files with follow-along:

### Basic navigation:
```
→ editor.navigate_to({file, line, column})
   → Opens file and scrolls to location
```

### High-level overview:
```
→ editor.snippet({file, line, contextBefore: 5, contextAfter: 5})
   → Shows file contents with context around line
```

### Code folding:
```
→ editor.expand_region({file, line})
   → Expand collapsed region (e.g., #region in C#)
```

## 6. Handle results

### If results truncated:
- `file.list` and `file.classes` have `Truncated` flag
- Report: "Showing first X of Y results"
- Suggest narrowing scope (projectId, folder, pattern)

### If symbol not found:
- Verify symbol name spelling
- Check project is loaded
- Try text search as fallback

### If C++ headers:
- Header may not be in file list (header-only projects)
- Use `cpp.header_lookup` to find header
- Check include chain for include order

## 7. Examples

### Example 1: Explore project structure
```
> Show me the project structure

→ file.list({projectId?})
→ Report: Files and folders in project
→ For each folder: file.list({projectId, folder})
→ Build tree structure
```

### Example 2: What classes are in this solution?
```
> List all classes in MyProject

→ file.classes({projectId: "MyProject", kinds: ["class", "struct"]})
→ Report: X classes with namespaces
→ For each class: Offer to see members with file.members
```

### Example 3: What methods does MyClass have?
```
> Show me MyClass methods

→ file.members({file, className: "MyClass", kinds: ["method"]})
→ Report: Methods with signatures and access levels
→ For specific method: Offer to see implementation
```

### Example 4: What inherits from MyBaseClass?
```
> Show me classes that derive from MyBaseClass

→ file.inheritance({file, className: "MyBaseClass"})
→ Report: Derived types and their locations
→ For each derived: Offer to see class contents
```

### Example 5: Find all test classes
```
> Find all test classes

→ file.glob({patterns: ["*Test.*", "*Tests.*"]})
→ Or: file.classes({projectId?, namePattern: "*Test"})
→ Report: Test classes with file paths
→ For each: Offer to run tests
```

### Example 6: Navigate include chain (C++)
```
> Show me what files MyClass.cpp includes

→ cpp.include_chain({file: "src/MyClass.cpp"})
→ Report: Chain of includes (entry, local, system)
→ For specific include: editor.navigate_to_include({file, includeName})
```

### Example 7: Find header for symbol
```
> Where is MyFunction declared?

→ If working in .cpp: cpp.header_lookup({file, "MyFunction"})
→ Otherwise: search.symbol({namePattern: "^MyFunction$", kinds: ["method"]})
→ Report: Header file and line number
→ Open with editor.navigate_to({headerFile, headerLine})
```

## Tips

1. **Start broad, narrow down** - Use file.list first, then drill down
2. **Use patterns** - Filter with `pattern` and `namePattern` early
3. **Show hierarchy** - Use file.inheritance to understand relationships
4. **Follow-along** - Use editor.navigate_to to open files as you explore
5. **C++ headers** - Remember implementation (.cpp) vs interface (.h) split
6. **Dry-run navigation** - For large lists, show truncated count first
7. **Context matters** - Use editor.snippet to see surrounding code
