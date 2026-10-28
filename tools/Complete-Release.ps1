#Requires -Version 7
<#
.SYNOPSIS
    Owner step after the Release workflow (P06-09, ADR 0038): signs the manifest for a draft release and publishes it.
.DESCRIPTION
    Run this on your own PC, where the private signing key lives (SG1). The key never goes to GitHub.
    The script:
      1. checks the release is a stable draft;
      2. downloads its portable ZIP and setup EXE to a new temporary folder;
      3. checks each file against its .sha256 and against the build-provenance attestation, so it came from this
         repository's Release workflow;
      4. checks your private key matches a key pinned in ReleaseSigningKeys (ADR 0034);
      5. signs DynamicIsland-release.json and uploads it with its .sig;
      6. with -Publish only, publishes the release as "latest", which makes the update visible to the app.
    Pre-releases (vX.Y.Z-beta.N) are never offered by the app; publish them from the GitHub page instead.
.PARAMETER Tag
    The release tag, e.g. v1.0.0.
.PARAMETER KeyPath
    The private key file from tools\New-ReleaseSigningKey.ps1.
.PARAMETER Publish
    Also publish the draft. Without it, check the uploaded manifest on GitHub first and publish there.
.EXAMPLE
    pwsh tools\Complete-Release.ps1 -Tag v1.0.0 -KeyPath D:\keys\dynamicisland-release-signing.pem -Publish
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Tag,
    [Parameter(Mandatory)] [string] $KeyPath,
    [switch] $Publish,
    [string] $Repo = 'imamulkadir/DyanamicEye'
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$assets = 'DynamicIsland-Portable-win-x64.zip', 'DynamicIsland-Setup-win-x64.exe'

if ($Tag -notmatch '^v((0|[1-9]\d{0,8})\.(0|[1-9]\d{0,8})\.(0|[1-9]\d{0,8}))$') {
    throw "Tag '$Tag' is not a stable v<X.Y.Z> tag. Pre-releases are published from the GitHub page and carry no manifest."
}
$version = $Matches[1]
$gh = (Get-Command gh -ErrorAction SilentlyContinue)?.Source ?? (Join-Path $env:ProgramFiles 'GitHub CLI' 'gh.exe')
if (-not (Test-Path $gh)) { throw 'GitHub CLI (gh) not found. Install it and run "gh auth login".' }

function Invoke-Gh {
    & $gh @args
    if ($LASTEXITCODE -ne 0) { throw "gh $($args -join ' ') failed." }
}

# 1. A stable draft created by the Release workflow.
$release = Invoke-Gh release view $Tag --repo $Repo --json 'isDraft,isPrerelease' | ConvertFrom-Json
if (-not $release.isDraft) { throw "$Tag is already published. Create a new version instead of re-signing a published release." }
if ($release.isPrerelease) { throw "$Tag is a pre-release; it carries no manifest." }

# 4 (checked early, before downloading): the private key must match a pinned public key.
$source = Get-Content -Raw (Join-Path $root 'src' 'DynamicIsland.Core' 'Updates' 'ReleaseManifest.cs')
if ($source -notmatch 'Pinned\s*\{\s*get;\s*\}\s*=\s*\[(?<list>[^\]]*)\]') { throw 'ReleaseSigningKeys.Pinned was not found.' }
$pinned = [regex]::Matches($Matches['list'], '"([A-Za-z0-9+/=]+)"') | ForEach-Object { $_.Groups[1].Value }
if (-not $pinned) { throw 'No public key is pinned yet (SG1). Pin it in ReleaseSigningKeys and ship that build first.' }
$key = [System.Security.Cryptography.ECDsa]::Create()
try {
    $key.ImportFromPem((Get-Content -Raw $KeyPath))
    $publicKey = [Convert]::ToBase64String($key.ExportSubjectPublicKeyInfo())
}
finally {
    $key.Dispose()
}
if ($publicKey -notin $pinned) { throw 'This private key does not match any pinned public key.' }

# 2–3. Download into a new empty folder and check hashes and provenance.
$work = Join-Path ([System.IO.Path]::GetTempPath()) "DynamicIsland-release-$version-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $work | Out-Null
try {
    foreach ($name in $assets) {
        Invoke-Gh release download $Tag --repo $Repo --dir $work --pattern $name --pattern "$name.sha256"
        $file = Join-Path $work $name
        $expected = ((Get-Content -Raw "$file.sha256").Trim() -split '\s+')[0].ToLowerInvariant()
        $actual = (Get-FileHash $file -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($expected -ne $actual) { throw "$name does not match its .sha256 file." }
        Invoke-Gh attestation verify $file --repo $Repo | Out-Null
        Write-Host "Checked $name (SHA-256 $actual, provenance OK)"
    }

    # 5. Sign exactly the files that were downloaded, then upload the manifest and its signature.
    & (Join-Path $PSScriptRoot 'New-ReleaseManifest.ps1') -Version $version -Files ($assets | ForEach-Object { Join-Path $work $_ }) `
        -KeyPath $KeyPath -PublicKey $publicKey -Output $work
    Invoke-Gh release upload $Tag --repo $Repo --clobber (Join-Path $work 'DynamicIsland-release.json') (Join-Path $work 'DynamicIsland-release.json.sig')
    Write-Host "Uploaded the signed manifest to the draft $Tag."

    # 6. Publishing makes the update visible to every copy that checks for updates.
    if ($Publish) {
        Invoke-Gh release edit $Tag --repo $Repo --draft=false --latest
        Write-Host "Published $Tag as the latest release."
    }
    else {
        Write-Host "Not published. Review the draft on GitHub, then publish it there (or re-run with -Publish)."
    }
}
finally {
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
}
