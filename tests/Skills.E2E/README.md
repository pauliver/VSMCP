# VSMCP Skills e2e tests

Opt-in automated suite that drives each Claude Skill playbook over the pipe
against a live VS 2022 + VSMCP VSIX. Mirrors the manual exercises described
in `tests/Skills/README.md`.

## Why opt-in

Each test launches a fixture process, talks to a running devenv.exe over its
named pipe, and in some cases profiles or captures a dump — too slow and too
environment-specific for CI. Skipped by default.

## Running

1. Launch VS 2022 Enterprise with the VSIX installed (F5 the `VSMCP.Vsix`
   project, or install the `.vsix` into your user hive).
2. For tests that need a solution open (DebugSkill), open
   `tests/Skills/HelloCrash/HelloCrash.csproj` in that instance.
3. From a shell:

   ```bash
   VSMCP_E2E=1 dotnet test tests/Skills.E2E
   ```

## Coverage

| Test                              | Skill binding        | Fixture       |
| --------------------------------- | -------------------- | ------------- |
| `DebugSkillTests`                 | `Debug`              | HelloCrash    |
| `DebugCrashSkillTests`            | `DebugCrash`         | HelloCrash    |
| `DebugPerfSkillTests`             | `DebugPerf`          | HotLoop       |
| `DebugMemorySkillTests`           | `DebugMemory`        | HotLoop       |
| `DebugNativeSkillTests`           | `DebugNative`        | (process list) |

## What these tests are not

- A replacement for the manual skill walkthroughs — those still prove the
  playbook is understandable to an LLM. These prove the *tool surface* the
  playbooks depend on stays wired up.
- A benchmark. CPU/memory assertions are "non-trivial" thresholds, not
  regression gates.
- Runnable on GitHub's `windows-latest` runners (no VS Enterprise there).
