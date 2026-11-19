#Requires -Version 5.1
<#
.SYNOPSIS
    Publica o repositório do Solo Mangás no GitHub e cria a release.

.DESCRIPTION
    Requer GitHub CLI autenticado (gh auth login). O script:
      1. valida a autenticação;
      2. inicializa o repositório git, se necessário;
      3. cria o repositório remoto (ou usa o origin existente);
      4. configura descrição, homepage e tópicos;
      5. faz push da branch main;
      6. publica a tag e a release com o executável, SHA256SUMS.txt e update.json.

.PARAMETER RepoName
    Nome do repositório. Padrão: solo-mangas.

.PARAMETER Visibility
    public (padrão) ou private.

.PARAMETER Tag
    Tag da release. Padrão: v2.7.0.

.PARAMETER Description
    Descrição do repositório no GitHub.

.PARAMETER AssetPath
    Executável já publicado a usar na release. Quando omitido, o script compila
    o projeto e usa dist\SoloMangas.exe.

.PARAMETER SkipRelease
    Não cria tag/release; apenas publica o código.

.EXAMPLE
    .\scripts\publish-github.ps1

.EXAMPLE
    .\scripts\publish-github.ps1 -RepoName meu-solo -Visibility private
#>
[CmdletBinding()]
param(
    [string]$RepoName = 'solo-mangas',

    [ValidateSet('public', 'private')]
    [string]$Visibility = 'public',

    [string]$Tag = 'v2.7.0',

    [string]$Description = 'Leitor nativo de mangás, manhwas e manhuas para Windows. C#/.NET 8 + WPF, executável único e atualização integrada.',

    [string]$AssetPath = '',

    [switch]$SkipRelease
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$script:gh = $null
$script:ghLastExit = 0

function Invoke-Gh {
    param([string[]]$Arguments)

    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = & $script:gh @Arguments 2>&1
        $script:ghLastExit = $LASTEXITCODE
        return $output
    }
    finally {
        $ErrorActionPreference = $previous
    }
}

