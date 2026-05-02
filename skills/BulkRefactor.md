---
name: BulkRefactor
description: Perform batch refactoring operations across multiple files. Use when the user asks to "rename all", "replace across", "bulk edit", "fix in all files", or "update X across the solution". Supports rename, pattern replacement, and organized bulk edits.
---

# BulkRefactor playbook

## 1. Understand the operation scope

Ask the user for:
- **Operation type**: rename, replace, or other refactoring
- **Scope**: single file, specific folder, or entire solution
- **Files to include/exclude**: file patterns or paths
- **Dry-run**: Show changes before applying (recommended for large changes)

If user didn't specify:
- Default to dry-run first
- Default to entire solution
- Default to rename if symbol is known, replace if pattern is known

## 2. Choose refactoring strategy

### Option A: Symbol rename (smart, uses Roslyn)
Use `edit.rename` when:
- Renaming a symbol (class, method, variable)
- Want all references updated automatically
- Symbol location is known

Call: `edit.rename({file, position, newName, dryRun?})`

### Option B: Text replace all (simple, regex)
Use `edit.replace_all` when:
- Replacing text in a single file
- Using regex for pattern matching
- No symbol resolution needed

Call: `edit.replace_all({file, pattern, replacement, maxReplacements?, regex?})`

### Option C: Bulk replace across files
Use `search.replace_many` when:
- Replacing across multiple files
- Using glob patterns to select files
- Same pattern in many places

Call: `search.replace_many({pattern, replacement, filePattern?, maxFiles?, dryRun?})`

### Option D: Organize usings (cleanup)
Use `edit.organize_usings` when:
- Removing unused using directives (C#)
- Adding missing using directives
- Cleanup after refactor

Call: `edit.organize_usings({file, addMissing?, removeUnused?})`

## 3. For C++: Special considerations

### Header files:
- C++ has header/.cpp split
- Rename symbol in both header and implementation
- Use `cpp.header_lookup` to find header from .cpp

### Include guards:
- After rename, may need to update include guards
- Pattern: `#define CLASSNAME_H` → `#define NEWCLASSNAME_H`

### Forward declarations:
- Check if forward declarations need updating
- Use `cpp.include_chain` to find dependent files

## 4. Execute in safe order

### Step 1: Dry-run (ALWAYS recommended)
```
→ search.replace_many({..., dryRun: true})
→ edit.rename({..., dryRun: true})
→ Report: "Would change X files, Y locations"
→ Wait for user confirmation
```

### Step 2: Apply changes
```
→ search.replace_many({..., dryRun: false})
→ edit.rename({..., dryRun: false})
→ Report: "Changed X files, Y locations"
```

### Step 3: Clean up
```
→ For each modified file: edit.organize_usings({file, addMissing: true, removeUnused: true})
→ Or: Build to check for errors
```

## 5. Handle results

### Success:
- Report: number of files changed, number of replacements
- List first 5 modified files (avoid overwhelming)
- Offer to show diffs or rebuild

### Partial success:
- Some files succeeded, some failed
- Report: succeeded vs failed per-file
- Investigate failures (permissions, locks, etc.)

### Conflict detection (rename only):
- If `Conflicts` array is non-empty:
  - Rename would create name collision
  - Suggest alternative name or manual review

## 6. Verify changes

After bulk refactoring:

### Build check:
```
→ build.start({})
→ build.wait(buildId)
→ Report: Build status, errors if any
```

### Test check (if applicable):
```
→ Run relevant tests to verify no regressions
→ Or: Suggest running tests
```

### Code review:
```
→ Offer to show diff for specific files
→ Or: Suggest using IDE's diff viewer
```

## 7. Examples

### Example 1: Rename class across solution
```
> Rename MyClass to NewClass across entire solution

→ Step 1: Locate MyClass with search.symbol or file.classes
→ Step 2: edit.rename({file, position, newName: "NewClass", dryRun: true})
→ Step 3: User confirms
→ Step 4: edit.rename({..., dryRun: false})
→ Step 5: build.start({}) to verify
```

### Example 2: Replace pattern across all .cpp files
```
> Replace all "TODO:" comments with "// TODO:" in all .cpp files

→ search.replace_many({
    pattern: "TODO:",
    replacement: "// TODO:",
    filePattern: "*.cpp",
    maxFiles: 100,
    dryRun: true
})
→ Report: "Would change 15 files, 42 replacements total"
→ User confirms
→ search.replace_many({..., dryRun: false})
```

### Example 3: Bulk rename with header lookup (C++)
```
> Rename Foo::Bar to Foo::Baz in all files

→ Step 1: Find Foo::Bar with search.symbol
→ Step 2: Get header file with cpp.header_lookup
→ Step 3: Rename in header: edit.rename({file, position, newName, dryRun: true})
→ Step 4: Rename in .cpp: edit.rename({file, position, newName, dryRun: true})
→ Step 5: Replace include guards if needed
→ Step 6: Build to verify
```

### Example 4: Cleanup after manual edit
```
> Clean up all usings in SolutionProject

→ For each project file: edit.organize_usings({file, removeUnused: true, addMissing: true})
→ Or: search.text({pattern: "using .*;", filePattern: "*.cs"})
→ Then: edit.organize_usings for each
```

## Tips

1. **Always dry-run first** - Especially for large solution renames
2. **Batch operations** - Use `search.replace_many` instead of individual `edit.replace_all`
3. **C++ headers** - Remember to update header guards after rename
4. **Build after** - Verify no compilation errors
5. **Commit early** - Suggest committing before large refactors
6. **Test** - Run tests after refactoring to catch regressions
