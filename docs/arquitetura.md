# Arquitetura

## Visão geral

```text
┌──────────────────────────── SoloMangas.exe (WPF, .NET 8) ────────────────────────────┐
│                                                                                      │
│  UI thread (WPF)                              Threads de trabalho                    │
│  ┌──────────────────────────────┐             ┌─────────────────────────────┐        │
│  │ MainWindow                   │             │ HttpClient / ImageLoader    │        │
│  │  ├─ barra lateral            │             │  ├─ download (10 paralelos)  │        │
│  │  ├─ HomePage                 │             │  ├─ decode (4 paralelos)     │        │
│  │  ├─ CatalogPage              │             │  └─ cache memória + disco    │        │
│  │  ├─ MangaPage                │             └─────────────────────────────┘        │
│  │  ├─ ReaderPage               │                                                     │
│  │  ├─ FavoritesPage            │             ┌─────────────────────────────┐        │
│  │  ├─ HistoryPage              │             │ UpdateService                │        │
│  │  ├─ LoginPage                │             │  ├─ manifesto update.json    │        │
│  │  └─ SettingsPage             │             │  ├─ download + SHA-256       │        │
│  └──────────────┬───────────────┘             │  └─ --apply-update           │        │
│                 │                             └─────────────────────────────┘        │
│  ┌──────────────▼───────────────┐             ┌─────────────────────────────┐        │
│  │ SourceRegistry               │             │ LocalStore                   │        │
│  │  ├─ MangeekSource            │             │  ├─ store.json               │        │
│  │  ├─ ManhastroSource          │             │  ├─ favoritos / histórico    │        │
│  │  ├─ LerMangaSource           │             │  ├─ posições de leitura      │        │
│  │  ├─ MangaDexSource           │             │  └─ configurações            │        │
│  │  ├─ MangaFlixSource          │             └─────────────────────────────┘        │
│  │  ├─ MangaLivreSource         │                                                     │
│  │  └─ 4 fontes +18             │                                                     │
│  └──────────────────────────────┘                                                     │
└──────────────────────────────────────────────────────────────────────────────────────┘
```

## Inicialização

1. `Program.Main` trata `--apply-update` (modo aplicador) e encerra sem abrir a UI.
2. Cria `Application`, registra o handler de exceções não tratadas.
3. `Theme.Init`, `LocalStore.Load`, `SourceRegistry` (que carrega a sessão Mangeek).
4. `MainWindow` abre, restaura geometria e fonte selecionada.
5. A verificação de atualização roda 1,8 s depois, se habilitada.

## Fluxo de uma consulta

1. A página chama `SourceRegistry.Get(key)` e o método correspondente
   (`CatalogAsync`, `MangaAsync`, `ChaptersAsync`, `PagesAsync`, `GenresAsync`).
2. O adaptador faz a requisição HTTP (API JSON ou HTML + HtmlAgilityPack) e
   normaliza o resultado em `Manga`, `Chapter` ou `PageSet`.
3. A página renderiza cards ou a lista; a UI nunca vê JSON/HTML cru.
4. `ImageLoader` baixa capas e páginas sob demanda, com cache em memória e disco.

A busca global dispara todas as fontes visíveis em paralelo e entrega os
resultados por fonte assim que cada uma responde; os resultados ficam em cache
por 2,5 minutos.

## Leitor

- `ReaderPage` monta a lista de imagens do capítulo e usa `VirtualizingStackPanel`
  com carregamento por interseção com a viewport.
- O modo webtoon rola continuamente; o modo página usa um `ScrollViewer` com
  snap por página.
- A posição é gravada por `Source:MangaId` e retomada ao reabrir.
- A leitura contínua (`ContinuousRead`) carrega o próximo capítulo ao chegar ao fim.
- Zoom altera a largura máxima das imagens; `Ctrl` + roda e `A−`/`A+` chamam `SetZoom`.

## Cache de imagens (`ImageLoader`)

- **Memória:** LRU por URL + largura, limite de 360 itens e orçamento de
  ~480 MB de pixels decodificados.
- **Bytes originais:** LRU por URL (sem largura), 300 itens / 72 MB.
- **HEIF/AVIF:** cache separado de 20 MB após conversão via Magick.NET.
- **Disco:** bytes originais em `%LOCALAPPDATA%\SoloMangas\cache\img`, teto de
  1,2 GB; a limpeza roda em segundo plano e remove os arquivos mais antigos.
