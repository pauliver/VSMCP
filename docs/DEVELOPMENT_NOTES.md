# Development Notes

Non-obvious things future-you needs when working on this codebase. Kept
deliberately short — architecture lives in `DesignDoc.md`, this file is only for
traps that weren't obvious from the code or docs the first time around.

---

## VSIX packaging

### ProjectReference DLLs must opt into the VSIX container

`ProjectReference` on its own **does not** pack dependency DLLs into the `.vsix`.
The manifest will appear correct and the build will succeed, but the package
fails to load at runtime with the cryptic:

> SetSite failed for package [VSMCPPackage] HRESULT 0x80070002

— because `VSMCP.Shared.dll` (or whatever the missing dep is) was never copied
into the installed extension folder.

Required form:

```xml
<ProjectReference Include="..\VSMCP.Shared\VSMCP.Shared.csproj">
  <Name>VSMCP.Shared</Name>
  <Private>true</Private>
  <IncludeInVSIX>true</IncludeInVSIX>
</ProjectReference>
```

Both metadata are needed: `Private` gets the DLL into `bin/`, `IncludeInVSIX`
gets it into the `.vsix`. `<Private>false</Private>` is a silent bug.

### `AsyncPackage` lifecycle and persisted tool windows

`ToolWindowPane.Initialize()` can run **before** `AsyncPackage.InitializeAsync`
completes when VS restores a persisted tool window on startup. At that moment,
`ToolWindowPane.Package` may be null and any state the package exposes may not
yet be constructed.

Rules for tool windows:

1. Do not rely on `Package` being sited in `Initialize()`.
2. Do not lazy-init shared state on the package — a `??=` fallback will happily
   create an orphan instance that nothing else is using. Init eagerly at field
   declaration so there's exactly one instance for the package's life.
3. Expose a static `Instance` on the package as a fallback path so the tool
   window can still find the package even if `Package` is null.

This was the cause of the "tool window shows 0 RPCs forever" bug: `HostActivity`
was lazy, the tool window grabbed instance A, `InitializeAsync` later replaced
the field with instance B, and `PipeHost` fed B while the UI listened to A.

---

## VSIXInstaller operational quirks

- **`/quiet` is not a valid flag.** Running silently requires no flag at all
  (just run the GUI installer headless-ish by closing it after the window
  paints — or don't, and let the user click through).
- **Logs:** `%TEMP%\dd_VSIXInstaller_*.log`. The most recent file by mtime is
  usually the one you want.
- **Install location:**
  `%LOCALAPPDATA%\Microsoft\VisualStudio\17.0_f3950181\Extensions\<hash>\`
  where `<hash>` is a per-install random folder name.
- **Cache invalidation is fragile.** `ExtensionMetadataCache.sqlite` remembers
  the folder hash. Manually `rm -rf`ing an extension folder makes VS fail to
  load from the cached path on next startup with `FileNotFoundException` on
  `VSMCP.Vsix.dll`. Safer path: uninstall via VS Extensions UI, or accept that
  after a manual delete you must reinstall so the cache repoints.
- **Duplicate installs** happen if you accidentally launch the installer twice
  — two folders with the same extension GUID, and VS will load one and ignore
  the other (or fail unpredictably). Always verify only one folder exists after
  install.

---

## E2E tests

- Single `E2ECollection` with `DisableParallelization = true` is mandatory.
  Visual Studio is COM-STA, there's only one debugger, and there's only one
  named pipe to connect to. Parallel tests will stomp each other.
- `[SkippableFact]` + `VSMCP_E2E=1` env var keeps these out of routine `dotnet
  test` runs. CI should not set the env var unless there's a hosted VS instance.
- Fixtures live at `tests/Skills/` (HelloCrash.sln, HotLoop, etc.). The fixture
  root is discovered at runtime by walking up from the test bin folder.
- To exercise the full suite, open the specific fixture solutions in VS before
  running; tests skip cleanly when their required solution isn't open.

---

## Windows / Git Bash tooling

- **Path conversion:** Git Bash on Windows rewrites `/something` as a POSIX
  path when passing args to native `.exe` binaries. Set `MSYS_NO_PATHCONV=1`
  before invoking `VSIXInstaller.exe`, `devenv.exe`, `MSBuild.exe`, etc.,
  otherwise flags like `/quiet` become `C:/Program Files/Git/quiet`.
- **MSBuild flags:** use `-p:Foo=Bar` syntax, not `/p:Foo=Bar`, when running
  from Git Bash. The `/` form is ambiguous with a POSIX path.
- **Background installers:** don't launch `VSIXInstaller.exe` via bash `&`
  because the shell can fire the same command twice (foreground + queued
  background) and you'll end up with duplicate extension folders.

---

## Git workflow

- PRs are squash-merged via `gh pr merge --squash --delete-branch`. After a
  squash merge, `git branch -d <feature>` will refuse because the squash creates
  a brand-new commit with no parent link back to the branch tip. Use
  `git branch -D` to force-delete stale feature branches, after confirming the
  corresponding PR is merged.
- `main` is the shipping branch; no release tags or GH Releases yet. Consumers
  build from source.
