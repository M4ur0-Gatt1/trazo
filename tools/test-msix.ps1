# Prueba el paquete MSIX en este equipo sin firmarlo: registra la carpeta compilada (requiere el modo de
# desarrollador de Windows), ejecuta la autoprueba dentro del contexto del paquete y lo desinstala.
#
# Uso: powershell -ExecutionPolicy Bypass -File tools/test-msix.ps1 [-Arch x64] [-Keep]
#   -Keep deja el paquete instalado para probarlo a mano (Inicio › Trazo).
param([string]$Arch = "x64", [switch]$Keep)
$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$dev = Get-ItemProperty "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock" -ErrorAction SilentlyContinue
if ($dev.AllowDevelopmentWithoutDevLicense -ne 1) { throw "Activa el modo de desarrollador (Configuración › Sistema › Para programadores)." }

$layout = Get-ChildItem (Join-Path $root "src\Trazo.App\bin") -Recurse -Filter AppxManifest.xml |
    Where-Object { $_.FullName -match "\\Release\\.*\\win-$Arch\\AppxManifest.xml$" } | Select-Object -First 1
if (-not $layout) { throw "Compila antes con tools/build-msix.ps1 -Arch $Arch" }
$name = ([xml](Get-Content $layout.FullName)).Package.Identity.Name

# el servicio de implementación no ve las unidades de «subst»: se pasa la ruta real
function Get-RealPath([string]$path) {
    foreach ($line in (subst)) {
        if ($line -match '^([A-Z]:)\\: => (.+)$' -and $path.StartsWith($Matches[1], [StringComparison]::OrdinalIgnoreCase)) {
            return $Matches[2] + $path.Substring(2)
        }
    }
    return $path
}
# se registra una copia en una ruta corta: la carpeta compilada puede pasar de 260 caracteres y cambia en cada compilación
$copy = Join-Path $env:TEMP "trazo-msix-layout-$Arch"
Get-AppxPackage -Name $name | Remove-AppxPackage
Remove-Item $copy -Recurse -Force -ErrorAction SilentlyContinue
robocopy (Get-RealPath $layout.DirectoryName) $copy /E /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw "No se pudo copiar la carpeta del paquete" }
$manifestPath = Join-Path $copy "AppxManifest.xml"

Add-AppxPackage -Register $manifestPath
$pkg = Get-AppxPackage -Name $name
Write-Output "Instalado: $($pkg.PackageFullName)"

# fuera de AppData, que el paquete ve virtualizado
$out = Get-RealPath (Join-Path $root "artifacts\msix-selftest")
Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue
# la autoprueba corre dentro del contexto del paquete (rutas virtualizadas, identidad, etc.)
Invoke-CommandInDesktopPackage -PackageFamilyName $pkg.PackageFamilyName -AppId App -Command (Join-Path $pkg.InstallLocation "Trazo.exe") -Args "--selftest `"$out`""
$deadline = (Get-Date).AddMinutes(3)
while (-not (Test-Path "$out\selftest.txt") -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 500 }
Start-Sleep 7
$report = Get-Content "$out\selftest.txt" -Encoding UTF8
Write-Output ("Autoprueba en el paquete: " + ($report | Select-Object -Last 1))
$report | Where-Object { $_ -notlike "OK*" -and $_ -notlike "RESULTADO*" }

if (-not $Keep) {
    Remove-AppxPackage $pkg.PackageFullName
    Remove-Item $copy -Recurse -Force -ErrorAction SilentlyContinue
    Write-Output "Paquete desinstalado."
}
