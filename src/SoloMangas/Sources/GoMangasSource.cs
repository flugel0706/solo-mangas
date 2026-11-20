using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HtmlAgilityPack;
using SoloMangas.Shared;

namespace SoloMangasNative
{
    /// <summary>
    /// GoMangas (PT-BR): catálogo e leitor renderizados no servidor (Next.js).
    /// A página da obra traz todos os capítulos num payload RSC no formato
    /// [número, título], e a página do leitor traz a lista ordenada de páginas.
    /// As imagens têm variantes m750/m1080; o leitor usa a maior até 1080 px
    /// para baixar menos e abrir mais rápido.
    /// </summary>
    public sealed class GoMangasSource : ISource
    {
        private const string Base = "https://gomangas.com";
        private const string ImageHost = "https://imagens.gomangas.com";
        private const string ChaptersMarker = "\\\"chapters\\\":[[";
        private const string PagesMarker = "\\\"pages\\\":[";

        private static readonly (string Name, string Slug)[] GenreSlugs =
        {
            ("Aventura", "aventura"), ("Ação", "acao"), ("Boys' Love", "boys-love"),
            ("Comédia", "comedia"), ("Crime", "crime"), ("Drama", "drama"), ("Ecchi", "ecchi"),
            ("Esportes", "esportes"), ("Fantasia", "fantasia"), ("Ficção Científica", "ficcao-cientifica"),
            ("Garotas Mágicas", "mahou-shoujo"), ("Girls' Love", "girls-love"), ("Hentai", "hentai"),
            ("Histórico", "historico"), ("Isekai", "isekai"), ("Mecha", "mecha"),
            ("Mistério", "misterio"), ("Médico", "medico"), ("Música", "musica"),
            ("Psicológico", "psicologico"), ("Romance", "romance"), ("Slice of Life", "slice-of-life"),
            ("Sobrenatural", "sobrenatural"), ("Super-heróis", "super-herois"), ("Suspense", "suspense"),
            ("Terror", "terror"), ("Tragédia", "tragedia"),
        };

        private static readonly HashSet<string> AdultGenres = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "hentai", "smut", "adulto", "adult", "+18", "18+", "pornografico", "pornográfico",
        };

        private static readonly TimeSpan DetailTtl = TimeSpan.FromMinutes(15);

        public string Key => "gomangas";
        public string Name => "GoMangas";
        public bool SupportsLogin => false;
        public bool AdultOnly => false;

        // ------------------------------------------------------------------
        // HTTP / util
        // ------------------------------------------------------------------
        private static Task<string> GetHtmlAsync(string url)
        {
            return HtmlHttp.GetAsync(url, Base + "/", 30000);
        }

