<#
.SYNOPSIS
  Runs the EditMode test suite headless and prints a summary. Results: Logs\editmode-results.xml
#>
param([string]$Filter = '')
. "$PSScriptRoot\Common.ps1"

$logs = Join-Path $ProjectRoot 'Logs'
New-Item -ItemType Directory -Force $logs | Out-Null
$results = Join-Path $logs 'editmode-results.xml'
if (Test-Path $results) { Remove-Item $results }

$unityArgs = @('-runTests', '-testPlatform', 'EditMode', '-testResults', $results)
if ($Filter) { $unityArgs += @('-testFilter', $Filter) }
$code = Invoke-UnityBatch -LogFile (Join-Path $logs 'editmode-tests.log') -Arguments $unityArgs

if (-not (Test-Path $results)) { throw "No test results produced (exit $code). See Logs\editmode-tests.log" }
[xml]$xml = Get-Content $results
$run = $xml.'test-run'
Write-Host ("EditMode: {0} total, {1} passed, {2} failed, {3} skipped" -f $run.total, $run.passed, $run.failed, $run.skipped)
foreach ($case in $xml.SelectNodes('//test-case[@result="Failed"]')) {
    Write-Host "  FAILED $($case.fullname)" -ForegroundColor Red
    Write-Host "    $($case.failure.message.'#cdata-section')"
}
if ($run.failed -ne '0' -or $code -ne 0) { exit 1 }
