# skills/

Claude Skills for VSMCP. Each skill is a Markdown file with YAML frontmatter describing when to invoke it and a playbook body describing how.

Skills land with milestone **M11 — Skills**. Planned set:

| File                 | Skill         |
|----------------------|---------------|
| `Project.md`         | Project       |
| `Build.md`           | Build         |
| `Debug.md`           | Debug         |
| `DebugPerf.md`       | DebugPerf     |
| `DebugMemory.md`     | DebugMemory   |
| `DebugCrash.md`      | DebugCrash    |
| `DebugNative.md`     | DebugNative   |

See [`../DesignDoc.md`](../DesignDoc.md) §10 for the skill format and design rules.

## Install

```powershell
# Claude Code
copy *.md "$env:USERPROFILE\.claude\skills\"
```
