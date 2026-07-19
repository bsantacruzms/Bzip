#requires -Version 5.1
<#
  Generates the BoltZip app icon: an amber lightning bolt on a dark rounded square.
  Produces a multi-resolution .ico plus PNGs for Avalonia and the website.
#>
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root = Split-Path $PSScriptRoot -Parent
$assets = Join-Path $root 'src\BoltZip.App\Assets'
$docsAssets = Join-Path $root 'docs\assets'
New-Item -ItemType Directory -Force -Path $assets, $docsAssets | Out-Null

# Lightning-bolt polygon in normalized (0..1) coordinates.
$bolt = @(
    @(0.575, 0.09), @(0.315, 0.53), @(0.485, 0.53),
    @(0.415, 0.91), @(0.705, 0.45), @(0.525, 0.45)
)

function New-Point([int]$px, [int]$py) {
    return New-Object System.Drawing.Point -ArgumentList $px, $py
}

function New-BoltPng([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap -ArgumentList $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    $pad = [Math]::Max(1, [int]($size * 0.05))
    $w = $size - 2 * $pad
    $d = [Math]::Max(2, [int]($size * 0.44))
    $x = $pad
    $y = $pad

    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc(($x + $w - $d), $y, $d, $d, 270, 90)
    $path.AddArc(($x + $w - $d), ($y + $w - $d), $d, $d, 0, 90)
    $path.AddArc($x, ($y + $w - $d), $d, $d, 90, 90)
    $path.CloseFigure()

    $bg1 = [System.Drawing.Color]::FromArgb(255, 38, 38, 50)
    $bg2 = [System.Drawing.Color]::FromArgb(255, 20, 20, 27)
    $bgP1 = New-Point $x $y
    $bgP2 = New-Point ($x + $w) ($y + $w)
    $bgBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush -ArgumentList $bgP1, $bgP2, $bg1, $bg2
    $g.FillPath($bgBrush, $path)

    $points = New-Object 'System.Collections.Generic.List[System.Drawing.PointF]'
    foreach ($p in $bolt) {
        $pf = New-Object System.Drawing.PointF -ArgumentList ([single]($p[0] * $size)), ([single]($p[1] * $size))
        $points.Add($pf)
    }
    $poly = New-Object System.Drawing.Drawing2D.GraphicsPath
    $poly.AddPolygon($points.ToArray())

    $amber = [System.Drawing.Color]::FromArgb(255, 255, 176, 32)
    $amberLight = [System.Drawing.Color]::FromArgb(255, 255, 208, 96)
    $boltBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush -ArgumentList $bgP1, $bgP2, $amberLight, $amber
    $g.FillPath($boltBrush, $poly)

    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return , $ms.ToArray()
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$frames = @{}
foreach ($s in $sizes) { $frames[$s] = New-BoltPng $s }

$icoStream = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter -ArgumentList $icoStream
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
foreach ($s in $sizes) {
    $data = $frames[$s]
    $dim = if ($s -ge 256) { 0 } else { $s }
    $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$data.Length); $bw.Write([uint32]$offset)
    $offset += $data.Length
}
foreach ($s in $sizes) { $bw.Write($frames[$s]) }
$bw.Flush()

[IO.File]::WriteAllBytes((Join-Path $assets 'boltzip.ico'), $icoStream.ToArray())
[IO.File]::WriteAllBytes((Join-Path $assets 'boltzip.png'), $frames[256])
[IO.File]::WriteAllBytes((Join-Path $docsAssets 'logo.png'), $frames[256])
$bw.Dispose()

Write-Host ("Icon written: {0} bytes (.ico)." -f (Get-Item (Join-Path $assets 'boltzip.ico')).Length)
