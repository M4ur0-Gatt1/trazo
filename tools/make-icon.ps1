# Genera todos los iconos de Trazo desde una sola definición:
#   src/Trazo.App/Assets/Trazo.ico y Logo.png (app de escritorio e instalador)
#   web/icons/icon.svg, icon-192.png e icon-512.png (versión web)
# Diseño: una pincelada en S con presión (fina → gruesa → fina), degradado rojo anaranjado → cian, fondo negro.
# Uso: powershell -ExecutionPolicy Bypass -File tools/make-icon.ps1
Add-Type -AssemblyName System.Drawing
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$assets = Join-Path $root 'src\Trazo.App\Assets'
$web = Join-Path $root 'web\icons'

$warm = [System.Drawing.Color]::FromArgb(255, 74, 28)   # rojo anaranjado #FF4A1C
$cool = [System.Drawing.Color]::FromArgb(0, 229, 255)   # cian #00E5FF
$inv = [System.Globalization.CultureInfo]::InvariantCulture

# ---- Geometría en un lienzo de 512 × 512 ----
# Curva de Bézier cúbica en S; el grosor sigue un perfil de presión: fino en los extremos, máximo en el centro.
$p0 = @(96, 392); $p1 = @(196, 132); $p2 = @(318, 384); $p3 = @(418, 122)
function Bezier([double]$t) {
  $u = 1 - $t
  $x = $u*$u*$u*$p0[0] + 3*$u*$u*$t*$p1[0] + 3*$u*$t*$t*$p2[0] + $t*$t*$t*$p3[0]
  $y = $u*$u*$u*$p0[1] + 3*$u*$u*$t*$p1[1] + 3*$u*$t*$t*$p2[1] + $t*$t*$t*$p3[1]
  return @($x, $y)
}
function Width([double]$t) { 3 + 58 * [math]::Pow([math]::Sin([math]::PI * $t), 1.15) }

$n = 160
$left = New-Object System.Collections.Generic.List[double[]]
$right = New-Object System.Collections.Generic.List[double[]]
for ($i = 0; $i -le $n; $i++) {
  $t = $i / $n
  $p = Bezier $t
  # 0.0 y 1.0 (no 0 y 1): con enteros, [math]::Max/Min eligen la sobrecarga Int32 y redondean t
  $a = Bezier ([math]::Max(0.0, $t - 0.002)); $b = Bezier ([math]::Min(1.0, $t + 0.002))
  $dx = $b[0] - $a[0]; $dy = $b[1] - $a[1]; $len = [math]::Sqrt($dx*$dx + $dy*$dy)
  $nx = -$dy / $len; $ny = $dx / $len
  $h = (Width $t) / 2
  $left.Add(@(($p[0] + $nx*$h), ($p[1] + $ny*$h)))
  $right.Add(@(($p[0] - $nx*$h), ($p[1] - $ny*$h)))
}
$outline = @($left) + @([System.Linq.Enumerable]::Reverse($right))

function New-Icon([int]$size) {
  $bmp = New-Object System.Drawing.Bitmap $size, $size
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = 'AntiAlias'
  $g.PixelOffsetMode = 'HighQuality'
  $g.ScaleTransform($size / 512.0, $size / 512.0)
  # fondo negro con esquinas redondeadas
  $d = 224
  $bg = New-Object System.Drawing.Drawing2D.GraphicsPath
  $bg.AddArc(0, 0, $d, $d, 180, 90); $bg.AddArc(512 - $d, 0, $d, $d, 270, 90)
  $bg.AddArc(512 - $d, 512 - $d, $d, $d, 0, 90); $bg.AddArc(0, 512 - $d, $d, $d, 90, 90); $bg.CloseFigure()
  $g.FillPath([System.Drawing.Brushes]::Black, $bg)
  # pincelada
  $pts = [System.Drawing.PointF[]]($outline | ForEach-Object { [System.Drawing.PointF]::new($_[0], $_[1]) })
  $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush ([System.Drawing.PointF]::new(96, 392)), ([System.Drawing.PointF]::new(418, 122)), $warm, $cool
  # cada color puro en su tercio y transición corta: la mezcla RGB de naranja y cian es un gris apagado
  $blend = New-Object System.Drawing.Drawing2D.ColorBlend 4
  $blend.Colors = @($warm, $warm, $cool, $cool)
  $blend.Positions = @(0.0, 0.36, 0.64, 1.0)
  $brush.InterpolationColors = $blend
  $g.FillPolygon($brush, $pts)
  $g.Dispose()
  return $bmp
}

# ---- ICO con entradas PNG (16 a 256 px) ----
$sizes = 16, 24, 32, 48, 64, 128, 256
$pngs = foreach ($s in $sizes) {
  $b = New-Icon $s; $ms = New-Object System.IO.MemoryStream
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

foreach ($spec in @(@((Join-Path $assets 'Logo.png'), 256), @((Join-Path $web 'icon-192.png'), 192), @((Join-Path $web 'icon-512.png'), 512))) {
  $b = New-Icon $spec[1]; $b.Save($spec[0], [System.Drawing.Imaging.ImageFormat]::Png); $b.Dispose()
}

# ---- SVG equivalente para la web ----
$d = ($outline | ForEach-Object -Begin { $first = $true } -Process {
  $cmd = if ($first) { 'M' } else { 'L' }; $first = $false
  '{0}{1} {2}' -f $cmd, $_[0].ToString('0.#', $inv), $_[1].ToString('0.#', $inv)
}) -join ''
$svg = @"
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 512 512">
  <defs>
    <linearGradient id="st" gradientUnits="userSpaceOnUse" x1="96" y1="392" x2="418" y2="122">
      <stop offset=".36" stop-color="#ff4a1c"/>
      <stop offset=".64" stop-color="#00e5ff"/>
    </linearGradient>
  </defs>
  <rect width="512" height="512" rx="112" fill="#000"/>
  <path d="${d}Z" fill="url(#st)"/>
</svg>
"@
Set-Content -Path (Join-Path $web 'icon.svg') -Value $svg -Encoding ascii
Write-Output "Iconos generados: Trazo.ico, Logo.png, icon.svg, icon-192.png, icon-512.png"
