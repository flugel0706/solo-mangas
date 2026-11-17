#Requires -Version 5.1
<#
.SYNOPSIS
    Publica o Solo Mangás em dist/ e valida o executável.

.DESCRIPTION
    Gera o executável único autocontido (win-x64), confere a versão do arquivo
    contra o csproj e grava dist/SHA256SUMS.txt.

.PARAMETER Configuration
    Release (padrão) ou Debug.

.PARAMETER Output
    Diretório de saída. Padrão: dist na raiz do repositório.

.PARAMETER SkipBuild
    Não executa o dotnet publish; apenas valida o executável já publicado.

.EXAMPLE
    .\scripts\build.ps1

.EXAMPLE
    .\scripts\build.ps1 -Output out -SkipBuild
#>
[CmdletBinding()]
param(
    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release',

    [string]$Output = 'dist',

    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src/SoloMangas/SoloMangas.csproj'

if (-not (Test-Path $project)) {
    throw "Projeto não encontrado: $project"
}

$outDir = if ([System.IO.Path]::IsPathRooted($Output)) { $Output } else { Join-Path $root $Output }
New-Item -ItemType Directory -Path $outDir -Force | Out-Null

if (-not $SkipBuild) {
    Write-Host "==> Publicando executável único autocontido" -ForegroundColor Cyan
    dotnet publish $project -c $Configuration -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -o $outDir
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish falhou (código $LASTEXITCODE)." }
}
else {
    Write-Host "==> Pulando build; validando $outDir" -ForegroundColor Cyan
}

$exe = Join-Path $outDir 'SoloMangas.exe'
if (-not (Test-Path $exe)) {
    throw "Executável não encontrado: $exe"
}

$file = Get-Item $exe
if ($file.Length -lt 60MB) {
    throw "Executável menor que o esperado ($([math]::Round($file.Length / 1MB, 1)) MB). O publish autocontido falhou?"
}

[xml]$csproj = Get-Content $project
$version = ($csproj.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
$fileVersion = $file.VersionInfo.FileVersion
if (-not $fileVersion.StartsWith($version)) {
    throw "Versão do executável ($fileVersion) não corresponde ao csproj ($version)."
}

$hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $exe).Hash.ToLowerInvariant()
"$hash  SoloMangas.exe" | Set-Content -Path (Join-Path $outDir 'SHA256SUMS.txt') -Encoding ASCII

Write-Host ""
Write-Host "Build concluído:" -ForegroundColor Green
Write-Host "  arquivo  : $exe"
Write-Host "  tamanho  : $([math]::Round($file.Length / 1MB, 1)) MB"
Write-Host "  versão   : $fileVersion"
Write-Host "  sha256   : $hash"
