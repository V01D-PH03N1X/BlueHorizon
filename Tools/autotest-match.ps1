<#
.SYNOPSIS
  Automated end-to-end multiplayer test: a headless dedicated server plus headless bot clients play a
  short HeavyBomb match through the real network path; the server log is checked for the expected events.
.PARAMETER Scenario
  fight   — bots shoot each other (kills, elimination round ends)
  passive — bots never shoot: attacker carries/plants, defender walks to the bomb and defuses
  explode — like passive, but the fuse is shorter than the defuse time, so the bomb detonates
  idle    — bots do nothing: rounds end on the timer
  disconnect — three passive bots; Bot1 is killed mid-round, the match must recover and finish
  reconnect  — like disconnect, then Bot1 rejoins with the same name and must get its team and stats back
  duel       — Duel mode, two fighting bots
  deathmatch — Deathmatch, four free-for-all bots, first to 5 kills
  capture    — Capture the Area, 2v2, first to 25 points
  weapongame — Weapon Game, four free-for-all bots climb the weapon ladder
  weapons    — Deathmatch, four bots with random loadouts (Milestone 4 weapons, optics, laser)
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File Tools\autotest-match.ps1 -Scenario passive
#>
param(
    [ValidateSet('fight', 'passive', 'explode', 'idle', 'disconnect', 'reconnect', 'duel', 'deathmatch', 'capture', 'weapongame', 'weapons')][string]$Scenario = 'fight',
    [int]$Bots = 2,
    [int]$Port = 7790,
    [int]$RoundsToWin = 2,
    [int]$TimeoutSeconds = 300,
    [string]$Builds = 'Builds'
)
. "$PSScriptRoot\Common.ps1"

$server = Join-Path $ProjectRoot "$Builds\Server\BlueHorizonServer.exe"
$client = Join-Path $ProjectRoot "$Builds\Client\BlueHorizon.exe"
foreach ($exe in @($server, $client)) { if (-not (Test-Path $exe)) { throw "Missing $exe. Run Tools\build.ps1 first." } }

$logs = Join-Path $ProjectRoot "Logs\autotest-$Scenario"
if (Test-Path $logs) { Remove-Item -Recurse -Force $logs }
New-Item -ItemType Directory -Force $logs | Out-Null
$serverLog = Join-Path $logs 'server.log'

$roundTime = switch ($Scenario) { 'idle' { 15 } default { 75 } }
$fuse = switch ($Scenario) { 'explode' { 4 } default { 25 } }
$defuse = switch ($Scenario) { 'explode' { 6 } default { 5 } }
$botMode = switch ($Scenario) { 'idle' { 'idle' } { $_ -in 'passive', 'explode', 'disconnect', 'reconnect' } { 'passive' } default { 'fight' } }
$modeArgs = switch ($Scenario) {
    'duel' { @('-mode', 'duel', '-roundTime', 45, '-roundsToWin', 3) }
    'deathmatch' { @('-mode', 'deathmatch', '-roundTime', 180, '-roundsToWin', 5, '-respawnDelay', 2) }
    'weapons' { @('-mode', 'deathmatch', '-roundTime', 180, '-roundsToWin', 5, '-respawnDelay', 2) }
    'capture' { @('-mode', 'capture', '-roundTime', 180, '-roundsToWin', 25, '-respawnDelay', 3) }
    'weapongame' { @('-mode', 'weapongame', '-roundTime', 200, '-respawnDelay', 2) }
    default { @('-roundTime', $roundTime, '-roundsToWin', $RoundsToWin) }
}
if ($Scenario -in 'deathmatch', 'capture', 'weapongame', 'weapons' -and $Bots -lt 4) { $Bots = 4 }
$botArgs = switch ($Scenario) { 'duel' { @('-botSite', 0) } 'weapons' { @('-botLoadout', 'random') } default { @() } }
if ($Scenario -eq 'reconnect' -and $Bots -lt 3) { $Bots = 3 }
# 2v2, so the killed bot never leaves its team empty (an empty team correctly sends the match back to warmup).
if ($Scenario -eq 'disconnect' -and $Bots -lt 4) { $Bots = 4 }

$sArgs = @('-port', $Port, '-logFile', $serverLog, '-preRoundTime', 2, '-postRoundTime', 3,
    '-plantTime', 3, '-defuseTime', $defuse, '-fuseTime', $fuse,
    '-quitAfterMatches', 1, '-quitAfter', $TimeoutSeconds) + $modeArgs
Write-Host "[$Scenario] Starting headless dedicated server on UDP $Port"
$serverProc = Start-Process -FilePath $server -ArgumentList $sArgs -PassThru -WindowStyle Hidden
Start-Sleep -Seconds 4

$clients = @()
for ($i = 1; $i -le $Bots; $i++) {
    $cArgs = @('-batchmode', '-nographics', '-connect', '127.0.0.1', '-port', $Port, '-name', "Bot$i", '-bot', '-botMode', $botMode,
        '-quitAfter', $TimeoutSeconds, '-logFile', (Join-Path $logs "bot$i.log")) + $botArgs
    $clients += Start-Process -FilePath $client -ArgumentList $cArgs -PassThru -WindowStyle Hidden
    Start-Sleep -Milliseconds 500
}
Write-Host "[$Scenario] $Bots bot client(s) started; waiting for the match to finish (timeout ${TimeoutSeconds}s)"

