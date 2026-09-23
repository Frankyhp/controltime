#Requires -Version 7
<#
  Compila y prepara los artefactos de release de ControlTime.

  1. Publica App y Service (win-x64, autocontenido) en publish\.
  2. Si Inno Setup esta disponible, compila el instalador en dist\.

  Uso:
    pwsh build/publish.ps1 [-SkipInstaller] [-Version 1.0.0]
#>
param(
    [switch]$SkipInstaller,
    [string]$Version
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$publishDir = Join-Path $root 'publish'
$distDir = Join-Path $root 'dist'

if (-not $Version) {
    $props = Get-Content -LiteralPath (Join-Path $root 'Directory.Build.props') -Raw
    $Version = [regex]::Match($props, '<Version>(.+?)</Version>').Groups[1].Value
    if (-not $Version) { throw 'No se encontro <Version> en Directory.Build.props.' }
}

Write-Host "ControlTime v$Version" -ForegroundColor Cyan

function Publish-Project([string]$project, [string]$out) {
    Write-Host "Publicando $(Split-Path -Leaf $project) -> $out" -ForegroundColor Cyan
    dotnet publish $project -c Release -r win-x64 --self-contained true -p:PublishReadyToRun=false -o $out
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish fallo para $project (codigo $LASTEXITCODE)." }
}

if (Test-Path -LiteralPath $publishDir) {
    Remove-Item -LiteralPath $publishDir -Recurse -Force
}

Publish-Project (Join-Path $root 'src\ControlTime.App') (Join-Path $publishDir 'App')
Publish-Project (Join-Path $root 'src\ControlTime.Service') (Join-Path $publishDir 'Service')

if ($SkipInstaller) {
    Write-Host 'Artefactos en publish\ (instalador omitido).' -ForegroundColor Green
    return
}

$iscc = @(
    'iscc.exe',
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Get-Command $_ -ErrorAction SilentlyContinue } | Select-Object -First 1

if (-not $iscc) {
    Write-Warning 'ISCC.exe (Inno Setup 6) no encontrado. Los artefactos estan en publish\.'
    Write-Warning 'Instalalo (winget install JRSoftware.InnoSetup) y vuelve a ejecutar, o compila installer\ControlTime.iss manualmente.'
    return
}

New-Item -ItemType Directory -Force -Path $distDir | Out-Null
Write-Host 'Compilando instalador...' -ForegroundColor Cyan
& $iscc "/DMyAppVersion=$Version" "/DPublishDir=$publishDir" (Join-Path $root 'installer\ControlTime.iss')
if ($LASTEXITCODE -ne 0) { throw "ISCC fallo (codigo $LASTEXITCODE)." }

Write-Host "Instalador generado en dist\." -ForegroundColor Green
