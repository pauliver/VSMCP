---
name: CodeSearch
description: Search for code across files and projects. Use when the user asks to "find", "search", "grep", "locate", or "look up" code, classes, functions, symbols, or patterns. Can perform text search, symbol search, inheritance lookup, and usage finding.
---

# CodeSearch playbook

## 1. Understand the search scope

Ask the user for:
- Specific file(s) to search (if known)
- Project or folder to limit search scope
- Search pattern or symbol name
- Whether to include headers (for C++)

If user didn't specify:
- Default to searching the entire solution
- Default to text search if symbol name is unknown
- Default to class/method search if symbol name is known

## 2. Choose search strategy

### Option A: Symbol search (precise, uses Roslyn)
Use `search.symbol` when you know:
- Symbol name (exact or pattern)
- Symbol kind (class, method, property, etc.)
- Optionally: container (namespace/class)

Call: `search.symbol({namePattern, kinds[], container?, maxResults})`

### Option B: Text search (grep-style, broader)
Use `search.text` when:
- You know a pattern but not symbol name
- Searching for text matches across files
- Using regex patterns

Call: `search.text({pattern, filePattern?, projectId?, kinds[], maxResults})`

### Option C: Class search (inheritance-based)
Use `search.classes` when:
- Finding classes by base type or interface
- Finding test classes (e.g., "*Test" classes)
- Navigating type hierarchy

Call: `search.classes({namePattern?, baseType?, interface?, maxResults})`

### Option D: Member search (method/field lookup)
Use `search.members` when:
- Looking for a specific method/field across many classes
- Searching by name pattern

Call: `search.members({namePattern, kinds?, container?})`

### Option E: Find usages (References + text matches)
Use `search.find_usages` when:
- Finding all usages of a symbol
- Both Roslyn references AND text matches

Call: `search.find_usages({file, position})`

## 3. For C++: Use header-aware search

If working with C++ code:

### Find symbol in header files
- Use `cpp.header_lookup({file, symbolName})` to locate symbol definition
- This searches across include chain

### Include chain visualization
- Use `cpp.include_chain({file})` to see what headers a file depends on
- Helps understand where symbols might be defined

## 4. Handle results

### If results found:
- Report: symbol name, file path, line number, context
- For classes: show base/derived relationships
- For methods: show signature and access level
- For usages: show all locations where symbol is used

### If results truncated:
- Note that `Truncated: true` in response
- Request more results or narrow search scope

### If no results:
- Try broader pattern (remove filters)
- Try text search instead of symbol search
- Check file coverage (maybe symbol not in current project)

## 5. Next steps

After finding code:

### To examine:
- Call `file.read` on found file(s)
- Call `editor.navigate_to` to open file and focus location

### To refactor:
- Call `edit.rename` to rename symbol (if rename needed)
- Call `edit.replace_all` to replace pattern across files
- Call `edit.organize_usings` to cleanup after refactor

### To understand:
- Call `file.inheritance` for type hierarchy
- Call `file.members` for class contents
- Call `code.symbols` for document outline

## Examples

### Example 1: Find all classes that implement IMyInterface
```
> Find all classes that implement IMyInterface

→ search.classes({interface: "IMyInterface", maxResults: 100})
→ Report: Found X classes with their file paths and line numbers
→ For each: Offer to show class contents with file.read
```

### Example 2: Find all usages of a function
```
> Find all usages of MyClass::DoWork

→ First, use file.members to locate DoWork method
→ Then, call search.find_usages({file, position})
→ Report all locations where DoWork is called
```

### Example 3: Search for pattern across solution
```
> Search for "TODO:" comments in all .cpp files

→ search.text({pattern: "TODO:", filePattern: "*.cpp", maxResults: 50})
→ Report matches with context lines
```

### Example 4: Find header for symbol
```
> Where is MyClass defined?

→ If working in .cpp: use cpp.header_lookup({file, "MyClass"})
→ Otherwise: search.symbol({namePattern: "^MyClass$", kinds: ["class"]})
→ Report header file and line number
```

## Tips

1. **Always verify** - Search results are approximate, verify by reading file
2. **Use `maxResults`** - Limit results to avoid overwhelming the user
3. **Use `filePattern`** - Narrowscope (e.g., "*.cpp" vs all files)
4. **C++ headers** - Symbols may be in headers, use `cpp.header_lookup`
5. **Follow-along** - Use `editor.navigate_to` to show results in editor
