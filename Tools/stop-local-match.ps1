# Stops all locally running Blue Horizon servers and clients started from the Builds folder.
. "$PSScriptRoot\Common.ps1"
Get-Process -Name 'BlueHorizonServer', 'BlueHorizon' -ErrorAction SilentlyContinue | ForEach-Object {
    Write-Host "Stopping $($_.ProcessName) ($($_.Id))"
    Stop-Process -Id $_.Id -Force
}
