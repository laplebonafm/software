<#
  Compila VirtualStreamPlayer y TestPipeClient como ejecutables self-contained
  de un solo archivo, listos para empaquetar con Inno Setup.

  Uso:
    .\build\publish.ps1
#>

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$publishDir = Join-Path $root "publish\VirtualStreamPlayer"

Write-Host "== Compilando VirtualStreamPlayer ==" -ForegroundColor Cyan
dotnet publish "$root\src\VirtualStreamPlayer\VirtualStreamPlayer.csproj" `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $publishDir

Write-Host "== Compilando TestPipeClient ==" -ForegroundColor Cyan
dotnet publish "$root\src\VirtualStreamPlayer.TestPipeClient\VirtualStreamPlayer.TestPipeClient.csproj" `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $publishDir

Write-Host ""
Write-Host "Listo. Binarios en: $publishDir" -ForegroundColor Green
Write-Host "Ahora abre installer\VirtualStreamPlayerSetup.iss con Inno Setup y compílalo." -ForegroundColor Green
