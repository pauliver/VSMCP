# Launches Visual Studio with the given solution and waits until the VSMCP named pipe
# (\\.\pipe\VSMCP.<pid>) appears, i.e. the VSIX finished loading. Used by the live-e2e lane.
param(
    [string]$Solution = 'src/VSMCP.sln',
    [int]$TimeoutSec = 300
)

$ErrorActionPreference = 'Stop'

$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
if (-not (Test-Path $vswhere)) { throw 'vswhere.exe not found — is Visual Studio installed?' }
$devenv = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.CoreEditor -property productPath
if (-not $devenv) { throw 'vswhere found no Visual Studio installation.' }

$slnPath = (Resolve-Path $Solution).Path
Write-Host "Launching $devenv with $slnPath"
$proc = Start-Process -FilePath $devenv -ArgumentList "`"$slnPath`"" -PassThru

$deadline = (Get-Date).AddSeconds($TimeoutSec)
while ((Get-Date) -lt $deadline) {
    if ($proc.HasExited) { throw "devenv exited early with code $($proc.ExitCode)." }
    $pipes = [System.IO.Directory]::GetFiles('\\.\pipe\') | Where-Object { $_ -like '*VSMCP.*' }
    if ($pipes) {
        Write-Host "VSMCP pipe is up: $($pipes -join ', ')"
        exit 0
    }
    Start-Sleep -Seconds 5
}
throw "VSMCP pipe did not appear within ${TimeoutSec}s — is the VSIX installed on this runner?"
