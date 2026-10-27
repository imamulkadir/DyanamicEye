#Requires -Version 7
<#
.SYNOPSIS
    Builds the release layout (ADR 0030) and the portable ZIP for win-x64.
.DESCRIPTION
    artifacts/layout/
      DynamicIsland.exe            NativeAOT launcher: the stable launch path (shortcuts, startup entry, restarts)
      current.json                 { "current": "<version>", "previous": null }
      app-<version>/               self-contained app payload, never changed after install
    artifacts/DynamicIsland-Portable-win-x64.zip (+ .sha256)

    The app payload is self-contained (no .NET install needed, no admin rights). The launcher needs the C++ build
    tools to publish (NativeAOT). Data never lives in the layout: settings, notes and logs stay in
    %LOCALAPPDATA%\DynamicIsland, for the portable and the installed version alike.
.PARAMETER Version
    Payload version (SemVer). Defaults to <Version> in Directory.Build.props.
.PARAMETER Installer
    Also build the per-user installer artifacts/DynamicIsland-Setup-win-x64.exe (+ .sha256) with Inno Setup 6
    (ADR 0031). Needs ISCC.exe (Inno Setup 6 installed, or -IsccPath).
#>
[CmdletBinding()]
param(
    [string] $Version,
    [string] $Output = (Join-Path $PSScriptRoot '..' 'artifacts'),
    [switch] $Installer,
    [string] $IsccPath
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')

if (-not $Version) {
    $Version = ([xml](Get-Content (Join-Path $root 'Directory.Build.props'))).Project.PropertyGroup.Version | Select-Object -First 1
}
if ($Version -notmatch '^(0|[1-9]\d{0,8})\.(0|[1-9]\d{0,8})\.(0|[1-9]\d{0,8})(-[0-9A-Za-z]+(\.[0-9A-Za-z]+)*)?$') {
    throw "Version '$Version' is not SemVer."
}

# The NativeAOT linker is found through vswhere, which is not always on PATH.
$vsInstaller = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio' 'Installer'
if ((Test-Path $vsInstaller) -and ($env:PATH -notlike "*$vsInstaller*")) { $env:PATH = "$env:PATH;$vsInstaller" }

$layout = Join-Path $Output 'layout'
$payload = Join-Path $layout "app-$Version"
$launcherOut = Join-Path $Output 'launcher'
foreach ($folder in $layout, $launcherOut) {
    if (Test-Path $folder) { Remove-Item $folder -Recurse -Force }
}

Write-Host "Publishing app $Version (self-contained win-x64)..."
dotnet publish (Join-Path $root 'src' 'DynamicIsland.App') -c Release -r win-x64 --self-contained true `
    -p:Version=$Version -p:DebugType=none -o $payload
if ($LASTEXITCODE -ne 0) { throw 'App publish failed.' }
# Our API doc files are for developers only.
Get-ChildItem $payload -Filter 'DynamicIsland*.xml' | Remove-Item

Write-Host 'Publishing launcher (NativeAOT)...'
dotnet publish (Join-Path $root 'src' 'DynamicIsland.Launcher') -c Release -r win-x64 `
    -p:Version=$Version -p:DebugType=none -o $launcherOut
if ($LASTEXITCODE -ne 0) { throw 'Launcher publish failed.' }
Copy-Item (Join-Path $launcherOut 'DynamicIsland.Launcher.exe') (Join-Path $layout 'DynamicIsland.exe')
Remove-Item $launcherOut -Recurse -Force

# License texts travel with every package (THIRD_PARTY_NOTICES.md).
Copy-Item (Join-Path $root 'LICENSE') (Join-Path $layout 'LICENSE.txt')
Copy-Item (Join-Path $root 'THIRD_PARTY_NOTICES.md') $layout

# Same shape as AppLayout.SerializePointer.
[ordered]@{ current = $Version; previous = $null } | ConvertTo-Json | Set-Content (Join-Path $layout 'current.json') -Encoding utf8NoBOM

$zip = Join-Path $Output 'DynamicIsland-Portable-win-x64.zip'
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $layout '*') -DestinationPath $zip -CompressionLevel Optimal
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $(Split-Path $zip -Leaf)" | Set-Content "$zip.sha256" -Encoding ascii

$payloadMb = [math]::Round(((Get-ChildItem $payload -Recurse -File | Measure-Object Length -Sum).Sum) / 1MB, 1)
$launcherMb = [math]::Round((Get-Item (Join-Path $layout 'DynamicIsland.exe')).Length / 1MB, 1)
$zipMb = [math]::Round((Get-Item $zip).Length / 1MB, 1)
Write-Host "Layout: $layout (launcher $launcherMb MB, payload $payloadMb MB)"
Write-Host "Portable ZIP: $zip ($zipMb MB, SHA-256 $hash)"

if ($Installer) {
    if (-not $IsccPath) {
        $IsccPath = @(
            (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6' 'ISCC.exe'),
            (Join-Path $env:ProgramFiles 'Inno Setup 6' 'ISCC.exe'),
            (Join-Path $env:LOCALAPPDATA 'Programs' 'Inno Setup 6' 'ISCC.exe')
        ) | Where-Object { Test-Path $_ } | Select-Object -First 1
    }
    if (-not $IsccPath) { throw 'Inno Setup 6 (ISCC.exe) not found; install it or pass -IsccPath.' }

    Write-Host "Building installer with $IsccPath..."
    $script = Join-Path $root 'packaging' 'installer' 'DynamicIsland.iss'
    & $IsccPath /Q "/DAppVersion=$Version" "/DLayoutDir=$((Resolve-Path $layout).Path)" "/DOutputDir=$((Resolve-Path $Output).Path)" $script
    if ($LASTEXITCODE -ne 0) { throw 'Installer build failed.' }

    $setup = Join-Path $Output 'DynamicIsland-Setup-win-x64.exe'
    $setupHash = (Get-FileHash $setup -Algorithm SHA256).Hash.ToLowerInvariant()
    "$setupHash  $(Split-Path $setup -Leaf)" | Set-Content "$setup.sha256" -Encoding ascii
    Write-Host "Installer: $setup ($([math]::Round((Get-Item $setup).Length / 1MB, 1)) MB, SHA-256 $setupHash)"
}
