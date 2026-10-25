#Requires -Version 7
<#
.SYNOPSIS
    Regenerates assets/DynamicIsland.ico: the original Dynamic Island app icon (no third-party artwork).
.DESCRIPTION
    Draws a light capsule with a green status dot on a dark rounded square at every size Windows uses for
    high-DPI shells (16-256 px) and packs them as PNG-compressed entries in one .ico file.
    Windows only (System.Drawing). Output is committed; rerun only when the design changes.
#>
[CmdletBinding()]
param(
    [string] $OutputPath = (Join-Path $PSScriptRoot '..' 'assets' 'DynamicIsland.ico')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$sizes = 16, 20, 24, 32, 40, 48, 64, 256

function New-RoundedPath([float] $x, [float] $y, [float] $w, [float] $h, [float] $r) {
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $d = 2 * $r
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-IconPng([int] $size) {
    $bitmap = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $g.Clear([System.Drawing.Color]::Transparent)

        # Dark rounded-square tile.
        $tile = New-RoundedPath 0 0 $size $size ($size * 0.22)
        $g.FillPath([System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 0x11, 0x12, 0x14)), $tile)

        # Island capsule, slightly above center like the real island at the top of the screen.
        $capW = $size * 0.72; $capH = [Math]::Max($size * 0.28, 4)
        $capX = ($size - $capW) / 2; $capY = $size * 0.30
        $capsule = New-RoundedPath $capX $capY $capW $capH ($capH / 2)
        $g.FillPath([System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 0xF2, 0xF3, 0xF5)), $capsule)

        # Green status dot centered in the capsule, as on the island (omitted where it would be a blur).
        if ($size -ge 20) {
            $dot = $capH * 0.45
            $g.FillEllipse([System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 0x1F, 0xB8, 0x66)),
                [float]($capX + ($capW - $dot) / 2), [float]($capY + ($capH - $dot) / 2), [float]$dot, [float]$dot)
        }
    }
    finally {
        $g.Dispose()
    }

    $stream = [System.IO.MemoryStream]::new()
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    # Leading comma: return the byte[] as one object instead of unrolling it into the pipeline.
    return , $stream.ToArray()
}

$images = [System.Collections.Generic.List[byte[]]]::new()
foreach ($size in $sizes) { $images.Add([byte[]](New-IconPng $size)) }

$out = [System.IO.MemoryStream]::new()
$writer = [System.IO.BinaryWriter]::new($out)
$writer.Write([uint16]0)              # reserved
$writer.Write([uint16]1)              # type: icon
$writer.Write([uint16]$sizes.Count)

$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dimension = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $writer.Write([byte]$dimension)   # width (0 = 256)
    $writer.Write([byte]$dimension)   # height
    $writer.Write([byte]0)            # palette colors
    $writer.Write([byte]0)            # reserved
    $writer.Write([uint16]1)          # color planes
    $writer.Write([uint16]32)         # bits per pixel
    $writer.Write([uint32]$images[$i].Length)
    $writer.Write([uint32]$offset)
    $offset += $images[$i].Length
}

foreach ($image in $images) { $writer.Write($image) }
$writer.Flush()

$directory = Split-Path -Parent $OutputPath
New-Item -ItemType Directory -Force -Path $directory | Out-Null
[System.IO.File]::WriteAllBytes((Resolve-Path $directory | Join-Path -ChildPath (Split-Path -Leaf $OutputPath)), $out.ToArray())
Write-Output "Wrote $OutputPath ($($sizes -join ', ') px)"
