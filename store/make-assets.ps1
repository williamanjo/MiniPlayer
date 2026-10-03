<#
.SYNOPSIS
  Generates the MSIX visual assets (tiles, taskbar icons, store logo) from the app's logo drawing.
  Output: store/Assets (scale-qualified PNGs; build-store.ps1 indexes them with MakePri).
#>
param([string]$OutDir = "$PSScriptRoot\Assets")

Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Force $OutDir | Out-Null

# Rounded gradient square with a music note: same look as Assets/app.ico.
function Draw-Logo([System.Drawing.Graphics]$g, [float]$x, [float]$y, [float]$size) {
    $r = [Math]::Max(2, $size * 0.22)
    $d = 2 * $r
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $size - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $size - $d, $y + $size - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $size - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $rect = New-Object System.Drawing.RectangleF $x, $y, $size, $size
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.Color]::FromArgb(255, 124, 77, 255)), ([System.Drawing.Color]::FromArgb(255, 0, 184, 212)), 45.0
    $g.FillPath($brush, $path)
    $font = New-Object System.Drawing.Font 'Segoe UI Symbol', ([single]($size * 0.58)), ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
    $fmt = New-Object System.Drawing.StringFormat
    $fmt.Alignment = 'Center'; $fmt.LineAlignment = 'Center'
    $text = New-Object System.Drawing.RectangleF $x, ($y + $size * 0.03), $size, $size
    $g.DrawString([string][char]0x266B, $font, [System.Drawing.Brushes]::White, $text, $fmt)
}

# $logoFraction: logo size relative to the image height (tiles have padding, icons are full-bleed).
function New-Asset([string]$name, [int]$w, [int]$h, [double]$logoFraction) {
    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.TextRenderingHint = 'AntiAliasGridFit'
    $g.Clear([System.Drawing.Color]::Transparent)
    $size = [Math]::Round([Math]::Min($w, $h) * $logoFraction)
    Draw-Logo $g (($w - $size) / 2) (($h - $size) / 2) $size
    $g.Dispose()
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