- **Paralelismo:** 10 downloads e 4 decodificações simultâneas, com semáforos
  independentes. Requisições repetidas compartilham a mesma task (`InFlight`).

## Persistência (`LocalStore`)

- Um único `store.json` em `%APPDATA%\SoloMangas`.
- `SaveSoon()` agenda a gravação (debounce) para não escrever a cada evento de
  rolagem; `Flush()` força a gravação antes de backups.
- Leitura tolerante: JSON corrompido não derruba o app; volta ao estado padrão.
- Estrutura: `Settings`, `Favorites`, `History`, `Searches`, `Read`, `Positions`.

## Atualizações (`UpdateService`)

1. `ResolveManifest(source)` decide entre URL, pasta, arquivo ou `update.json`
   ao lado do executável.
2. `ParseManifest` aceita comentários e vírgula final; valida `version`, `url`,
   `sha256` e `size`.
3. `DownloadAsync` grava em `%TEMP%\SoloMangas-update`, reporta progresso e
   calcula o SHA-256 durante a cópia.
4. `ApplyUpdateAndExit` (segunda instância) espera o PID antigo, copia o novo
   exe sobre o alvo com tentativas e reabre o app.

## Mapa de arquivos

| Arquivo | Responsabilidade |
|---|---|
| `Program.cs` | Entrada, `--apply-update`, exceções globais |
| `MainWindow.cs` | Shell, barra lateral, navegação, tela cheia, atalhos |
| `Pages/HomePage.cs` | Destaques, continuar lendo, seções da fonte |
| `Pages/CatalogPage.cs` | Catálogo, busca global, gêneros, paginação |
| `Pages/MangaPage.cs` | Sinopse, gêneros, capítulos, favoritos |
| `Pages/ReaderPage.cs` | Leitor, zoom, modos, painel de capítulos |
| `Pages/FavoritesPage.cs` | Lista de favoritos |
| `Pages/HistoryPage.cs` | Histórico e retomada |
| `Pages/LoginPage.cs` | Login Google da Mangeek e sessão |
| `Pages/SettingsPage.cs` | Preferências, dados, cache, atualizações |
| `Sources/*.cs` | Um adaptador por fonte |
| `SourceRegistry.cs` | Registro e visibilidade das fontes |
| `Models.cs` | Modelos normalizados e interfaces de página |
| `ImageLoader.cs` | Download, decodificação, cache e limpeza |
| `LocalStore.cs` | Estado local e gravação agendada |
| `UpdateService.cs` | Manifesto, download, hash e troca do exe |
| `UpdateWindow.cs` | UI de atualização |
| `MangaCard.cs` | Card de obra com capa assíncrona |
| `Theme.cs` | Tema escuro, estilos e controles |
| `Log.cs` | Log opcional (`SOLO_DEBUG=1`) |
| `src/Shared/HttpUtil.cs` | Cliente HTTP com UA de navegador, gzip/br e timeout |
| `src/Shared/AppPaths.cs` | Pastas de sessão e dados legados |
| `src/Shared/SessionStore.cs` | Sessão Mangeek em `session.json` |
| `src/Shared/MangeekApi.cs` | API assinada da Mangeek |
| `src/Shared/ManhastroApi.cs` | API do Manhastro |
| `src/Shared/LerMangaApi.cs` | REST do tema do Ler Mangá |

## Decisões de projeto

- **WPF puro, sem WebView2:** telas, leitor e biblioteca construídos em WPF;
  nenhum navegador embutido, nenhum runtime extra.
- **Executável único autocontido:** o usuário baixa um arquivo e executa.
- **Modelo normalizado:** cada fonte resolve suas particularidades; a UI é genérica.
- **Cache agressivo, mas limitado:** memória com orçamento de pixels e disco com
  teto de 1,2 GB; nada cresce sem controle.
- **Atualização sem instalador:** o próprio app troca o executável, preservando
  os dados do usuário.
- **Falha silenciosa no cache/log:** cache e log nunca derrubam a leitura.

## Limitações conhecidas

- Fontes são sites públicos; layout/API podem mudar e quebrar um adaptador.
- Cloudflare e bloqueios regionais podem impedir uma fonte específica.
- O app não contorna paywall, login pago ou assinatura.
- Não há suíte de testes automatizados; a validação é build + execução manual.
- O executável não é assinado digitalmente; o Windows pode exibir aviso de editor.
