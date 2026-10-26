<#
.SYNOPSIS
  Hit-registration test matrix: a shooting bot against a strafing bot under simulated round-trip latencies,
  with lag compensation on and off. Prints the hit ratio, average rewind and prediction corrections per run.
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File Tools\latency-matrix.ps1
  powershell -ExecutionPolicy Bypass -File Tools\latency-matrix.ps1 -Latencies 0,150 -JitterMs 15 -LossPercent 2
.NOTES
  Latency/jitter/loss are simulated inside both bot clients' sockets (IcmpSafeUdpInterface); the server runs clean.
  All runs execute in parallel on separate ports. Logs: Logs\latency\<run>\
#>
param(
    [int[]]$Latencies = @(0, 50, 100, 150),
    [int]$JitterMs = 0,
    [float]$LossPercent = 0,
    [int]$DurationSeconds = 180,
    [int]$BasePort = 7830,
    [string]$Builds = 'Builds'
)
. "$PSScriptRoot\Common.ps1"

$server = Join-Path $ProjectRoot "$Builds\Server\BlueHorizonServer.exe"
$client = Join-Path $ProjectRoot "$Builds\Client\BlueHorizon.exe"
$root = Join-Path $ProjectRoot 'Logs\latency'
if (Test-Path $root) { Remove-Item -Recurse -Force $root }

$runs = @()
$port = $BasePort
foreach ($latency in $Latencies) {
    foreach ($lagComp in @($true, $false)) {
        $name = "rtt{0}_{1}" -f $latency, $(if ($lagComp) { 'lagcomp' } else { 'nolagcomp' })
        $dir = Join-Path $root $name
        New-Item -ItemType Directory -Force $dir | Out-Null
        $sArgs = @('-port', $port, '-logFile', (Join-Path $dir 'server.log'), '-perfLogInterval', 10, '-quitAfter', ($DurationSeconds + 10),
            '-roundTime', 40, '-preRoundTime', 2, '-postRoundTime', 2, '-roundsToWin', 50)
        if (-not $lagComp) { $sArgs += '-noLagComp' }
        $procs = @(Start-Process -FilePath $server -ArgumentList $sArgs -PassThru -WindowStyle Hidden)
        $runs += [pscustomobject]@{ name = $name; latency = $latency; lagComp = $lagComp; dir = $dir; port = $port; procs = $procs }
        $port++
    }
}
Start-Sleep -Seconds 4
foreach ($run in $runs) {
    $sim = @('-simLatency', $run.latency, '-simJitter', $JitterMs, '-simLoss', $LossPercent)
    foreach ($bot in @(@{ n = 'Shooter'; m = 'fight' }, @{ n = 'Target'; m = 'strafe' })) {
        $cArgs = @('-batchmode', '-nographics', '-connect', '127.0.0.1', '-port', $run.port, '-name', $bot.n, '-bot', '-botMode', $bot.m, '-botSite', 0,
            '-perfLogInterval', 10, '-quitAfter', $DurationSeconds, '-logFile', (Join-Path $run.dir "$($bot.n).log")) + $sim
        $run.procs += Start-Process -FilePath $client -ArgumentList $cArgs -PassThru -WindowStyle Hidden
        Start-Sleep -Milliseconds 800
    }
}
Write-Host "Running $($runs.Count) matches for ${DurationSeconds}s..."
foreach ($run in $runs) { foreach ($p in $run.procs) { $p.WaitForExit(($DurationSeconds + 40) * 1000) | Out-Null; if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force } } }

$table = foreach ($run in $runs) {
    $log = Get-Content (Join-Path $run.dir 'server.log')
    $shots = 0; $hits = 0; $rewindSum = 0.0; $rewindN = 0
    foreach ($line in ($log | Where-Object { $_ -match '\[BH\]\[Perf\].* shots (\d+) hits (\d+)' })) {
        $null = $line -match ' shots (\d+) hits (\d+)'
        $s = [int]$Matches[1]; $h = [int]$Matches[2]
        $shots += $s; $hits += $h
        if ($line -match 'rewind avg (\d+)ms') { $rewindSum += [double]$Matches[1] * $s; $rewindN += $s }
    }
    $corr = 0
    foreach ($line in (Get-Content (Join-Path $run.dir 'Shooter.log') | Where-Object { $_ -match 'corrections (\d+)' })) {
        $null = $line -match 'corrections (\d+)'; $corr += [int]$Matches[1]
    }
    [pscustomobject]@{
        Run         = $run.name
        RttMs       = $run.latency
        LagComp     = $run.lagComp
        Shots       = $shots
        Hits        = $hits
        HitPercent  = if ($shots) { [math]::Round(100.0 * $hits / $shots, 1) } else { 0 }
        RewindAvgMs = if ($rewindN) { [math]::Round($rewindSum / $rewindN) } else { 0 }
        Kills       = ($log | Select-String '\[BH\]\[Kill\] Shooter').Count
        Corrections = $corr
        Exceptions  = ($log | Select-String 'Exception').Count
    }
}
$table | Format-Table -AutoSize | Out-String | Write-Host
$table | ConvertTo-Json | Set-Content (Join-Path $root 'results.json')
