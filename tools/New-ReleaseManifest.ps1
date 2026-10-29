#Requires -Version 7
<#
.SYNOPSIS
    Writes and signs the release manifest (ADR 0034): DynamicIsland-release.json + DynamicIsland-release.json.sig.
.DESCRIPTION
    The manifest lists each release file's name, size and SHA-256; the signature (ECDSA P-256, SHA-256, IEEE P1363,
    Base64) covers its exact bytes. Upload both next to the files on the GitHub release. The app downloads an update
    only when the signature verifies against its pinned public key and the file matches the manifest.
.PARAMETER Version
    The release version (SemVer, no "v"); must match the release tag.
.PARAMETER Files
    The release files, e.g. artifacts/DynamicIsland-Setup-win-x64.exe and artifacts/DynamicIsland-Portable-win-x64.zip.
.PARAMETER KeyPath
    The private key file from New-ReleaseSigningKey.ps1. Only a file on the owner's PC: the key is never an
    environment variable or a CI secret (ADR 0038, P07-01).
.PARAMETER PublicKey
    Optional: the pinned public key (Base64); the script then refuses a private key that does not match it.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Version,
    [Parameter(Mandatory)] [string[]] $Files,
    [Parameter(Mandatory)] [string] $KeyPath,
    [string] $PublicKey,
    [string] $Output = (Join-Path $PSScriptRoot '..' 'artifacts')
)

$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^(0|[1-9]\d{0,8})\.(0|[1-9]\d{0,8})\.(0|[1-9]\d{0,8})$') { throw "Version '$Version' is not a stable SemVer version." }

$pem = Get-Content -Raw $KeyPath
if (-not $pem) { throw "The key file '$KeyPath' is empty." }

$entries = foreach ($file in $Files) {
    $item = Get-Item $file
    if ($item.Name -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$') { throw "Unsupported file name '$($item.Name)'." }
    [ordered]@{
        name   = $item.Name
        size   = $item.Length
        sha256 = (Get-FileHash $item.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    }
}

$manifest = [ordered]@{ schemaVersion = 1; product = 'DynamicIsland'; version = $Version; files = @($entries) } | ConvertTo-Json -Depth 4 -Compress
$bytes = [System.Text.UTF8Encoding]::new($false).GetBytes($manifest)

$key = [System.Security.Cryptography.ECDsa]::Create()
try {
    $key.ImportFromPem($pem)
    if ($key.KeySize -ne 256) { throw 'The signing key is not ECDSA P-256.' }
    $derived = [Convert]::ToBase64String($key.ExportSubjectPublicKeyInfo())
    if ($PublicKey -and $derived -ne $PublicKey) { throw 'The private key does not match the pinned public key.' }
    $format = [System.Security.Cryptography.DSASignatureFormat]::IeeeP1363FixedFieldConcatenation
    $signature = $key.SignData($bytes, [System.Security.Cryptography.HashAlgorithmName]::SHA256, $format)
    if (-not $key.VerifyData($bytes, $signature, [System.Security.Cryptography.HashAlgorithmName]::SHA256, $format)) { throw 'Self-check of the signature failed.' }
}
finally {
    $key.Dispose()
}

New-Item -ItemType Directory -Force -Path $Output | Out-Null
$manifestPath = Join-Path $Output 'DynamicIsland-release.json'
[System.IO.File]::WriteAllBytes($manifestPath, $bytes)
Set-Content -Path "$manifestPath.sig" -Value ([Convert]::ToBase64String($signature)) -Encoding ascii -NoNewline
Write-Host "Signed manifest for $Version ($(@($entries).Count) files, public key $derived):"
Write-Host "  $manifestPath"
Write-Host "  $manifestPath.sig"
