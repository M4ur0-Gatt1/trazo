# Compila el paquete MSIX de Trazo para la Microsoft Store (x64 y ARM64) y los une en un .msixbundle.
# El paquete va sin firmar: la Store lo firma al publicarlo. Para probarlo en este equipo sin firmar,
# usa tools/test-msix.ps1 (necesita el modo de desarrollador de Windows).
#
# Uso:
#   powershell -ExecutionPolicy Bypass -File tools/build-msix.ps1 -Version 1.0.0 `
#     -IdentityName "12345Mauro.Trazo" -Publisher "CN=XXXXXXXX-XXXX-XXXX-XXXX-XXXXXXXXXXXX" -PublisherDisplayName "Mauro Gatti"
# Los tres valores de identidad salen del Centro de partners (Administración de productos › Identidad del producto).
# Sin ellos se usa una identidad de desarrollo que la Store rechazaría. También se leen de las variables
# TRAZO_STORE_IDENTITY, TRAZO_STORE_PUBLISHER y TRAZO_STORE_PUBLISHER_NAME.
param(
    [string]$Version = "1.0.0",
    [string]$IdentityName = $env:TRAZO_STORE_IDENTITY,
    [string]$Publisher = $env:TRAZO_STORE_PUBLISHER,
    [string]$PublisherDisplayName = $env:TRAZO_STORE_PUBLISHER_NAME,
    [string[]]$Arch = @("x64", "arm64"),
    [string]$Out = ""
)
$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
if (-not $Out) { $Out = Join-Path $root "artifacts\msix" }
$manifest = Join-Path $root "src\Trazo.App\Package.appxmanifest"
$originalBytes = [System.IO.File]::ReadAllBytes($manifest)

# La Store exige versión de 4 números con el último a 0; los sufijos (-beta.N) no caben
$numeric = ($Version -split "-")[0]
$parts = @($numeric -split "\.")
while ($parts.Count -lt 3) { $parts += "0" }
$pkgVersion = "$($parts[0]).$($parts[1]).$($parts[2]).0"

# XmlDocument.Load entiende el BOM; convertir el texto con [xml] fallaría si lo lleva
$xml = New-Object System.Xml.XmlDocument
$xml.PreserveWhitespace = $true
$xml.Load($manifest)
$id = $xml.Package.Identity
$id.Version = $pkgVersion
if ($IdentityName) { $id.Name = $IdentityName }
if ($Publisher) { $id.Publisher = $Publisher }
if ($PublisherDisplayName) { $xml.Package.Properties.PublisherDisplayName = $PublisherDisplayName }
$storeReady = [bool]($IdentityName -and $Publisher -and $PublisherDisplayName)

Remove-Item $Out -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $Out | Out-Null
try {
    $xml.Save($manifest)
    foreach ($a in $Arch) {
        $platform = if ($a -eq "arm64") { "ARM64" } else { "x64" }
        Write-Output "== MSIX $a ($pkgVersion)"
        dotnet publish (Join-Path $root "src\Trazo.App") -c Release -r "win-$a" -p:Platform=$platform `
            -p:WindowsPackageType=MSIX -p:GenerateAppxPackageOnBuild=true -p:AppxPackageSigningEnabled=false `
            -p:AppxBundle=Never -p:UapAppxPackageBuildMode=SideloadOnly -p:Version=$numeric `
            "-p:AppxPackageDir=$Out\$a\" -v q -nologo
        if ($LASTEXITCODE) { throw "Falló la compilación MSIX de $a" }
    }
}
finally {
    # el manifiesto del repositorio no cambia
    [System.IO.File]::WriteAllBytes($manifest, $originalBytes)
}

$packages = Get-ChildItem $Out -Recurse -Filter "*.msix" | Where-Object { $_.DirectoryName -notlike "*Dependencies*" }
$stage = Join-Path $Out "bundle"
New-Item -ItemType Directory -Force $stage | Out-Null
foreach ($p in $packages) { Copy-Item $p.FullName $stage }

# makeappx viene en el paquete NuGet Microsoft.Windows.SDK.BuildTools que ya usa el proyecto
$makeappx = Get-ChildItem (Join-Path $env:USERPROFILE ".nuget\packages\microsoft.windows.sdk.buildtools") -Recurse -Filter makeappx.exe |
    Where-Object { $_.FullName -match "\\x64\\" } | Sort-Object FullName -Descending | Select-Object -First 1
if (-not $makeappx) { throw "No se encontró makeappx.exe" }
$bundle = Join-Path $Out "Trazo-$Version.msixbundle"
& $makeappx.FullName bundle /d $stage /p $bundle /bv $pkgVersion /o | Out-Null
if ($LASTEXITCODE) { throw "Falló makeappx bundle" }
Remove-Item $stage -Recurse -Force

Write-Output "Paquetes:"
$packages | ForEach-Object { Write-Output "  $($_.FullName)" }
Write-Output "Lote para la Store: $bundle"
if (-not $storeReady) { Write-Warning "Identidad de desarrollo: la Store rechazará este lote. Pasa los valores del Centro de partners." }
