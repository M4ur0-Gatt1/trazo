# Une las imágenes de referencia en una hoja de contacto para revisarlas a ojo.
# Uso: powershell -ExecutionPolicy Bypass -File tools/contact-sheet.ps1 [-Dir tests/Trazo.Rendering.Tests/Golden/brushes] [-Out hoja.png]
param([string]$Dir = (Join-Path $PSScriptRoot '..\tests\Trazo.Rendering.Tests\Golden\brushes'), [string]$Out = 'golden-sheet.png', [int]$Columns = 3)
Add-Type -AssemblyName System.Drawing
$files = Get-ChildItem $Dir -Filter *.png | Sort-Object Name
$first = [System.Drawing.Image]::FromFile($files[0].FullName); $w = $first.Width; $h = $first.Height; $first.Dispose()
$label = 22; $rows = [math]::Ceiling($files.Count / $Columns)
$sheet = New-Object System.Drawing.Bitmap ($w * $Columns), (($h + $label) * $rows)
$g = [System.Drawing.Graphics]::FromImage($sheet)
$g.Clear([System.Drawing.Color]::White)
$font = New-Object System.Drawing.Font 'Segoe UI', 10
for ($i = 0; $i -lt $files.Count; $i++) {
  $x = ($i % $Columns) * $w; $y = [math]::Floor($i / $Columns) * ($h + $label)
  $img = [System.Drawing.Image]::FromFile($files[$i].FullName)
  $g.DrawImage($img, $x, $y + $label, $w, $h); $img.Dispose()
  $g.DrawString($files[$i].BaseName, $font, [System.Drawing.Brushes]::Black, $x + 4, $y + 2)
  $g.DrawRectangle([System.Drawing.Pens]::LightGray, $x, $y, $w - 1, $h + $label - 1)
}
$sheet.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $sheet.Dispose()
Write-Output $Out
