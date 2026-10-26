# Shared helpers for the Blue Horizon tool scripts. Dot-source: . "$PSScriptRoot\Common.ps1"

$ErrorActionPreference = 'Stop'
$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

function Get-UnityVersion {
    $line = Get-Content (Join-Path $ProjectRoot 'ProjectSettings\ProjectVersion.txt') | Where-Object { $_ -like 'm_EditorVersion:*' }
    return ($line -split ':')[1].Trim()
}

# Resolves the Unity editor executable: $env:UNITY_EDITOR, then common Hub install locations.
function Get-UnityEditor {
    if ($env:UNITY_EDITOR -and (Test-Path $env:UNITY_EDITOR)) { return $env:UNITY_EDITOR }
    $version = Get-UnityVersion
    $candidates = @(
        "C:\Program Files\Unity\Hub\Editor\$version\Editor\Unity.exe",
        "D:\Unity\$version\Editor\Unity.exe",
        "E:\Unity\$version\Editor\Unity.exe",
        "F:\Unity\$version\Editor\Unity.exe"
    )
    $hubConfig = Join-Path $env:APPDATA 'UnityHub\secondaryInstallPath.json'
    if (Test-Path $hubConfig) {
        $secondary = Get-Content $hubConfig -Raw | ConvertFrom-Json
        if ($secondary) { $candidates = @((Join-Path $secondary "$version\Editor\Unity.exe")) + $candidates }
    }
    foreach ($c in $candidates) { if (Test-Path $c) { return $c } }
    throw "Unity $version not found. Install it via Unity Hub or set `$env:UNITY_EDITOR to Unity.exe."
}

function Assert-ProjectClosed {
    if (Test-Path (Join-Path $ProjectRoot 'Temp\UnityLockfile')) {
        try {
            $stream = [System.IO.File]::Open((Join-Path $ProjectRoot 'Temp\UnityLockfile'), 'Open', 'ReadWrite', 'None')
            $stream.Close()
        } catch {
            throw 'The project is open in the Unity Editor. Close it before running batch-mode tools.'
        }
    }
}

# Runs Unity in batch mode and returns its exit code. Extra arguments are passed through.
function Invoke-UnityBatch {
    param([string[]]$Arguments, [string]$LogFile)
    Assert-ProjectClosed
    $unity = Get-UnityEditor
    $all = @('-batchmode', '-nographics', '-projectPath', $ProjectRoot, '-logFile', $LogFile) + $Arguments
    Write-Host "> $unity $($all -join ' ')"
    $p = Start-Process -FilePath $unity -ArgumentList $all -Wait -PassThru -NoNewWindow
    return $p.ExitCode
}
