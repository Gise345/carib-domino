<#
.SYNOPSIS
    Prints the SHA-1, SHA-256 and Facebook key hash for a signing certificate.

.DESCRIPTION
    Firebase wants SHA-1 and SHA-256; Facebook wants the base64 of the raw SHA-1
    digest. Both must be registered for the Play *app signing* certificate as well
    as the upload key, or login works on a sideloaded build and fails for every
    tester who installs from Play.

    Point this at deployment_cert.der, downloaded from
    Play Console -> Test and release -> Setup -> App signing.

    With no arguments it reads the local upload key instead, so you can compare.

.PARAMETER Path
    A .der or .pem certificate. Omit to read the upload keystore.

.EXAMPLE
    ./scripts/cert-fingerprints.ps1 ~/Downloads/deployment_cert.der

.EXAMPLE
    ./scripts/cert-fingerprints.ps1
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [string]$Path
)

$ErrorActionPreference = 'Stop'

function Show-Fingerprints {
    param(
        [byte[]]$RawCertificate,
        [string]$Label
    )

    $sha1 = [System.Security.Cryptography.SHA1]::Create().ComputeHash($RawCertificate)
    $sha256 = [System.Security.Cryptography.SHA256]::Create().ComputeHash($RawCertificate)

    Write-Host ""
    Write-Host $Label -ForegroundColor Cyan
    Write-Host ("  SHA-1   " + (($sha1 | ForEach-Object { $_.ToString('X2') }) -join ':'))
    Write-Host ("  SHA-256 " + (($sha256 | ForEach-Object { $_.ToString('X2') }) -join ':'))
    Write-Host ("  Facebook key hash  " + [Convert]::ToBase64String($sha1))
}

if ($Path) {
    if (-not (Test-Path $Path)) { throw "No certificate at $Path" }
    $cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2 ((Resolve-Path $Path).Path)
    Show-Fingerprints -RawCertificate $cert.RawData -Label "$Path`n  Subject: $($cert.Subject)"
    Write-Host ""
    Write-Host "Register these in Firebase (Project settings -> Android app -> Add fingerprint)"
    Write-Host "and in the Meta app (Settings -> Basic -> Android -> Key Hashes)."
    return
}

# --- No path given: read the upload key out of the local keystore -----------
$configDir = Join-Path $env:USERPROFILE '.config\invovibe'
$signingEnv = Join-Path $configDir 'android-signing.env'
if (-not (Test-Path $signingEnv)) {
    throw "Missing $signingEnv. Pass a certificate path instead, or see docs/SETUP/store-releases.md."
}
foreach ($line in Get-Content $signingEnv) {
    if ($line -match '^\s*([A-Z0-9_]+)\s*=\s*(.*)$') {
        Set-Item -Path "env:$($Matches[1])" -Value $Matches[2]
    }
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$versionLine = Get-Content (Join-Path $repoRoot 'unity\ProjectSettings\ProjectVersion.txt') -TotalCount 1
$editorVersion = ($versionLine -split ':\s*')[1].Trim()
$keytool = "C:\Program Files\Unity\Hub\Editor\$editorVersion\Editor\Data\PlaybackEngines\AndroidPlayer\OpenJDK\bin\keytool.exe"
if (-not (Test-Path $keytool)) {
    throw "keytool not found at $keytool. Install Android Build Support for Unity $editorVersion."
}

$derPath = Join-Path $env:TEMP 'pose-upload-cert.der'
& $keytool -exportcert -alias $env:POSE_KEY_ALIAS -keystore $env:POSE_KEYSTORE_PATH -storepass $env:POSE_KEYSTORE_PASS -file $derPath
if ($LASTEXITCODE -ne 0) { throw "keytool failed with $LASTEXITCODE" }

try {
    $cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2 $derPath
    Show-Fingerprints -RawCertificate $cert.RawData -Label "Upload key ($($env:POSE_KEY_ALIAS))"
}
finally {
    Remove-Item $derPath -ErrorAction SilentlyContinue
}

Write-Host ""
Write-Host "This is the UPLOAD key. The Play APP SIGNING certificate is different --"
Write-Host "download deployment_cert.der from Play Console and re-run with its path."
