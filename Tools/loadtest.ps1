<#
.SYNOPSIS
  Ten-player server load test: one headless dedicated server and N headless bot clients fight for a fixed
  duration. Prints the server's periodic [BH][Perf] lines (frame time, GC, player count) and match activity.
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File Tools\loadtest.ps1 -Bots 10 -DurationSeconds 150
  powershell -ExecutionPolicy Bypass -File Tools\loadtest.ps1 -Bots 15 -Mode deathmatch
#>
param(
    [int]$Bots = 10,
    [int]$DurationSeconds = 150,
    [int]$Port = 7798,
    [string]$Mode = 'heavybomb',
    [string]$Builds = 'Builds'
)
. "$PSScriptRoot\Common.ps1"

$server = Join-Path $ProjectRoot "$Builds\Server\BlueHorizonServer.exe"
$client = Join-Path $ProjectRoot "$Builds\Client\BlueHorizon.exe"
$logs = Join-Path $ProjectRoot 'Logs\loadtest'
if (Test-Path $logs) { Remove-Item -Recurse -Force $logs }
New-Item -ItemType Directory -Force $logs | Out-Null
$serverLog = Join-Path $logs 'server.log'

$serverProc = Start-Process -FilePath $server -PassThru -WindowStyle Hidden -ArgumentList @(
    '-port', $Port, '-logFile', $serverLog, '-perfLogInterval', 15, '-quitAfter', ($DurationSeconds + 10),
    '-roundTime', $(if ($Mode -eq 'heavybomb') { 60 } else { 900 }), '-preRoundTime', 3, '-postRoundTime', 3,
    '-roundsToWin', $(if ($Mode -eq 'heavybomb') { 13 } else { 1000 }), '-mode', $Mode)
Start-Sleep -Seconds 4
$clients = @()
for ($i = 1; $i -le $Bots; $i++) {
    $clients += Start-Process -FilePath $client -PassThru -WindowStyle Hidden -ArgumentList @(
        '-batchmode', '-nographics', '-connect', '127.0.0.1', '-port', $Port, '-name', "Load$i", '-bot',
        '-perfLogInterval', 30, '-quitAfter', $DurationSeconds, '-logFile', (Join-Path $logs "bot$i.log"))
    Start-Sleep -Milliseconds 300
}
Write-Host "Running $Bots bots for ${DurationSeconds}s..."
$serverProc.WaitForExit(($DurationSeconds + 30) * 1000) | Out-Null
foreach ($c in $clients) { if (-not $c.HasExited) { Stop-Process -Id $c.Id -Force } }
if (-not $serverProc.HasExited) { Stop-Process -Id $serverProc.Id -Force }

$log = Get-Content $serverLog
Write-Host '--- Server performance ---'
$log | Where-Object { $_ -match '\[BH\]\[Perf\]' } | ForEach-Object { Write-Host "  $_" }
Write-Host '--- Activity ---'
Write-Host ("  joins: {0}  kills: {1}  rounds ended: {2}  abilities: {3}" -f
    ($log | Select-String 'joined').Count, ($log | Select-String '\[BH\]\[Kill\]').Count,
    ($log | Select-String 'Round \d+ ended').Count, ($log | Select-String '\[BH\]\[Ability\].* used ').Count)
$exceptions = ($log | Select-String 'Exception').Count
Write-Host "  exceptions in server log: $exceptions"
Write-Host '--- Client prediction (corrections per 30 s window) ---'
Get-ChildItem $logs -Filter 'bot*.log' | ForEach-Object {
    $last = Get-Content $_.FullName | Where-Object { $_ -match '\[BH\]\[Perf\]' } | Select-Object -Last 1
    if ($last) { Write-Host "  $($_.BaseName): $($last -replace '.*rtt', 'rtt')" }
}
