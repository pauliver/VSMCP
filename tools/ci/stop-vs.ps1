# Gracefully closes any running Visual Studio, force-killing stragglers. Used by the
# live-e2e lane's always() cleanup step so a wedged VS can't poison the next run.
$procs = Get-Process devenv -ErrorAction SilentlyContinue
if (-not $procs) { Write-Host 'No devenv running.'; exit 0 }

$procs | ForEach-Object { $_.CloseMainWindow() | Out-Null }
Start-Sleep -Seconds 15

Get-Process devenv -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Write-Host 'Visual Studio shut down.'
exit 0
