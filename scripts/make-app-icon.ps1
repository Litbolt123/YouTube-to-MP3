#Requires -Version 5.1
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class NativeIcon {
  [DllImport("user32.dll", CharSet=CharSet.Auto)]
  public static extern bool DestroyIcon(IntPtr handle);
}
"@

$repoRoot = Split-Path -Parent $PSScriptRoot
$outPath = Join-Path $repoRoot 'src\YouTubeToMp3\app.ico'
$dir = Split-Path $outPath
if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir | Out-Null }

function New-IconBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    $pad = [Math]::Max(1, [int]($size * 0.03))
    $diameter = $size - (2 * $pad)
    $brush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(211, 47, 47))
    $g.FillEllipse($brush, $pad, $pad, $diameter, $diameter)
    $brush.Dispose()

    $penWidth = [Math]::Max(1.5, $size * 0.08)
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), $penWidth
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round

    $cx = $size / 2.0
    $top = $size * 0.28
    $bottom = $size * 0.66
    $wing = $size * 0.14
    $g.DrawLine($pen, $cx, $top, $cx, $bottom)
    $g.DrawLine($pen, $cx - $wing, $bottom - $wing, $cx, $bottom)
    $g.DrawLine($pen, $cx + $wing, $bottom - $wing, $cx, $bottom)
    $pen.Dispose()
    $g.Dispose()
    return $bmp
}

$bmp = New-IconBitmap 256
try {
    $hIcon = $bmp.GetHicon()
    $owned = [System.Drawing.Icon]::FromHandle($hIcon)
    $clone = $owned.Clone()
    try {
        $fs = [System.IO.File]::Create($outPath)
        try { $clone.Save($fs) }
        finally { $fs.Dispose() }
    }
    finally {
        $clone.Dispose()
        $owned.Dispose()
        [void][NativeIcon]::DestroyIcon($hIcon)
    }
}
finally {
    $bmp.Dispose()
}

$fi = Get-Item $outPath
if ($fi.Length -lt 100) { throw "app.ico looks too small ($($fi.Length) bytes)" }
Write-Host ("Wrote {0} ({1} bytes)" -f $outPath, $fi.Length)
