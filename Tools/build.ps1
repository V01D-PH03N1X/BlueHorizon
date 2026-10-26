<#
.SYNOPSIS
  Generates project content (optional) and builds the Windows client and dedicated server.
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File Tools\build.ps1
  powershell -ExecutionPolicy Bypass -File Tools\build.ps1 -Generate -Development
#>
param(
    [switch]$Generate,
    [switch]$Development,
    [string]$Output = 'Builds',
    [ValidateSet('All', 'Client', 'Server', 'LinuxServer')][string]$Target = 'All'
)
. "$PSScriptRoot\Common.ps1"

$logs = Join-Path $ProjectRoot 'Logs'
New-Item -ItemType Directory -Force $logs | Out-Null

if ($Generate) {
    $code = Invoke-UnityBatch -LogFile (Join-Path $logs 'generate.log') -Arguments @('-quit', '-executeMethod', 'BlueHorizon.EditorTools.ProjectSetup.GenerateAll')
    if ($code -ne 0) { throw "Content generation failed ($code). See Logs\generate.log" }
}

$method = switch ($Target) {
    'Client' { 'BuildClient' }
    'Server' { 'BuildServer' }
    'LinuxServer' { 'BuildLinuxServer' }
    default { 'BuildAll' }
}
$unityArgs = @('-quit', '-executeMethod', "BlueHorizon.EditorTools.BuildTools.$method", '-buildOutput', (Join-Path $ProjectRoot $Output))
if ($Development) { $unityArgs += '-development' }
$code = Invoke-UnityBatch -LogFile (Join-Path $logs 'build.log') -Arguments $unityArgs
if ($code -ne 0) { throw "Build failed ($code). See Logs\build.log" }
Write-Host "Build succeeded -> $Output" -ForegroundColor Green