Push-Location $root
try {
    # ---------------------------------------------------------------- gh
    $ghCommand = Get-Command gh -ErrorAction SilentlyContinue
    $script:gh = if ($ghCommand) { $ghCommand.Source } else { $null }
    if (-not $script:gh) {
        $candidate = Join-Path $env:ProgramFiles 'GitHub CLI/gh.exe'
        if (Test-Path $candidate) { $script:gh = $candidate }
    }
    if (-not $script:gh) {
        throw 'GitHub CLI não encontrado. Instale com: winget install GitHub.cli'
    }

    $null = Invoke-Gh @('auth', 'status')
    if ($script:ghLastExit -ne 0) {
        throw "GitHub CLI sem autenticação. Rode: & '$script:gh' auth login"
    }

    $login = ((Invoke-Gh @('api', 'user', '--jq', '.login')) | Out-String).Trim()
    if ($script:ghLastExit -ne 0 -or -not $login) {
        throw 'Não foi possível obter o usuário do GitHub.'
    }
    Write-Host "==> Autenticado como $login" -ForegroundColor Cyan

    # ---------------------------------------------------------------- versão
    [xml]$csproj = Get-Content (Join-Path $root 'src/SoloMangas/SoloMangas.csproj')
    $version = ($csproj.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
    if ($Tag.TrimStart('v') -ne $version) {
        throw "A tag $Tag não corresponde à versão $version do csproj."
    }

    # ---------------------------------------------------------------- git
    if (-not (Test-Path (Join-Path $root '.git'))) {
        Write-Host "==> Inicializando repositório git" -ForegroundColor Cyan
        git init -b main | Out-Null
    }

    $userName = (git config user.name) 2>$null
    $userEmail = (git config user.email) 2>$null
    if (-not $userName) {
        git config user.name $login
        Write-Host "    user.name definido como $login"
    }
    if (-not $userEmail) {
        git config user.email "$login@users.noreply.github.com"
        Write-Host "    user.email definido como $login@users.noreply.github.com"
    }

    git add -A
    git diff --cached --quiet
    if ($LASTEXITCODE -ne 0) {
        $message = if ($SkipRelease) { 'Publica o projeto Solo Mangás' } else { "Solo Mangás $Tag" }
        git commit -m $message | Out-Null
        Write-Host "==> Commit criado: $message" -ForegroundColor Cyan
    }

    # ---------------------------------------------------------------- remote
    $remotes = @(git remote)
    if ($remotes -notcontains 'origin') {
        Write-Host "==> Criando repositório $login/$RepoName ($Visibility)" -ForegroundColor Cyan
        $result = Invoke-Gh @('repo', 'create', "$login/$RepoName", "--$Visibility", '--source', '.', '--remote', 'origin', '--push', '--description', $Description)
        $result | ForEach-Object { Write-Host $_ }
        if ($script:ghLastExit -ne 0) { throw 'Falha ao criar o repositório remoto.' }
    }
    else {
        Write-Host "==> Enviando para origin" -ForegroundColor Cyan
        git push -u origin main
        if ($LASTEXITCODE -ne 0) { throw 'Falha no git push.' }
    }

    # ---------------------------------------------------------------- metadados
    Write-Host "==> Configurando descrição, homepage e tópicos" -ForegroundColor Cyan
    $topics = @(
        'manga', 'manga-reader', 'manhwa', 'manhua', 'leitor-de-mangas',
        'windows', 'wpf', 'dotnet', 'csharp', 'portable'
    )
    $editArgs = @(
        'repo', 'edit', "$login/$RepoName",
        '--description', $Description,
        '--homepage', "https://github.com/$login/$RepoName/releases/latest"
    )
    foreach ($topic in $topics) { $editArgs += @('--add-topic', $topic) }
    $null = Invoke-Gh $editArgs

    if ($SkipRelease) {
        Write-Host ""
        Write-Host "Repositório publicado: https://github.com/$login/$RepoName" -ForegroundColor Green
        return
    }

    # ---------------------------------------------------------------- release
    $dist = Join-Path $root 'dist'
    $assetName = "Solo-Mangas-$version.exe"
    $releaseExe = Join-Path $dist $assetName

    if ($AssetPath) {
        if (-not (Test-Path -LiteralPath $AssetPath)) {
            throw "Executável informado não existe: $AssetPath"
        }
        $exe = (Resolve-Path -LiteralPath $AssetPath).Path
        $fileVersion = (Get-Item -LiteralPath $exe).VersionInfo.FileVersion
        if (-not $fileVersion.StartsWith($version)) {
            throw "Executável $fileVersion não corresponde à versão $version do csproj."
        }
        Write-Host "==> Usando executável existente: $exe" -ForegroundColor Cyan
        Copy-Item -LiteralPath $exe -Destination $releaseExe -Force
    }
    else {
        $exe = Join-Path $dist 'SoloMangas.exe'
        if (-not (Test-Path $exe)) {
            Write-Host "==> dist ausente; executando build" -ForegroundColor Cyan
            & (Join-Path $PSScriptRoot 'build.ps1')
        }
        Copy-Item $exe $releaseExe -Force
    }

    $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $releaseExe).Hash
    $size = (Get-Item $releaseExe).Length

    $updatePath = Join-Path $dist 'update.json'
    $notesPath = Join-Path $root "docs/releases/$Tag.update.txt"
    $notes = if (Test-Path $notesPath) { (Get-Content $notesPath -Raw).Trim() } else { "Versão $version" }
    $manifest = [ordered]@{
        version = $version
        url     = "https://github.com/$login/$RepoName/releases/download/$Tag/$assetName"
        sha256  = $hash
        size    = $size
        notes   = $notes
    }
    [IO.File]::WriteAllText($updatePath, ($manifest | ConvertTo-Json -Depth 3), (New-Object Text.UTF8Encoding($false)))

    $sumsPath = Join-Path $dist 'SHA256SUMS.txt'
    "$($hash.ToLowerInvariant())  $assetName" | Set-Content -Path $sumsPath -Encoding ASCII

    $assets = @($releaseExe, $sumsPath, $updatePath)

    # Tag
    $tagExists = (git tag --list $Tag) -contains $Tag
    if (-not $tagExists) {
        git tag -a $Tag -m "Solo Mangás $Tag"
        git push origin $Tag
        if ($LASTEXITCODE -ne 0) { throw 'Falha ao enviar a tag.' }
        Write-Host "==> Tag $Tag criada" -ForegroundColor Cyan
    }

    # Release
    $null = Invoke-Gh @('release', 'view', $Tag, '--repo', "$login/$RepoName")
    if ($script:ghLastExit -eq 0) {
        Write-Host "==> Release $Tag já existe; assets preservados (nenhuma sobrescrita)" -ForegroundColor Yellow
    }
    else {
        Write-Host "==> Criando release $Tag" -ForegroundColor Cyan
        $notesFile = Join-Path $root "docs/releases/$Tag.md"
        $createArgs = @('release', 'create', $Tag, '--repo', "$login/$RepoName", '--title', "Solo Mangás $version", '--latest')
        if (Test-Path $notesFile) { $createArgs += @('--notes-file', $notesFile) }
        else { $createArgs += @('--generate-notes') }
        $result = Invoke-Gh ($createArgs + $assets)
        $result | ForEach-Object { Write-Host $_ }
        if ($script:ghLastExit -ne 0) { throw 'Falha ao publicar a release.' }
    }

    Write-Host ""
    Write-Host "Repositório: https://github.com/$login/$RepoName" -ForegroundColor Green
    Write-Host "Release:     https://github.com/$login/$RepoName/releases/tag/$Tag" -ForegroundColor Green
}
finally {
    Pop-Location
}