        private static string Norm(string v)
        {
            if (string.IsNullOrEmpty(v)) return string.Empty;
            var s = v.Normalize(System.Text.NormalizationForm.FormD);
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (var c in s)
                if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) !=
                    System.Globalization.UnicodeCategory.NonSpacingMark) sb.Append(c);
            return Regex.Replace(sb.ToString(), "\\s+", " ").Trim().ToLowerInvariant();
        }

        private static string Txt(HtmlNode n) =>
            n == null ? string.Empty : Regex.Replace(HtmlEntity.DeEntitize(n.InnerText ?? string.Empty), "\\s+", " ").Trim();

        private static string Meta(HtmlDocument doc, string property)
        {
            var n = doc.DocumentNode.SelectSingleNode("//meta[@property='" + property + "']")
                    ?? doc.DocumentNode.SelectSingleNode("//meta[@name='" + property + "']");
            return n == null ? string.Empty : HtmlEntity.DeEntitize(n.GetAttributeValue("content", string.Empty)).Trim();
        }

        private static string Abs(string u)
        {
            if (string.IsNullOrWhiteSpace(u)) return string.Empty;
            u = u.Trim();
            if (u.StartsWith("//")) return "https:" + u;
            if (u.StartsWith("/")) return Base + u;
            return u;
        }

        private static string CoverFrom(HtmlNode img)
        {
            if (img == null) return string.Empty;
            var src = img.GetAttributeValue("src", string.Empty);
            if (string.IsNullOrWhiteSpace(src)) src = img.GetAttributeValue("data-src", string.Empty);
            if (string.IsNullOrWhiteSpace(src)) return string.Empty;

            // O Next serve as capas por /_next/image?url=<url codificada>.
            if (src.IndexOf("/_next/image", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                var m = Regex.Match(src, "[?&]url=([^&]+)");
                if (m.Success) src = Uri.UnescapeDataString(m.Groups[1].Value);
            }
            return IsPlaceholderCover(src) ? string.Empty : Abs(src);
        }

        /// <summary>
        /// O GoMangas usa a imagem "no-cover" do MangaLivre quando a obra não tem
        /// capa. Ela responde 403 com o Referer do GoMangas, então é melhor deixar
        /// o card com o placeholder local (letra sobre gradiente).
        /// </summary>
        private static bool IsPlaceholderCover(string url)
        {
            return !string.IsNullOrWhiteSpace(url) &&
                   url.IndexOf("no-cover", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool LooksAdult(string title, string description, IEnumerable<string> genres)
        {
            foreach (var g in genres ?? Enumerable.Empty<string>())
                if (AdultGenres.Contains(Norm(g))) return true;
            var text = Norm((title ?? string.Empty) + " " + (description ?? string.Empty));
            return Regex.IsMatch(text, "\\bhentai\\b|\\bsmut\\b|\\bnsfw\\b|\\+18\\b|\\b18\\+\\b");
        }

        private static double ParseNumber(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return 0;
            return double.TryParse(raw.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : 0;
        }

        private static string Humanize(string slug)
        {
            var text = (slug ?? string.Empty).Replace('-', ' ').Trim();
            return text.Length == 0 ? "Sem título" : char.ToUpper(text[0], CultureInfo.CurrentCulture) + text.Substring(1);
        }

        // ------------------------------------------------------------------
        // Cards do catálogo / busca
        // ------------------------------------------------------------------
        private static List<Manga> ParseCards(string html)
        {
            var outp = new List<Manga>();
            if (string.IsNullOrWhiteSpace(html)) return outp;

            var doc = new HtmlDocument();
            doc.LoadHtml(html);
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var a in doc.DocumentNode.SelectNodes("//a[starts-with(@href,'/manga/')]")
                     ?? Enumerable.Empty<HtmlNode>())
            {
                var href = (a.GetAttributeValue("href", string.Empty) ?? string.Empty).Trim();
                var slug = href.Substring("/manga/".Length).Trim('/');
                if (slug.Length == 0 || slug.IndexOf('/') >= 0 || slug.IndexOf('?') >= 0) continue;
                if (!seen.Add(slug)) continue;

                var img = a.SelectSingleNode(".//img");
                var title = img == null ? string.Empty : img.GetAttributeValue("alt", string.Empty);
                title = Regex.Replace(HtmlEntity.DeEntitize(title ?? string.Empty), "^Capa de\\s+", "", RegexOptions.IgnoreCase).Trim();
                if (title.Length == 0) title = Txt(a.SelectSingleNode(".//h3")) ?? string.Empty;
                if (title.Length == 0) title = Humanize(slug);

                outp.Add(new Manga
                {
                    Source = "gomangas",
                    Id = slug,
                    Title = title,
                    Cover = CoverFrom(img),
                    Url = Base + "/manga/" + slug,
                });
            }
            return outp;
        }

        private static bool HasNextPage(string html, int page)
        {
            if (string.IsNullOrEmpty(html)) return false;
            var next = page + 1;
            // Os links de paginação aparecem como \"?page=N\" ou \"&page=N\" no RSC.
            return Regex.IsMatch(html, "[?&](?:amp;)?page=" + next + "(?![0-9])");
        }

        // ------------------------------------------------------------------
        // Detalhe + capítulos (uma requisição)
        // ------------------------------------------------------------------
        private sealed class Detail
        {
            public DateTime At;
            public Manga Manga;
            public List<Chapter> Chapters = new List<Chapter>();
        }

        private readonly object _cacheGate = new object();
        private readonly Dictionary<string, Detail> _cache = new Dictionary<string, Detail>(StringComparer.Ordinal);
        private readonly Dictionary<string, Task<Detail>> _inflight = new Dictionary<string, Task<Detail>>(StringComparer.Ordinal);

        private async Task<Detail> DetailAsync(string slug)
        {
            Task<Detail> task;
            lock (_cacheGate)
            {
                if (_cache.TryGetValue(slug, out var hit) && DateTime.UtcNow - hit.At < DetailTtl) return hit;
                if (!_inflight.TryGetValue(slug, out task))
                {
                    task = LoadDetailAsync(slug);
                    _inflight[slug] = task;
                }
            }

            try
            {
                return await task.ConfigureAwait(false);
            }
            finally
            {
                lock (_cacheGate) _inflight.Remove(slug);
            }
        }

        private async Task<Detail> LoadDetailAsync(string slug)
        {
            var html = await GetHtmlAsync(Base + "/manga/" + Uri.EscapeDataString(slug)).ConfigureAwait(false);
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var book = FindBook(html);
            var title = book.HasValue ? JStr(book.Value, "name") : string.Empty;
            if (string.IsNullOrWhiteSpace(title)) title = Meta(doc, "og:title");
            if (string.IsNullOrWhiteSpace(title)) title = Txt(doc.DocumentNode.SelectSingleNode("//h1"));
            if (string.IsNullOrWhiteSpace(title)) title = Humanize(slug);

            var desc = book.HasValue ? JStr(book.Value, "description") : string.Empty;
            if (string.IsNullOrWhiteSpace(desc)) desc = Meta(doc, "og:description");
            if (string.IsNullOrWhiteSpace(desc)) desc = Meta(doc, "description");

            var cover = book.HasValue ? ImageOf(book.Value) : string.Empty;
            if (string.IsNullOrWhiteSpace(cover)) cover = ImageHost + "/covers/" + slug + ".jpg";
            if (IsPlaceholderCover(cover)) cover = string.Empty;

            var genres = new List<string>();
            if (book.HasValue && book.Value.TryGetProperty("genre", out var garr) && garr.ValueKind == JsonValueKind.Array)
            {
                foreach (var g in garr.EnumerateArray())
                {
                    if (g.ValueKind != JsonValueKind.String) continue;
                    var name = (g.GetString() ?? string.Empty).Trim();
                    if (name.Length > 0 && !genres.Contains(name, StringComparer.OrdinalIgnoreCase)) genres.Add(name);
                }
            }

            var year = 0;
            var published = book.HasValue ? JStr(book.Value, "datePublished") : string.Empty;
            if (published.Length >= 4) int.TryParse(published.Substring(0, 4), out year);

            var chapters = ParseChapters(html, slug);
            var manga = new Manga
            {
                Source = "gomangas",
                Id = slug,
                Title = title,
                Cover = cover,
                Description = desc,
                Status = MapStatus(RscValue(html, "status")),
                Url = Base + "/manga/" + slug,
                Adult = LooksAdult(title, desc, genres),
                Chapters = chapters.Count,
                Latest = chapters.LastOrDefault()?.Title ?? string.Empty,
                Year = year,
                Genres = genres,
            };

            var detail = new Detail { At = DateTime.UtcNow, Manga = manga, Chapters = chapters };
            lock (_cacheGate) _cache[slug] = detail;
            return detail;
        }

        private static JsonElement? FindBook(string html)
        {
            foreach (Match m in Regex.Matches(html, "<script[^>]*application/ld\\+json[^>]*>(.*?)</script>",
                         RegexOptions.Singleline | RegexOptions.IgnoreCase))
            {
                var raw = HtmlEntity.DeEntitize(m.Groups[1].Value).Trim();
                if (raw.Length == 0) continue;
                try
                {
                    using var doc = JsonDocument.Parse(raw);
                    var root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Object) continue;
                    var type = JStr(root, "@type");
                    if (type.Equals("Book", StringComparison.OrdinalIgnoreCase) ||
                        root.TryGetProperty("genre", out _) && root.TryGetProperty("numberOfPages", out _))
                        return root.Clone();
                }
                catch
                {
                    // JSON-LD opcional: segue para o próximo bloco.
                }
            }
            return null;
        }

        private static string JStr(JsonElement e, string name)
        {
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v)) return string.Empty;
            if (v.ValueKind == JsonValueKind.String) return v.GetString() ?? string.Empty;
            if (v.ValueKind == JsonValueKind.Number) return v.ToString();
            return string.Empty;
        }

        private static string ImageOf(JsonElement book)
        {
            if (!book.TryGetProperty("image", out var image)) return string.Empty;
            if (image.ValueKind == JsonValueKind.String) return image.GetString() ?? string.Empty;
            if (image.ValueKind == JsonValueKind.Array)
            {
                foreach (var i in image.EnumerateArray())
                    if (i.ValueKind == JsonValueKind.String) return i.GetString() ?? string.Empty;
            }
            if (image.ValueKind == JsonValueKind.Object) return JStr(image, "url");
            return string.Empty;
        }

        private static string MapStatus(string raw)
        {
            switch ((raw ?? string.Empty).Trim().ToUpperInvariant())
            {
                case "ONGOING": return "Em andamento";
                case "COMPLETED": return "Completo";
                case "HIATUS": return "Em hiato";
                case "CANCELLED":
                case "CANCELED": return "Cancelado";
                default: return raw ?? string.Empty;
            }
        }

        /// <summary>Lê um campo string do payload RSC, ex.: \"status\":\"ONGOING\".</summary>
        private static string RscValue(string html, string field)
        {
            var marker = "\\\"" + field + "\\\":\\\"";
            var i = html.IndexOf(marker, StringComparison.Ordinal);
            if (i < 0) return string.Empty;
            var start = i + marker.Length;
            var end = html.IndexOf("\\\"", start, StringComparison.Ordinal);
            return end > start ? html.Substring(start, end - start) : string.Empty;
        }

        private static List<Chapter> ParseChapters(string html, string slug)
        {
            var list = new List<Chapter>();
            var idx = html.IndexOf(ChaptersMarker, StringComparison.Ordinal);
            if (idx >= 0)
            {
                var start = idx + ChaptersMarker.Length;
                var end = html.IndexOf("]]", start, StringComparison.Ordinal);
                if (end > start)
                {
                    var body = html.Substring(start, end - start).Replace("\\\"", "\"");
                    try
                    {
                        using var doc = JsonDocument.Parse("[[" + body + "]]");
                        var pairs = new List<(string Number, string Title)>();
                        foreach (var pair in doc.RootElement.EnumerateArray())
                        {
                            if (pair.ValueKind != JsonValueKind.Array || pair.GetArrayLength() < 2) continue;
                            var number = pair[0].ValueKind == JsonValueKind.String ? pair[0].GetString() : pair[0].ToString();
                            var title = pair[1].ValueKind == JsonValueKind.String ? pair[1].GetString() : pair[1].ToString();
                            if (string.IsNullOrWhiteSpace(number) && string.IsNullOrWhiteSpace(title)) continue;
                            pairs.Add((number ?? string.Empty, (title ?? string.Empty).Trim()));
                        }

                        // O payload vem do mais novo para o mais antigo.
                        pairs.Reverse();
                        var numbered = pairs.Count(p => ParseNumber(p.Number) > 0);
                        if (numbered >= Math.Max(2, (int)Math.Ceiling(pairs.Count * 0.6)))
                            pairs = pairs.OrderBy(p => ParseNumber(p.Number)).ToList();

                        foreach (var p in pairs)
                        {
                            var number = p.Number.Trim();
                            if (number.Length == 0) continue;
                            list.Add(new Chapter
                            {
                                Id = "/ler/" + slug + "-capitulo-" + number,
                                Title = string.IsNullOrWhiteSpace(p.Title) ? "Capítulo " + number : p.Title,
                                Number = ParseNumber(number),
                            });
                        }
                    }
                    catch
                    {
                        // Cai no fallback por links do HTML.
                    }
                }
            }

            if (list.Count == 0)
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(html);
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var a in doc.DocumentNode.SelectNodes("//a[starts-with(@href,'/ler/" + slug + "-capitulo-')]")
                         ?? Enumerable.Empty<HtmlNode>())
                {
                    var href = (a.GetAttributeValue("href", string.Empty) ?? string.Empty).Trim();
                    var m = Regex.Match(href, "-capitulo-([0-9]+(?:\\.[0-9]+)?)$");
                    if (!m.Success || !seen.Add(m.Groups[1].Value)) continue;
                    var number = m.Groups[1].Value;
                    list.Add(new Chapter
                    {
                        Id = href,
                        Title = "Capítulo " + number,
                        Number = ParseNumber(number),
                    });
                }
                list = ChapterOrder.Ascending(list);
            }

            return list;
        }

        private static Manga Copy(Manga m) => new Manga
        {
            Source = m.Source,
            Id = m.Id,
            Title = m.Title,
            TitleAlt = m.TitleAlt,
            Cover = m.Cover,
            Description = m.Description,
            Status = m.Status,
            Url = m.Url,
            Latest = m.Latest,
            Adult = m.Adult,
            Chapters = m.Chapters,
            Year = m.Year,
            Genres = new List<string>(m.Genres),
        };

        // ------------------------------------------------------------------
        // ISource
        // ------------------------------------------------------------------
        public async Task<CatalogResult> CatalogAsync(SourceQuery query)
        {
            var page = new CatalogResult { Page = Math.Max(1, query.Page) };
            var q = (query.Q ?? string.Empty).Trim();
            var genre = (query.Genre ?? string.Empty).Trim();
            var genreSlug = string.Empty;
            if (genre.Length > 0 && Norm(genre) != "tudo" && Norm(genre) != "todos")
            {
                foreach (var g in GenreSlugs)
                    if (Norm(g.Name) == Norm(genre)) { genreSlug = g.Slug; break; }
                if (genreSlug.Length == 0) return page;
                if (!query.Adult && AdultGenres.Contains(Norm(genre))) return page;
            }

            string url;
            if (q.Length > 0)
            {
                url = Base + "/busca?q=" + Uri.EscapeDataString(q) + (page.Page > 1 ? "&page=" + page.Page : string.Empty);
            }
            else if (genreSlug.Length > 0)
            {
                // O parâmetro do site é "genre" (não "genero"); o slug é o mesmo do <option>.
                url = Base + "/mangas?genre=" + genreSlug + (page.Page > 1 ? "&page=" + page.Page : string.Empty);
            }
            else
            {
                url = Base + "/mangas" + (page.Page > 1 ? "?page=" + page.Page : string.Empty);
            }

            var html = await GetHtmlAsync(url).ConfigureAwait(false);
            page.Items = ParseCards(html);
            page.Total = TotalOf(html);
            page.HasMore = page.Items.Count > 0 && HasNextPage(html, page.Page);
            return page;
        }

        /// <summary>Lê o "N obras encontradas" do topo da listagem.</summary>
        private static int TotalOf(string html)
        {
            if (string.IsNullOrEmpty(html)) return 0;
            var m = Regex.Match(html, ">([0-9][0-9\\.]*)</span>\\s*<!-- -->obras encontradas");
            if (!m.Success) return 0;
            var raw = m.Groups[1].Value.Replace(".", string.Empty);
            return int.TryParse(raw, out var n) ? n : 0;
        }

        public async Task<Manga> MangaAsync(string id, bool adult)
        {
            var d = await DetailAsync(id).ConfigureAwait(false);
            return Copy(d.Manga);
        }

        public async Task<List<Chapter>> ChaptersAsync(string id)
        {
            var d = await DetailAsync(id).ConfigureAwait(false);
            return new List<Chapter>(d.Chapters);
        }

        public async Task<PageSet> PagesAsync(string token, bool adult)
        {
            var path = (token ?? string.Empty).Trim();
            if (path.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                path = Uri.TryCreate(path, UriKind.Absolute, out var uri) ? uri.PathAndQuery : path;
            if (!path.StartsWith("/", StringComparison.Ordinal)) path = "/" + path;

            var html = await GetHtmlAsync(Base + path).ConfigureAwait(false);
            var set = new PageSet();

            // Payload do leitor: \"pages\":[{"imageUrl":"...","srcSet":"... 1080w, ..."}]
            var search = 0;
            while (true)
            {
                var idx = html.IndexOf(PagesMarker, search, StringComparison.Ordinal);
                if (idx < 0) break;
                var start = idx + PagesMarker.Length;
                var end = html.IndexOf(']', start);
                if (end > start && html.IndexOf("imageUrl", start, end - start, StringComparison.Ordinal) >= 0)
                {
                    var body = html.Substring(start, end - start).Replace("\\\"", "\"");
                    try
                    {
                        using var doc = JsonDocument.Parse("[" + body + "]");
                        foreach (var p in doc.RootElement.EnumerateArray())
                        {
                            if (p.ValueKind != JsonValueKind.Object) continue;
                            var full = JStr(p, "imageUrl");
                            if (string.IsNullOrWhiteSpace(full)) continue;
                            var picked = PickFast(full.Trim(), JStr(p, "srcSet"));
                            if (!set.Images.Contains(picked, StringComparer.Ordinal)) set.Images.Add(picked);
                        }
                    }
                    catch
                    {
                        // Cai no fallback por URLs soltas.
                    }
                    if (set.Images.Count > 0) break;
                }
                search = idx + PagesMarker.Length;
            }

            if (set.Images.Count == 0)
            {
                foreach (Match m in Regex.Matches(html,
                             "https://imagens\\.gomangas\\.com/manga/[^\"\\\\\\s]+\\.(?:webp|jpg|jpeg|png)",
                             RegexOptions.IgnoreCase))
                {
                    var url = m.Value;
                    if (!set.Images.Contains(url, StringComparer.Ordinal)) set.Images.Add(url);
                }
            }

            if (set.Images.Count == 0) throw new Exception("Capítulo sem páginas disponíveis.");
            return set;
        }

        /// <summary>
        /// Escolhe a maior variante até 1080 px. Páginas estreitas (por exemplo
        /// 757 px) não têm m1080; nesse caso usa a própria imagem original.
        /// </summary>
        private static string PickFast(string full, string srcSet)
        {
            if (string.IsNullOrWhiteSpace(srcSet)) return full;
            string best = null;
            var bestWidth = -1.0;
            foreach (var part in srcSet.Split(','))
            {
                var bits = part.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (bits.Length < 2) continue;
                if (!bits[1].EndsWith("w", StringComparison.OrdinalIgnoreCase)) continue;
                if (!double.TryParse(bits[1].Substring(0, bits[1].Length - 1), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out var w) || w <= 0) continue;
                if (w > 1200) continue;
                if (w > bestWidth) { bestWidth = w; best = bits[0]; }
            }
            return string.IsNullOrWhiteSpace(best) ? full : best;
        }

        public Task<List<string>> GenresAsync() => Task.FromResult(GenreSlugs.Select(g => g.Name).ToList());
    }
}
