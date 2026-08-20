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

.PARAMETER TimeoutMinutes
    Backstop for a wedged Editor. A cold IL2CPP build can legitimately take 30+
    minutes, so raise this rather than lower it.

.EXAMPLE
    ./scripts/build-android.ps1 -Version 0.1.0
#>
[CmdletBinding()]
param(
    [string]$Version,
    [switch]$Apk,
    [int]$TimeoutMinutes = 90
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
Write-Host "This takes a while on a cold Library -- IL2CPP compiles the whole game to C++."

# No -quit. If startup triggers a script recompile, -quit tears the Editor down
# before -executeMethod is invoked and Unity exits 0 having built nothing.
# BuildScript.Guarded owns the exit code instead; the timeout below is the backstop
# in case it somehow never reaches it.
$unityArgs = @(
    '-batchmode', '-nographics',
    '-projectPath', $unityProject,
    '-buildTarget', 'Android',
    '-executeMethod', $method,
    '-logFile', $logFile
)

$proc = Start-Process -FilePath $unityExe -ArgumentList $unityArgs -PassThru -NoNewWindow

# Touching .Handle forces .NET to keep the native handle open. Without it,
# Start-Process -PassThru hands back a Process whose ExitCode reads as $null once
# the process is gone, and every finished build looks like a failure.
$null = $proc.Handle

if (-not $proc.WaitForExit($TimeoutMinutes * 60 * 1000)) {
    $proc.Kill()
    throw "Unity was still running after $TimeoutMinutes minutes and was killed. Log: $logFile"
}
$unityExit = $proc.ExitCode

$ranBuild = Select-String -Path $logFile -Pattern '\[PoseBuild\]' -Quiet
if (-not $ranBuild) {
    Write-Host "--- last 30 log lines ---"
    Get-Content $logFile -Tail 30
    throw "Unity exited ($unityExit) without ever invoking $method. Usually this means the project failed to compile -- search $logFile for 'error CS'."
}

if ($null -eq $unityExit) {
    # Belt and braces: if the exit code is somehow still unreadable, the artifact
    # check below is the real proof, so warn rather than fail a good build.
    Write-Warning "Could not read Unity's exit code; relying on the build log and artifact check."
}
elseif ($unityExit -ne 0) {
    Write-Host "--- last 40 log lines ---"
    Get-Content $logFile -Tail 40
    throw "Unity exited with $unityExit. Full log: $logFile"
}

$artifactPath = Join-Path $buildsDir $artifact
if (-not (Test-Path $artifactPath)) {
    throw "Unity reported success but $artifactPath is missing. Check $logFile."
}

$sizeMb = [math]::Round((Get-Item $artifactPath).Length / 1MB, 1)
Write-Host ""
Write-Host "Done: $artifactPath ($sizeMb MB)"
Write-Host "Upload at https://play.google.com/console -> Testing -> Internal testing -> Create new release"
