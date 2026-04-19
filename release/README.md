# Release pipeline

Pushing a tag of the form `v*` (e.g. `v0.2.0`) triggers `.github/workflows/release.yml`,
which builds everything in `Release` config, creates a GitHub Release, and attaches:

- `VSMCP.Vsix-<version>.vsix` — Visual Studio 2022 extension.
- `VSMCP.Server.<version>.nupkg` — stdio MCP bridge, installable as a `dotnet tool`.

## Cutting a release

```bash
# Bump versions as needed, commit, then:
git tag v0.2.0
git push origin v0.2.0
```

The workflow resolves the version from the tag (`v0.2.0` → `0.2.0`) and passes it to
MSBuild and `dotnet pack`. No manual version bumps in csproj/vsixmanifest are required
if the projects pick up `-p:Version` / `-p:PackageVersion`.

## Optional publishing

The workflow conditionally publishes to NuGet and the VS Marketplace if the
corresponding repository secrets are set. Without them the GitHub Release is still
produced — only the public-registry push is skipped.

| Secret | Purpose | How to obtain |
| --- | --- | --- |
| `NUGET_API_KEY` | Push `VSMCP.Server.nupkg` to [nuget.org](https://www.nuget.org/). | [nuget.org account settings → API Keys](https://www.nuget.org/account/apikeys). Scope it to the `VSMCP.Server` package id. |
| `VSCE_PAT` | Push the `.vsix` to the [Visual Studio Marketplace](https://marketplace.visualstudio.com/). | [Azure DevOps PAT](https://dev.azure.com/) with `Marketplace → Publish` scope, associated with the `pauliver` publisher. |

Add them under **Settings → Secrets and variables → Actions**.

### Marketplace manifest

`release/publishManifest.json` drives the Marketplace metadata. Update the `publisher`
field if you fork this project. `release/overview.md` is the short description shown on
the Marketplace listing.

## Code signing — deferred

The VSIX and the `.nupkg` are currently unsigned. Once we have an Authenticode / code-signing
certificate, add these steps to `release.yml`:

- **VSIX**: run `VsixSignTool.exe sign /f <cert.pfx> /p <password> /tr http://timestamp.digicert.com /td sha256 /fd sha256 <path-to-vsix>` before upload.
- **NuGet**: run `dotnet nuget sign <nupkg> --certificate-path <cert.pfx> --certificate-password <password> --timestamper http://timestamp.digicert.com` before push.

Store cert material in secrets (`CODESIGN_PFX_B64`, `CODESIGN_PFX_PASSWORD`) and decode
in a step that runs before the sign step.
