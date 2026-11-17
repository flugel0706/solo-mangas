# Uso

Guia completo do Solo Mangás. Para instalação rápida, veja o [README](../README.md).

## Instalação

O Solo Mangás é portátil: baixe `Solo-Mangas-2.7.0.exe` e execute. Não há
instalador, serviço ou driver.

- O executável é **autocontido**: não precisa de .NET nem WebView2.
- O app grava os dados em `%APPDATA%\SoloMangas\store.json`.
- A sessão Mangeek fica em `%APPDATA%\MangeekPC\session.json` (mesmo diretório
  das versões anteriores, para preservar login e leituras).
- O cache de imagens fica em `%LOCALAPPDATA%\SoloMangas\cache\img`.

Mantenha o `.exe` numa pasta em que você tenha permissão de escrita (Área de
Trabalho, Documentos, `D:\...`). O atualizador precisa substituir o próprio
arquivo; em `Arquivos de Programas` o app avisa antes de tentar.

## Primeira execução

1. Abra o app. A tela inicial carrega o catálogo da fonte selecionada.
2. Use a busca no topo ou navegue pelas seções (destaques, em alta, novidades).
3. Clique numa obra para ver sinopse, gêneros e capítulos.
4. Clique num capítulo para abrir o leitor.
5. A posição de leitura é salva automaticamente e aparece em **Continuar lendo**.

### Login Mangeek (opcional)

A fonte **Mangeek** usa a mesma conta do app Android:

1. Abra **Login** na barra lateral.
2. Clique em **Entrar com Google**.
3. Faça login na janela do próprio app.
4. O redirecionamento é interceptado, o código é trocado pelo JWT e a janela fecha.

As demais fontes não exigem conta.

## Fontes e modo +18

A barra lateral lista as fontes disponíveis. As fontes exclusivamente +18 ficam
ocultas até o modo adulto ser ligado no botão **＋18** da barra lateral ou em
**Configurações → Conteúdo +18**.

Com o modo ligado:

- obras adultas aparecem nas fontes normais que têm esse filtro;
- **Hanami Heaven**, **HQ Brasa**, **Melhor Hentai** e **MangaDex +18** entram na lista.

O filtro é global e vale para catálogo, busca, gêneros e histórico.

## Busca

- **Busca global** (padrão): consulta todas as fontes visíveis ao mesmo tempo.
  Cada fonte mostra seus resultados assim que responde, e títulos repetidos são
  agrupados. Os resultados ficam em cache por 2,5 minutos.
- **Busca por fonte**: desative a busca global em **Configurações** para consultar
  apenas a fonte atual.
- `Ctrl+F` ou `Ctrl+K` abre o catálogo com o cursor na busca.

## Leitor

Ao abrir um capítulo:

| Controle | Ação |
|---|---|
| `C` | Abre/fecha o painel de capítulos |
| `M` | Alterna webtoon (rolagem) e página a página |
| `←` / `→` | Capítulo anterior/próximo; no modo página, página anterior/próxima |
| `Espaço` / `PageDown` | Avança uma tela |
| `PageUp` | Volta uma tela |
| `Home` / `End` | Início/fim do capítulo |
| `Ctrl` + roda | Zoom |
| `A−` / `A+` | Zoom pelos botões da barra |
| `F11` | Tela cheia |
| `Esc` | Fecha o painel, sai da tela cheia ou volta |

**Leitura contínua** (Configurações → Leitor) emenda o próximo capítulo no fim
do atual, sem trocar de tela. A barra de progresso acompanha a rolagem.

## Biblioteca

- **Favoritos** — coração na página da obra. Lista em **Favoritos** na barra lateral.
- **Histórico** — últimas obras lidas, com capítulo e posição.
- **Continuar lendo** — retoma a obra de onde parou.
- O progresso é gravado por obra e por capítulo.

## Configurações

| Opção | Efeito |
|---|---|
| Conteúdo +18 | Libera obras e fontes adultas |
| Título preferido | Português ou nome original, quando a fonte oferece os dois |
| Busca global | Consulta todas as fontes de uma vez |
| Leitura contínua | Emenda o próximo capítulo no fim do atual |
| Largura da página | Automática, compacta, confortável ou ampla |
| Modo de leitura | Webtoon ou página a página |
| Pasta de dados | Abre `%APPDATA%\SoloMangas` no Explorer |
| Backup dos dados | Exporta favoritos, histórico, posições e preferências em JSON |
| Cache de imagens | Mostra o tamanho e permite limpar |
| Histórico / favoritos | Limpeza com confirmação |
| Verificar atualizações | Abre a janela de atualização |
| Verificar ao iniciar | Consulta a origem em segundo plano ao abrir |
| Origem das atualizações | URL, pasta ou manifesto local |
| Fontes | Ativa/desativa fontes individuais |

## Dados e backup

| Caminho | Conteúdo |
|---|---|
| `%APPDATA%\SoloMangas\store.json` | Estado completo (favoritos, histórico, buscas, posições, configurações) |
| `%APPDATA%\MangeekPC\session.json` | Token e dados da conta Mangeek |
| `%LOCALAPPDATA%\SoloMangas\cache\img` | Cache de imagens |

O backup em **Configurações → Backup dos dados** copia o `store.json` para o
arquivo escolhido. Para restaurar, feche o app e copie o backup de volta para
`%APPDATA%\SoloMangas\store.json`.

## Solução de problemas

**Uma fonte não carrega**
A fonte pode estar fora do ar, com Cloudflare ou mudado o layout. Teste outra
fonte e veja o log com `SOLO_DEBUG=1` (arquivo `%TEMP%\solo2.log`).

**A capa fica sem imagem**
O app mostra uma letra de reserva quando a fonte não devolve a imagem. Limpar o
cache em Configurações e reabrir a obra costuma resolver.

**O app não atualiza**
Confira a origem em Configurações → Atualizações. O manifesto precisa estar
acessível e o `sha256` precisa bater com o arquivo. Se a pasta do executável não
for gravável, mova o app para outra pasta.

**A leitura não continua de onde parou**
Verifique se o `store.json` está gravável e se o histórico não foi limpo. O app
salva a posição de forma agendada; fechar à força logo após rolar pode perder
os últimos segundos.

**Antivírus reclama do executável**
O app baixa imagens e se auto-atualiza, comportamentos que podem disparar
heurística. O código é aberto; compile você mesmo e confira o hash publicado.
