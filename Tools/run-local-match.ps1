<#
.SYNOPSIS
  Starts a local dedicated server and N windowed clients that connect to it automatically.
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File Tools\run-local-match.ps1 -Clients 2
  powershell -ExecutionPolicy Bypass -File Tools\run-local-match.ps1 -Clients 1 -Bots 1 -DevSolo
.NOTES
  Stop everything with Tools\stop-local-match.ps1 (or close the windows). Logs: Logs\local\*.log
#>
param(
    [int]$Clients = 2,
    [int]$Bots = 0,
    [int]$Port = 7777,
    [string]$Builds = 'Builds',
    [switch]$DevSolo,
    [string[]]$ServerArgs = @()
)
. "$PSScriptRoot\Common.ps1"

$server = Join-Path $ProjectRoot "$Builds\Server\BlueHorizonServer.exe"
$client = Join-Path $ProjectRoot "$Builds\Client\BlueHorizon.exe"
foreach ($exe in @($server, $client)) { if (-not (Test-Path $exe)) { throw "Missing $exe. Run Tools\build.ps1 first." } }

$logs = Join-Path $ProjectRoot 'Logs\local'
New-Item -ItemType Directory -Force $logs | Out-Null

$sArgs = @('-port', $Port, '-logFile', (Join-Path $logs 'server.log')) + $ServerArgs
if ($DevSolo) { $sArgs += '-devSoloStart' }
Write-Host "Starting dedicated server on UDP $Port"
Start-Process -FilePath $server -ArgumentList $sArgs -WindowStyle Minimized | Out-Null
Start-Sleep -Seconds 3

for ($i = 1; $i -le $Clients; $i++) {
    $cArgs = @('-connect', '127.0.0.1', '-port', $Port, '-name', "Player$i", '-screen-width', '1280', '-screen-height', '720', '-screen-fullscreen', '0', '-logFile', (Join-Path $logs "client$i.log"))
    Write-Host "Starting client Player$i"
    Start-Process -FilePath $client -ArgumentList $cArgs | Out-Null
}
for ($i = 1; $i -le $Bots; $i++) {
    $bArgs = @('-connect', '127.0.0.1', '-port', $Port, '-name', "Bot$i", '-bot', '-batchmode', '-nographics', '-logFile', (Join-Path $logs "bot$i.log"))
    Write-Host "Starting headless bot Bot$i"
    Start-Process -FilePath $client -ArgumentList $bArgs -WindowStyle Hidden | Out-Null
}
Write-Host "Server log: $logs\server.log"
