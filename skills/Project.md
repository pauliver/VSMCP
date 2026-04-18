---
name: Project
description: Create, modify, or inspect a .NET project inside the VS solution currently open through VSMCP. Use when the user asks to add/remove a project, add/remove a file, create a folder, read or edit a project property, or open/save a source file through VS's editor (so undo/redo works). Not for building (use Build), not for debugging (use Debug).
---

# Project playbook

## 1. Confirm a solution is open
`vs.status()`. If `SolutionOpen=false`, ask the user for a `.sln` path and call `solution.open({path})` before continuing.

## 2. Locate the project
`project.list()` to resolve the user's target (prefer `UniqueName`, fall back to `Name`, last resort `FullPath`). If the user names a project that doesn't exist and is asking to **add** one, go to §4; otherwise ask them to pick from the list.

## 3. Read intent
- "show me / what is" → read-only: `project.properties.get`, `file.read`.
- "create / add" → §4.
- "remove / delete" → §5 (destructive — confirm with user first).
- "edit / change" → §6.

## 4. Add
- New project: `project.add({templateOrProjectPath, destinationPath, projectName})`.
- Existing file into project: `project.file.add({projectId, path, linkOnly?})`. Default `linkOnly=false` (copies into the project folder).
- Folder: `project.folder.create({projectId, path})`.

## 5. Remove (destructive — confirm)
- Project: `project.remove({projectId})` — does not delete files from disk.
- File: `project.file.remove({projectId, path, deleteFromDisk?})`. Ask before `deleteFromDisk=true`.

## 6. Edit
- Property: `project.properties.set({projectId, values:{key:value}})`. Null clears.
- Source file: prefer `file.replace_range({path, range, text})` for targeted edits; `file.write` for full rewrites. If the file is already open in VS, edits route through the text buffer so VS undo works. Call `editor.save({path})` or `editor.save_all()` when done.

## 7. Hand back
One line: what changed, which project, which file.
