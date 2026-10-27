#Requires -Version 7
<#
.SYNOPSIS
    Creates the project's release-signing key pair (ADR 0034). Run once, by the project owner.
.DESCRIPTION
    Writes an ECDSA P-256 private key (PKCS#8 PEM) to -Path, readable only by you, and prints the public key.
    - The PUBLIC key goes into the app (Core/Updates/ReleaseManifest.cs, ReleaseSigningKeys.Pinned); it is not secret.
    - The PRIVATE key signs every release manifest (tools/New-ReleaseManifest.ps1; later a GitHub Actions secret, P06-09).
      Never commit it, never paste it into an issue or chat. Keep an offline backup: if it is lost, already installed
      copies can no longer verify updates until users install a new version by hand; if it leaks, anyone could sign
      "updates" — then replace the key and publish a version with only the new public key.
.PARAMETER Path
    Where to write the private key. Defaults to your user profile; refused inside the repository.
#>
[CmdletBinding()]
param(
    [string] $Path = (Join-Path $HOME 'DynamicIsland-release-signing-key.pem')
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$full = [System.IO.Path]::GetFullPath($Path)
if ($full.StartsWith($repo, [StringComparison]::OrdinalIgnoreCase)) { throw 'Refusing to write the private key inside the repository.' }
if (Test-Path $full) { throw "$full already exists; refusing to overwrite a signing key." }

$key = [System.Security.Cryptography.ECDsa]::Create([System.Security.Cryptography.ECCurve+NamedCurves]::nistP256)
try {
    # Create the file empty, lock it to the current user, then write the key.
    New-Item -ItemType File -Path $full | Out-Null
    & icacls $full /inheritance:r /grant:r "$($env:USERDOMAIN)\$($env:USERNAME):F" | Out-Null
    if ($LASTEXITCODE -ne 0) { Remove-Item $full; throw 'Could not restrict access to the key file.' }
    Set-Content -Path $full -Value $key.ExportPkcs8PrivateKeyPem() -Encoding ascii -NoNewline
    $public = [Convert]::ToBase64String($key.ExportSubjectPublicKeyInfo())
}
finally {
    $key.Dispose()
}

Write-Host "Private key: $full (only your account can read it). Back it up offline; never commit or share it."
Write-Host ''
Write-Host 'Public key (safe to share; goes into ReleaseSigningKeys.Pinned):'
Write-Output $public
