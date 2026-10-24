# Genera el icono de la app de escritorio (src/Trazo.App/Assets/Trazo.ico y Logo.png).
# Uso: powershell -ExecutionPolicy Bypass -File tools/make-icon.ps1
Add-Type -AssemblyName System.Drawing
$assets = Join-Path $PSScriptRoot '..\src\Trazo.App\Assets'
New-Item -ItemType Directory -Force $assets | Out-Null
$assets = (Resolve-Path $assets).Path

function New-TrazoBitmap([int]$size) {
  $bmp = New-Object System.Drawing.Bitmap $size, $size
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = 'AntiAlias'
  $g.ScaleTransform($size / 512.0, $size / 512.0)
  $d = 224
  $path = New-Object System.Drawing.Drawing2D.GraphicsPath
  $path.AddArc(0, 0, $d, $d, 180, 90); $path.AddArc(512 - $d, 0, $d, $d, 270, 90)
  $path.AddArc(512 - $d, 512 - $d, $d, $d, 0, 90); $path.AddArc(0, 512 - $d, $d, $d, 90, 90); $path.CloseFigure()
  $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush ([System.Drawing.Point]::new(0, 0)), ([System.Drawing.Point]::new(512, 512)), ([System.Drawing.Color]::FromArgb(43, 53, 80)), ([System.Drawing.Color]::FromArgb(15, 17, 23))
  $g.FillPath($bg, $path)
  $sb = New-Object System.Drawing.Drawing2D.LinearGradientBrush ([System.Drawing.Point]::new(80, 400)), ([System.Drawing.Point]::new(430, 120)), ([System.Drawing.Color]::White), ([System.Drawing.Color]::White)
  $blend = New-Object System.Drawing.Drawing2D.ColorBlend 3
  $blend.Colors = @([System.Drawing.Color]::FromArgb(255, 95, 109), [System.Drawing.Color]::FromArgb(255, 195, 113), [System.Drawing.Color]::FromArgb(61, 139, 255))
  $blend.Positions = @(0.0, 0.5, 1.0)
  $sb.InterpolationColors = $blend
  $pen = New-Object System.Drawing.Pen $sb, 54
  $pen.StartCap = 'Round'; $pen.EndCap = 'Round'; $pen.LineJoin = 'Round'
  $g.DrawBeziers($pen, [System.Drawing.PointF[]]@(
    [System.Drawing.PointF]::new(110, 372), [System.Drawing.PointF]::new(150, 252), [System.Drawing.PointF]::new(230, 332), [System.Drawing.PointF]::new(270, 232),
    [System.Drawing.PointF]::new(310, 132), [System.Drawing.PointF]::new(380, 132), [System.Drawing.PointF]::new(402, 150)))
  $g.FillEllipse([System.Drawing.Brushes]::White, 375, 123, 54, 54)
  $g.Dispose()
  return $bmp
}

# ICO con entradas PNG (formato admitido desde Windows Vista)
$sizes = 16, 24, 32, 48, 64, 128, 256
$pngs = foreach ($s in $sizes) {
  $b = New-TrazoBitmap $s; $ms = New-Object System.IO.MemoryStream
  $b.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $b.Dispose(); , $ms.ToArray()
}
$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
  $s = $sizes[$i]; $dim = if ($s -ge 256) { 0 } else { $s }
  $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
  $w.Write([UInt16]1); $w.Write([UInt16]32); $w.Write([UInt32]$pngs[$i].Length); $w.Write([UInt32]$offset)
  $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $w.Write($p) }
[System.IO.File]::WriteAllBytes((Join-Path $assets 'Trazo.ico'), $out.ToArray())

$logo = New-TrazoBitmap 256
$logo.Save((Join-Path $assets 'Logo.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$logo.Dispose()
Write-Output "Iconos generados en $assets"
