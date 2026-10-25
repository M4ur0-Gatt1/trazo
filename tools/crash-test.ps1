# Prueba de cierre forzado (RC-01.9, FI-01.8) con el proceso real:
#   1. abre Trazo, pinta una línea y espera a que el AUTOGUARDADO la guarde (no se guarda a mano);
#   2. mata el proceso sin dejarle cerrar (como un corte de luz);
#   3. vuelve a abrir Trazo y comprueba el aviso de sesión abandonada y que la línea esté en el archivo.
#
# Uso: powershell -ExecutionPolicy Bypass -File tools/crash-test.ps1 [-Exe ruta\Trazo.exe]
param([string]$Exe = "")
$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
if (-not $Exe) {
    $Exe = (Get-ChildItem (Join-Path $root "src\Trazo.App\bin") -Recurse -Filter Trazo.exe |
        Where-Object { $_.FullName -notmatch "\\AppX\\" } | Sort-Object LastWriteTime -Descending | Select-Object -First 1).FullName
}
if (-not $Exe) { throw "Compila antes la app (dotnet build src/Trazo.App -p:Platform=x64)" }
$folder = Join-Path $env:TEMP "trazo-cierre-forzado"
Remove-Item $folder -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory $folder | Out-Null

Write-Output "1. Pintar y esperar al autoguardado"
$p = Start-Process $Exe -ArgumentList "--selftest-crash", "`"$folder`"" -PassThru
$deadline = (Get-Date).AddSeconds(60)
while (-not (Test-Path "$folder\listo.txt") -and -not $p.HasExited -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 200 }
if (-not (Test-Path "$folder\listo.txt")) { throw "La app no llegó a pintar y guardar" }
$state = Get-Content "$folder\listo.txt"
Write-Output "   estado antes de matar el proceso: $state"

Write-Output "2. Matar el proceso (sin cierre normal)"
Stop-Process -Id $p.Id -Force
$p.WaitForExit(10000) | Out-Null

Write-Output "3. Volver a abrir y comprobar"
$r = Start-Process $Exe -ArgumentList "--selftest-recover", "`"$folder`"" -PassThru
if (-not $r.WaitForExit(90000)) { $r.Kill(); throw "La comprobación no terminó" }
$report = Get-Content "$folder\recuperacion.txt" -Encoding UTF8
$report | ForEach-Object { Write-Output "   $_" }
if ($r.ExitCode -ne 0 -or $state -ne "guardado") { exit 1 }