if ($Scenario -in 'disconnect', 'reconnect') {
    Start-Sleep -Seconds 18
    Write-Host "[$Scenario] Killing Bot1 (pid $($clients[0].Id)) mid-round"
    Stop-Process -Id $clients[0].Id -Force
}
if ($Scenario -eq 'reconnect') {
    Start-Sleep -Seconds 10
    Write-Host "[$Scenario] Restarting Bot1 with the same name"
    $cArgs = @('-batchmode', '-nographics', '-connect', '127.0.0.1', '-port', $Port, '-name', 'Bot1', '-bot', '-botMode', $botMode,
        '-quitAfter', $TimeoutSeconds, '-logFile', (Join-Path $logs 'bot1-rejoin.log'))
    $clients += Start-Process -FilePath $client -ArgumentList $cArgs -PassThru -WindowStyle Hidden
}

$finished = $serverProc.WaitForExit(($TimeoutSeconds + 15) * 1000)
foreach ($c in $clients) { if (-not $c.HasExited) { $c.WaitForExit(10000) | Out-Null; if (-not $c.HasExited) { Stop-Process -Id $c.Id -Force } } }
if (-not $finished) { Stop-Process -Id $serverProc.Id -Force; Write-Host "[$Scenario] Server did not finish in time" -ForegroundColor Red }

$log = Get-Content $serverLog
$events = $log | Where-Object { $_ -match '^\[BH\]\[(Server|Match|Bomb|Kill|Ability|Hosting|Loadout)\]' }
$events | ForEach-Object { Write-Host "  $_" }

$ffa = $Scenario -in 'deathmatch', 'weapongame', 'weapons'
$checks = [ordered]@{
    'Server started'          = '\[BH\]\[Server\] Started'
    'Players joined'          = $(if ($ffa) { 'joined \(free-for-all\)' } else { 'joined team Alpha' })
    'Second side joined'      = $(if ($ffa) { "Player 'Bot2'.*joined" } else { 'joined team Bravo' })
    'Match started'           = '\[BH\]\[Match\] Match started'
    'Round went live'         = 'Round 1 live'
    'Round ended'             = 'Round \d+ ended'
    'Match over'              = '\[BH\]\[Match\] Match over'
}
switch ($Scenario) {
    'fight'   { $checks['Kills happened'] = '\[BH\]\[Kill\]' }
    'passive' { $checks['Bomb planted'] = 'Bomb planted at site'; $checks['Bomb defused'] = 'Bomb defused' }
    'explode' { $checks['Bomb planted'] = 'Bomb planted at site'; $checks['Bomb exploded'] = 'Bomb exploded' }
    'idle'    { $checks['Timer ended rounds'] = 'Time expired' }
    'duel'       { $checks['Kills happened'] = '\[BH\]\[Kill\]'; $checks['Duel mode active'] = "Mode 'duel' ready" }
    'deathmatch' { $checks['Kills happened'] = '\[BH\]\[Kill\]'; $checks['Kill limit or time'] = '5 kills reached|Time expired' }
    'capture'    { $checks['Area captured'] = 'Area captured by'; $checks['Points or time'] = '25 points reached|Time expired' }
    'weapongame' { $checks['Ladder advanced'] = 'advanced to level'; $checks['Final weapon or time'] = 'Final weapon kill|Time expired' }
    'weapons'    { $checks['Random loadouts applied'] = '\[BH\]\[Loadout\] Bot\d+ loadout [\d,]+ \(applied\)'; $checks['Kills happened'] = '\[BH\]\[Kill\]'; $checks['Kill limit or time'] = '5 kills reached|Time expired' }
    'reconnect' { $checks['Bot1 disconnect handled'] = "Client \d+ 'Bot1' disconnected"; $checks['Bot1 reconnected with state'] = "Player 'Bot1' reconnected to match" }
    'disconnect' { $checks['Bot1 disconnect handled'] = "Client \d+ 'Bot1' disconnected"; $checks['Round ended after disconnect'] = 'Round \d+ ended' }
}

$failed = 0
foreach ($k in $checks.Keys) {
    $ok = [bool]($log | Select-String -Pattern $checks[$k] -Quiet)
    if ($ok) { Write-Host "  PASS  $k" -ForegroundColor Green } else { Write-Host "  FAIL  $k" -ForegroundColor Red; $failed++ }
}
if ($Scenario -eq 'weapons') {
    # Bot loadouts are generated valid, so the server must never have to correct one; and the bots should not all carry the same primary.
    $corrected = [bool]($log | Select-String -Pattern 'invalid loadout request' -Quiet)
    if ($corrected) { Write-Host '  FAIL  No loadout corrections' -ForegroundColor Red; $failed++ } else { Write-Host '  PASS  No loadout corrections' -ForegroundColor Green }
    $primaries = $log | Select-String -Pattern 'Bot\d+ loadout (\d+),' | ForEach-Object { $_.Matches[0].Groups[1].Value } | Sort-Object -Unique
    Write-Host "  INFO  distinct primary weapons: $($primaries -join ', ')"
}
$errors = $log | Select-String -Pattern 'Exception|\[BH\]\[[A-Za-z]+\].*(error|Error)' | Select-Object -First 10
if ($errors) { Write-Host '  Errors/exceptions in server log:' -ForegroundColor Yellow; $errors | ForEach-Object { Write-Host "    $_" } }

$summary = [pscustomobject]@{ scenario = $Scenario; passed = ($failed -eq 0); failedChecks = $failed; logs = $logs }
$summary | ConvertTo-Json | Set-Content (Join-Path $logs 'result.json')
if ($failed -gt 0) { exit 1 }
Write-Host "[$Scenario] PASSED" -ForegroundColor Green
