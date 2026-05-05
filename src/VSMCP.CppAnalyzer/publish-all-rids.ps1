#!/usr/bin/env pwsh
# Publish VSMCP.CppAnalyzer for all supported RIDs.
#
# Build verification:
#   - win-x64: builds and runs on this Windows machine.
#   - linux-x64, osx-arm64: cross-publish only — verifies the project + native
#     restore (libclang.so / libclang.dylib) succeeds. Smoke-testing on those
#     platforms requires running the analyzer there; CI is the right place.
#
# Output: bin/Release/net9.0/<rid>/publish/ for each rid. Each folder is
# self-contained (no separate .NET 9 runtime install required on the target).

param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$rids = @("win-x64", "linux-x64", "osx-arm64")

Push-Location $PSScriptRoot
try {
    foreach ($rid in $rids) {
        Write-Host "==> publishing $rid" -ForegroundColor Cyan
        dotnet publish -c $Configuration -r $rid --self-contained
        if ($LASTEXITCODE -ne 0) {
            Write-Error "publish failed for $rid"
            exit 1
        }
        $publishDir = Join-Path $PSScriptRoot "bin\$Configuration\net9.0\$rid\publish"
        $native = switch ($rid) {
            "win-x64"    { "libclang.dll" }
            "linux-x64"  { "libclang.so" }
            "osx-arm64"  { "libclang.dylib" }
        }
        $nativePath = Join-Path $publishDir $native
        if (-not (Test-Path $nativePath)) {
            Write-Error "expected native binary not found: $nativePath"
            exit 1
        }
        Write-Host "    OK  $nativePath" -ForegroundColor Green
    }
}
finally {
    Pop-Location
}
