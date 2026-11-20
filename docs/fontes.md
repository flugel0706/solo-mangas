# Fontes

Todas as fontes implementam o mesmo contrato (`ISource`) e devolvem modelos
normalizados (`Manga`, `Chapter`, `PageSet`). A interface nunca conhece detalhes
de HTML ou de API: cada adaptador resolve catálogo, busca, gêneros, obra,
capítulos e páginas.

## Contrato

```csharp
public interface ISource
{
    string Key { get; }
    string Name { get; }
    bool SupportsLogin { get; }
    bool AdultOnly { get; }

    Task<CatalogResult> CatalogAsync(SourceQuery query);
    Task<Manga> MangaAsync(string id, bool adult);
    Task<List<Chapter>> ChaptersAsync(string id);
    Task<PageSet> PagesAsync(string token, bool adult);
    Task<List<string>> GenresAsync();
}
```

- `CatalogAsync` recebe página, limite, termo de busca, gênero e o modo adulto.
- `PagesAsync` recebe um token opaco (id do capítulo, URL ou slug) definido pelo
  próprio adaptador.
- `AdultOnly` mantém a fonte fora da lista até o modo +18 ser ligado.

## Fontes incluídas

### Mangeek (`MangeekSource`)

- API do app Android, assinada (v2), em `geekstations.com.br`.
- Home, continuar lendo, em alta, top da semana, novidades, categorias,
  similares, perfil, loja e moedas.
- Login Google com interceptação do callback `geekstations.com.br/api/v1/oauth/google/callback`.
- Sessão em `%APPDATA%\MangeekPC\session.json`, compartilhada com as versões
  anteriores.
- É a única fonte com conta e sincronização entre dispositivos.

### Manhastro (`ManhastroSource`)

- API pública `api.manhastro.net`.
- Catálogo paginado, ordenação, busca, detalhe, capítulos e páginas.
- Escopo `/adulta` para o modo +18.
- A lista de gêneros é montada apenas com valores que retornam obras (46).

### Ler Mangá Online (`LerMangaSource`)

- Catálogo HTML com tema próprio.
- Metadados pelo REST do WordPress (`mbi_manga`).
- Capítulos pelos endpoints públicos do tema (`mlo_chapter_batch`, `mbi_chapter`).
- Gêneros BL/GL usam slugs `boys-love`, `girls-love` e `shounen-ai`.

### MangaDex (`MangaDexSource`)

- API pública `api.mangadex.org`, somente obras com capítulos em `pt-br`.
- A API recusa User-Agent de navegador; o app aplica o UA de cliente por requisição.
- Cliente TLS próprio (`MangaDexHttp`) para os endpoints que exigem HTTP/2 e
  TLS 1.3 com as suites aceitas pela API.
- Requisições espaçadas (≈230 ms), até três tentativas em 429/5xx e respeito ao
  `Retry-After`; catálogo e capítulos são sempre em `pt-br`, sem fallback para
  inglês.
- A variante `mangadex18` reutiliza o mesmo adaptador com o escopo adulto.

### MangaFlix (`MangaFlixSource`)

- Catálogo SSR em `mangaflix.net/br/browse` e `/br/categories/<id>?page=N`.
- Busca, detalhe, capítulos e páginas pela API pública `api.mangaflix.net/v1`
  com `selected_language=pt-BR`.
- Categorias adultas (Hentai, Bara, Adulto, +18, Maduro) ficam ocultas até o
  modo +18 ser ligado.

### MangaLivre (`MangaLivreSource`)

- Catálogo WordPress em `mangalivre.blog/manga/?ordem=popular` (paginado).
- Busca em `/?s=`; gêneros em `/genre/<slug>/page/N/`.
- Metadados na página da obra; capítulos pelo REST público
  `/wp-json/slimeread/v1/chapters/<id>?offset=N` (30 por requisição).
- O leitor lê as imagens de `/capitulo/<slug>/`.

### Suki Mangás (`SukiSource`)

- API JSON própria em `sukimangas.com/api/mangas`; o catálogo inteiro vem numa
  requisição e é reutilizado por busca e gêneros (cache de 10 minutos).
- Busca e filtro de gênero são aplicados localmente sobre o catálogo.
- Detalhe em `/api/mangas/{id}` com a lista de capítulos e as páginas de cada
  capítulo (`pages`, com fallback para `pagesLeves`).
- O token do capítulo é `obra|capítulo`, estável para histórico e favoritos.
- Capas e páginas ficam num CDN público (R2).

### GoMangas (`GoMangasSource`)

- Catálogo SSR em `gomangas.com/mangas?page=N`, busca em `/busca?q=` e filtro
  de gênero em `/mangas?genre=<slug>`.
- A página da obra traz todos os capítulos num payload RSC
  (`"chapters":[["N","Capítulo N"],…]`); o detalhe usa o JSON-LD `Book` para
  capa, sinopse e gêneros.
- O leitor lê o array `"pages"` do payload e escolhe a maior variante até
  1080 px (`m750`/`m1080`) para reduzir banda sem perder legibilidade.
- Obras sem capa usam o placeholder local do card, porque a imagem "no-cover"
  do site responde 403 com o Referer do GoMangas.

### Fontes exclusivamente +18

| Fonte | Site | Tipo |
|---|---|---|
| **Hanami Heaven** | Madara/WordPress em português | catálogo "todas-as-obras", busca e gênero por querystring |
| **HQ Brasa** | `hqbrasa.com` | catálogo em `/lista`, busca em `/busca?q=`, imagens em CDN próprio |
| **Melhor Hentai** | `melhorhentai.com` | cada obra é um one-shot com as páginas na própria página |
| **MangaDex +18** | `api.mangadex.org` | mesma API, escopo adulto |

## Adicionar uma fonte

1. Crie `Sources/MinhaFonteSource.cs` implementando `ISource`.
2. Registre em `SourceRegistry` com uma `Key` estável (ela é gravada no
   histórico e nos favoritos).
3. Defina `AdultOnly = true` se a fonte for exclusivamente adulta.
4. Use `HttpUtil` (shared) ou `HtmlHttp` para as requisições, com User-Agent de
   navegador quando o site exigir.
5. Normalize os campos em `Manga`, `Chapter` e `PageSet`; a UI não deve precisar
   de casos especiais.
6. Teste catálogo, busca, gêneros, obra, capítulos e páginas com o modo +18
   ligado e desligado.

## Limitações

- As fontes são sites públicos e podem mudar sem aviso; um adaptador quebrado
  não derruba o app, mas aquela fonte para de responder.
- Cloudflare e bloqueios regionais podem exigir ajustes no cliente HTTP.
- O app não contorna paywall, login pago ou assinatura.
