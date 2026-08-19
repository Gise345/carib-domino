<#
.SYNOPSIS
    Produces the .alf activation request needed to get a Unity Personal .ulf licence
    file for CI.

.DESCRIPTION
    Unity Personal has no serial number, so CI cannot activate the way a Plus/Pro
    seat does. The workaround is a manually activated licence file:

      1. This script produces an .alf activation request.
      2. You upload it to https://license.unity3d.com/manual and get a .ulf back.
      3. You base64 the .ulf into the UNITY_LICENSE variable in Codemagic's
         'unity' environment group.

    The Unity Editor must be closed -- batch mode cannot run while it holds a lock.

    Caveat worth knowing before you spend time on this: a Personal .ulf is issued
    against the machine that generated the .alf, and using it on a cloud runner is
    unofficial. It may simply not activate. See docs/SETUP/store-releases.md for
    the fallbacks.

.EXAMPLE
    ./scripts/unity-request-license.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$unityProject = Join-Path $repoRoot 'unity'

$versionLine = Get-Content (Join-Path $unityProject 'ProjectSettings\ProjectVersion.txt') -TotalCount 1
$editorVersion = ($versionLine -split ':\s*')[1].Trim()
$unityExe = "C:\Program Files\Unity\Hub\Editor\$editorVersion\Editor\Unity.exe"
if (-not (Test-Path $unityExe)) {
    throw "Unity $editorVersion is not installed at $unityExe."
}

$outDir = Join-Path $repoRoot 'unity\Builds'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$logFile = Join-Path $outDir 'unity-license-request.log'

Push-Location $outDir
try {
    Write-Host "Requesting an activation file for Unity $editorVersion..."
    & $unityExe -batchmode -nographics -quit -logFile $logFile -createManualActivationFile
}
finally {
    Pop-Location
}

$alf = Get-ChildItem -Path $outDir -Filter '*.alf' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $alf) {
    Write-Host "--- last 30 log lines ---"
    Get-Content $logFile -Tail 30
    throw "Unity produced no .alf file. Full log: $logFile"
}

Write-Host ""
Write-Host "Activation request: $($alf.FullName)"
Write-Host ""
Write-Host "Next:"
Write-Host "  1. Open https://license.unity3d.com/manual and upload that .alf"
Write-Host "  2. Choose Unity Personal, download the .ulf it returns"
Write-Host "  3. Turn it into the UNITY_LICENSE value:"
Write-Host ""
Write-Host '     [Convert]::ToBase64String([IO.File]::ReadAllBytes("path\to\Unity_v6000.ulf")) | Set-Clipboard'
Write-Host ""
Write-Host "  4. Paste it into Codemagic -> Environment variables -> group 'unity', marked Secure"
