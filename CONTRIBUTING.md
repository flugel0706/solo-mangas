# Contribuindo

Obrigado pelo interesse em melhorar o Solo Mangás.

## Ambiente

1. Windows 10/11 x64.
2. [.NET SDK 8](https://dotnet.microsoft.com/download/dotnet/8.0).
3. Clone o repositório:

   ```powershell
   git clone https://github.com/flugel0706/solo-mangas.git
   cd solo-mangas
   ```

4. Build:

   ```powershell
   dotnet build src/SoloMangas/SoloMangas.csproj -c Release
   ```

5. Execute:

   ```powershell
   dotnet run --project src/SoloMangas/SoloMangas.csproj -c Release
   ```

## Padrões do código

- C# 12, .NET 8, WPF. Sem dependências além de HtmlAgilityPack, BouncyCastle e
  Magick.NET.
- Namespace principal `SoloMangasNative`; código compartilhado em `SoloMangas.Shared`.
- UI, mensagens e comentários voltados ao usuário em português.
- Não introduza telemetria, coleta de dados ou rede além das fontes.
- Preserve o modelo normalizado (`Manga`, `Chapter`, `PageSet`): a UI não deve
  conhecer detalhes de cada site.
- Preserve o formato do `store.json` e a pasta `%APPDATA%\MangeekPC` da sessão.

## O que testar antes de abrir um PR

- [ ] `dotnet build -c Release` sem erros.
- [ ] `.\scripts\build.ps1` gera o executável único e o `SHA256SUMS.txt`.
- [ ] `FileVersion` do `.exe` está correta.
- [ ] A janela abre com o título esperado.
- [ ] Catálogo, busca, obra, capítulos e leitor funcionam na fonte alterada.
- [ ] Favoritos, histórico e posição persistem após fechar e abrir.
- [ ] Modo +18 liga e desliga corretamente.
- [ ] Atualização simulada: manifesto lido, hash conferido e exe trocado.

## Commits e PRs

- Commits em português ou inglês, no imperativo e com escopo:
  `leitor: corrige zoom no modo página`.
- Um PR por assunto. Descreva o problema, a solução e como testou.
- Inclua captura de tela para mudanças visuais.
- Atualize `CHANGELOG.md` quando a mudança for visível ao usuário.

## Áreas que aceitam contribuição

- Correções de adaptadores quando uma fonte muda o layout.
- Novas fontes em português (seguindo `ISource`).
- Desempenho do leitor e do cache de imagens.
- Acessibilidade e navegação por teclado.
- Documentação e traduções.

## Reportando bugs

Use o template de bug e inclua:

- versão do app (Configurações → Sobre);
- versão do Windows;
- fonte e obra onde aconteceu;
- passos para reproduzir;
- comportamento esperado e observado;
- trecho de `%TEMP%\solo2.log` com `SOLO_DEBUG=1`.
