#Requires -Version 7
<#
.SYNOPSIS
    Prints the CHANGELOG.md section for one version, for the GitHub release notes (P06-09, ADR 0038).
.DESCRIPTION
    Finds the heading "## [<Version>]" (optionally followed by " - <date>") and returns everything up to the next
    "## " heading, trimmed. Fails when the section is missing or empty, so a release cannot go out without notes.
.PARAMETER Version
    SemVer without the leading "v", e.g. 1.0.0 or 1.1.0-beta.1.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Version,
    [string] $Path = (Join-Path $PSScriptRoot '..' 'CHANGELOG.md')
)

$ErrorActionPreference = 'Stop'
$lines = Get-Content -LiteralPath $Path
$heading = '^## \[' + [regex]::Escape($Version) + '\](\s+-\s+\d{4}-\d{2}-\d{2})?\s*$'
$start = -1
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match $heading) { $start = $i + 1; break }
}
if ($start -lt 0) { throw "CHANGELOG.md has no section '## [$Version]'." }

$end = $lines.Count
for ($i = $start; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match '^## ') { $end = $i; break }
}

$notes = ($lines[$start..($end - 1)] -join "`n").Trim()
if (-not $notes) { throw "The CHANGELOG.md section for $Version is empty." }
$notes
