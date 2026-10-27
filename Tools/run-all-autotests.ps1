<#
.SYNOPSIS
  Runs every automated multiplayer scenario in parallel (separate ports) and prints a pass/fail table.
  Results per scenario: Logs\autotest-<scenario>\result.json
#>
param([int]$TimeoutSeconds = 240)
. "$PSScriptRoot\Common.ps1"

$scenarios = [ordered]@{ fight = 7810; passive = 7811; explode = 7812; idle = 7813; disconnect = 7814; reconnect = 7815; duel = 7816; deathmatch = 7817; capture = 7818; weapongame = 7819 }
$jobs = @()
foreach ($s in $scenarios.Keys) {
    $jobs += Start-Process -FilePath 'powershell' -PassThru -WindowStyle Hidden -ArgumentList @(
        '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $PSScriptRoot 'autotest-match.ps1'),
        '-Scenario', $s, '-Port', $scenarios[$s], '-TimeoutSeconds', $TimeoutSeconds)
}
foreach ($j in $jobs) { $j.WaitForExit() }

$failed = 0
foreach ($s in $scenarios.Keys) {
    $resultFile = Join-Path $ProjectRoot "Logs\autotest-$s\result.json"
    if (Test-Path $resultFile) {
        $r = Get-Content $resultFile -Raw | ConvertFrom-Json
        $status = if ($r.passed) { 'PASS' } else { $failed++; "FAIL ($($r.failedChecks) checks)" }
    } else {
        $failed++
        $status = 'FAIL (no result)'
    }
    Write-Host ("{0,-11} {1}" -f $s, $status)
}
if ($failed -gt 0) { exit 1 }
