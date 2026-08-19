<#
.SYNOPSIS
    Produces a signed Android artifact for Google Play internal testing.

.DESCRIPTION
    Loads the upload-key credentials from ~/.config/invovibe/android-signing.env
    (never the repo), allocates a monotonically increasing versionCode, and runs
    Unity in batch mode against Pose.Build.BuildScript.

    Google Play rejects a versionCode it has seen before, so the counter in
    ~/.config/invovibe/build-number is shared by every local build and is only
    ever incremented.

.PARAMETER Version
    Marketing version, e.g. "0.1.0". Defaults to bundleVersion in ProjectSettings.

.PARAMETER Apk
    Build a sideloadable APK instead of the AAB that Play requires.

.EXAMPLE
    ./scripts/build-android.ps1 -Version 0.1.0
#>
[CmdletBinding()]
param(
    [string]$Version,
    [switch]$Apk
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$unityProject = Join-Path $repoRoot 'unity'
$configDir = Join-Path $env:USERPROFILE '.config\invovibe'
$signingEnv = Join-Path $configDir 'android-signing.env'
$counterFile = Join-Path $configDir 'build-number'

# --- Unity must not be holding the project lock -----------------------------
$lockFile = Join-Path $unityProject 'Temp\UnityLockfile'
if (Test-Path $lockFile) {
    throw "Unity has $unityProject open (Temp\UnityLockfile exists). Close the Editor and retry -- batch mode cannot open a locked project."
}

# --- Resolve the Editor matching ProjectVersion.txt -------------------------
$versionLine = Get-Content (Join-Path $unityProject 'ProjectSettings\ProjectVersion.txt') -TotalCount 1
$editorVersion = ($versionLine -split ':\s*')[1].Trim()
$unityExe = "C:\Program Files\Unity\Hub\Editor\$editorVersion\Editor\Unity.exe"
if (-not (Test-Path $unityExe)) {
    throw "Unity $editorVersion is not installed at $unityExe. Install it from Unity Hub."
}

# --- Signing credentials ----------------------------------------------------
if (-not (Test-Path $signingEnv)) {
    throw "Missing $signingEnv. See docs/SETUP/store-releases.md -- an unsigned build cannot be uploaded to Play."
}
foreach ($line in Get-Content $signingEnv) {
    if ($line -match '^\s*([A-Z0-9_]+)\s*=\s*(.*)$') {
        Set-Item -Path "env:$($Matches[1])" -Value $Matches[2]
    }
}
if (-not (Test-Path $env:POSE_KEYSTORE_PATH)) {
    throw "Keystore not found at $($env:POSE_KEYSTORE_PATH). Restore it from your backup -- it is the only key Play will accept for this app."
}

# --- Version + build number -------------------------------------------------
if (-not $Version) {
    $settings = Get-Content (Join-Path $unityProject 'ProjectSettings\ProjectSettings.asset')
    $Version = (($settings | Select-String -Pattern '^\s*bundleVersion:\s*(.+)$' | Select-Object -First 1).Matches[0].Groups[1].Value).Trim()
}

$buildNumber = 1
if (Test-Path $counterFile) {
    $buildNumber = [int](Get-Content $counterFile -TotalCount 1) + 1
}
Set-Content -Path $counterFile -Value $buildNumber -Encoding utf8

$env:POSE_BUILD_VERSION = $Version
$env:POSE_BUILD_NUMBER = $buildNumber

# --- Build ------------------------------------------------------------------
if ($Apk) { $method = 'Pose.Build.BuildScript.BuildAndroidApk' } else { $method = 'Pose.Build.BuildScript.BuildAndroidAab' }
if ($Apk) { $artifact = 'pose.apk' } else { $artifact = 'pose.aab' }

$buildsDir = Join-Path $unityProject 'Builds'
$logFile = Join-Path $buildsDir 'android-build.log'
New-Item -ItemType Directory -Force -Path $buildsDir | Out-Null

Write-Host "Building $artifact  version $Version  versionCode $buildNumber"
Write-Host "Log: $logFile"

& $unityExe -batchmode -nographics -quit `
    -projectPath $unityProject `
    -buildTarget Android `
    -executeMethod $method `
    -logFile $logFile

if ($LASTEXITCODE -ne 0) {
    Write-Host "--- last 40 log lines ---"
    Get-Content $logFile -Tail 40
    throw "Unity exited with $LASTEXITCODE. Full log: $logFile"
}

$artifactPath = Join-Path $buildsDir $artifact
if (-not (Test-Path $artifactPath)) {
    throw "Unity reported success but $artifactPath is missing. Check $logFile."
}

$sizeMb = [math]::Round((Get-Item $artifactPath).Length / 1MB, 1)
Write-Host ""
Write-Host "Done: $artifactPath ($sizeMb MB)"
Write-Host "Upload at https://play.google.com/console -> Testing -> Internal testing -> Create new release"
