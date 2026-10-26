<#
.SYNOPSIS
  Generates the MSIX visual assets (tiles, taskbar icons, store logo) from Assets/app.ico.
  Output: store/Assets (scale-qualified PNGs; build-store.ps1 indexes them with MakePri).
#>
param([string]$OutDir = "$PSScriptRoot\Assets")

Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Force $OutDir | Out-Null
$ico = Join-Path (Split-Path $PSScriptRoot) "Assets\app.ico"

# The .ico has frames drawn for each size (16…256): take the closest one at or above the target.
function Get-Logo([int]$size) {
    $frame = (16, 20, 24, 32, 40, 48, 64, 128, 256 | Where-Object { $_ -ge $size } | Select-Object -First 1)
    if (-not $frame) { $frame = 256 }
    $icon = New-Object System.Drawing.Icon $ico, $frame, $frame
    $bmp = $icon.ToBitmap(); $icon.Dispose()
    return $bmp
}

# $logoFraction: logo size relative to the image height (tiles have padding, icons are full-bleed).
function New-Asset([string]$name, [int]$w, [int]$h, [double]$logoFraction) {
    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = 'HighQualityBicubic'
    $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)
    $size = [int][Math]::Round([Math]::Min($w, $h) * $logoFraction)
    $logo = Get-Logo $size
    $g.DrawImage($logo, [int](($w - $size) / 2), [int](($h - $size) / 2), $size, $size)
    $logo.Dispose(); $g.Dispose()
    $bmp.Save((Join-Path $OutDir $name), [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

foreach ($scale in 100, 200) {
    $f = $scale / 100
    New-Asset "StoreLogo.scale-$scale.png" (50 * $f) (50 * $f) 1.0
    New-Asset "Square44x44Logo.scale-$scale.png" (44 * $f) (44 * $f) 1.0
    New-Asset "Square150x150Logo.scale-$scale.png" (150 * $f) (150 * $f) 0.6
    New-Asset "Wide310x150Logo.scale-$scale.png" (310 * $f) (150 * $f) 0.6
}
# Taskbar / Start / file icons at exact pixel sizes (plated and unplated).
foreach ($px in 16, 24, 32, 48, 256) {
    New-Asset "Square44x44Logo.targetsize-$px.png" $px $px 1.0
    New-Asset "Square44x44Logo.targetsize-${px}_altform-unplated.png" $px $px 1.0
}
"assets: $((Get-ChildItem $OutDir -Filter *.png).Count) files in $OutDir"
