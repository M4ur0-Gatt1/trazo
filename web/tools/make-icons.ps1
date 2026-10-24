# Genera icons/icon-192.png e icons/icon-512.png (mismo diseño que icons/icon.svg).
# Uso: powershell -ExecutionPolicy Bypass -File tools/make-icons.ps1
param([string]$OutDir = (Join-Path $PSScriptRoot '..\icons'))
Add-Type -AssemblyName System.Drawing
$OutDir = (Resolve-Path $OutDir).Path
foreach ($size in 192, 512) {
  $bmp = New-Object System.Drawing.Bitmap $size, $size
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = 'AntiAlias'
  $k = $size / 512.0
  $g.ScaleTransform($k, $k)
  $d = 224
  $path = New-Object System.Drawing.Drawing2D.GraphicsPath
  $path.AddArc(0, 0, $d, $d, 180, 90); $path.AddArc(512 - $d, 0, $d, $d, 270, 90)
  $path.AddArc(512 - $d, 512 - $d, $d, $d, 0, 90); $path.AddArc(0, 512 - $d, $d, $d, 90, 90); $path.CloseFigure()
  $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush ([System.Drawing.Point]::new(0, 0)), ([System.Drawing.Point]::new(512, 512)), ([System.Drawing.Color]::FromArgb(43, 53, 80)), ([System.Drawing.Color]::FromArgb(15, 17, 23))
  $g.FillPath($bg, $path)
  $sb = New-Object System.Drawing.Drawing2D.LinearGradientBrush ([System.Drawing.Point]::new(100, 380)), ([System.Drawing.Point]::new(410, 140)), ([System.Drawing.Color]::White), ([System.Drawing.Color]::White)
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
  $bmp.Save((Join-Path $OutDir "icon-$size.png"), [System.Drawing.Imaging.ImageFormat]::Png)
  $g.Dispose(); $bmp.Dispose()
}
