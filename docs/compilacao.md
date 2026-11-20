# Compilação

## Requisitos

- Windows 10/11 x64
- [.NET SDK 8](https://dotnet.microsoft.com/download/dotnet/8.0)
- Git (opcional, para clonar)

```powershell
git clone https://github.com/flugel0706/solo-mangas.git
cd solo-mangas
```

O projeto é WPF (`net8.0-windows`, `win-x64`) e não compila em Linux/macOS.

## Build de desenvolvimento

```powershell
dotnet build src/SoloMangas/SoloMangas.csproj -c Release
```

Saída em `src/SoloMangas/bin/Release/net8.0-windows/win-x64/`.
Esse build é *framework-dependent* e não gera um arquivo único.

## Publicação

O comando oficial gera o executável único autocontido (~83 MB):

```powershell
dotnet publish src/SoloMangas/SoloMangas.csproj -c Release -r win-x64 `
  --self-contained true -p:PublishSingleFile=true -o dist
```

O `csproj` já define `SelfContained`, `PublishSingleFile`,
`IncludeNativeLibrariesForSelfExtract` e `EnableCompressionInSingleFile`; os
parâmetros explícitos deixam o comando autoexplicativo.

### Script pronto

```powershell
.\scripts\build.ps1                 # publica em dist/
.\scripts\build.ps1 -Output out     # outro diretório
.\scripts\build.ps1 -SkipBuild      # só valida o exe já publicado
```

O script:

1. roda `dotnet publish` em Release/win-x64;
2. confere que `SoloMangas.exe` existe e tem tamanho plausível;
3. lê `FileVersion` e falha se não for `2.9.1.0`;
4. grava `dist\SHA256SUMS.txt`.

## Versão

A versão fica em `src/SoloMangas/SoloMangas.csproj`:

```xml
<Version>2.9.1</Version>
<AssemblyVersion>2.9.1.0</AssemblyVersion>
<FileVersion>2.9.1.0</FileVersion>
```

`UpdateService.CurrentVersion` lê a versão do assembly; `FileVersion` é o que
aparece nas propriedades do `.exe`. Mantenha as três em sincronia.

## Verificação local

```powershell
# versão do executável
(Get-Item .\dist\SoloMangas.exe).VersionInfo.FileVersion

# hash
Get-FileHash .\dist\SoloMangas.exe -Algorithm SHA256

# abrir o app
.\dist\SoloMangas.exe
```

O app não tem suíte de testes automatizados. A validação de uma mudança é:

- [ ] `dotnet build -c Release` sem erros.
- [ ] `dotnet publish` gera `SoloMangas.exe` único.
- [ ] `FileVersion` correta.
- [ ] janela abre com o título `Solo Mangás 2.9.1`.
- [ ] catálogo, busca, obra, leitor e capítulos funcionam na fonte alterada.
- [ ] favoritos, histórico e posição de leitura persistem após fechar e abrir.
- [ ] modo +18 liga/desliga as fontes adultas.
- [ ] atualização: manifesto lido, hash conferido, exe trocado e app reaberto.

## Integração contínua

### `build.yml`

Roda em todo push/PR:

1. `dotnet restore` e `dotnet build -c Release`.
2. `dotnet publish` single-file autocontido.
3. Confere `FileVersion` (`2.9.1.0`) e tamanho do executável.
4. Sobe `SoloMangas.exe` como artefato do workflow.

### `release.yml`

Disparado por tags `v*`:

1. Publica o executável autocontido.
2. Renomeia para `Solo-Mangas-<versão>.exe`.
3. Gera `SHA256SUMS.txt` e `update.json` com URL absoluta do asset.
4. Cria a release com os três arquivos, caso ela ainda não exista — releases já
   publicadas nunca têm os assets sobrescritos.

Para lançar uma versão:

```powershell
git tag v2.9.1
git push origin v2.9.1
```

## Publicação manual no GitHub

```powershell
.\scripts\publish-github.ps1 -RepoName solo-mangas
```

O script confere o `gh auth status`, cria o repositório se necessário, configura
descrição e tópicos, faz push da `main`, cria a tag e publica a release com o
executável, o `SHA256SUMS.txt` e o `update.json`.

Requisitos: [GitHub CLI](https://cli.github.com/) autenticado e Git configurado.

## Assinatura de código (opcional)

O executável não é assinado. Para distribuição pública, assine o binário para
evitar o aviso de "editor desconhecido":

```powershell
signtool sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 `
  /a dist\SoloMangas.exe
```

Assinatura não altera o comportamento do app.
