using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HtmlAgilityPack;
using SoloMangas.Shared;

namespace SoloMangasNative
{
    /// <summary>
    /// MangaLivre (WordPress + leitor Slimeread). A lista completa de capítulos já
    /// vem no HTML da obra; o antigo endpoint wp-json deixou de existir, então a
    /// página é baixada uma única vez e reaproveitada por detalhe e capítulos.
    /// </summary>
    public sealed class MangaLivreSource : ISource
    {
        private const string Base = "https://mangalivre.blog";

        private static readonly (string Name, string Slug)[] GenreSlugs =
        {
            ("Ação", "acao"), ("Aventura", "aventura"), ("Comédia", "comedy"), ("Drama", "drama"),
            ("Fantasia", "fantasia"), ("Romance", "romance"), ("Shounen", "shounen"),
            ("Artes Marciais", "artes-marciais"), ("Slice of Life", "slice-of-life"),
        };

        public string Key => "mangalivre";
        public string Name => "MangaLivre";
        public bool SupportsLogin => false;
        public bool AdultOnly => false;

        // ------------------------------------------------------------------
        // HTTP / util
        // ------------------------------------------------------------------
        private static async Task<string> GetTextAsync(string url)
        {
            string lastError = null;
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var res = await HttpUtil.RequestAsync(url, headers: new Dictionary<string, string>
                {
                    ["Accept"] = "text/html,application/xhtml+xml",
                    ["Accept-Language"] = "pt-BR,pt;q=0.9,en;q=0.7",
                    ["Referer"] = Base + "/",
                }, timeoutMs: 25000, userAgent: HttpUtil.ChromeUa).ConfigureAwait(false);

                if (res.Status >= 200 && res.Status < 300) return res.Raw ?? string.Empty;

                lastError = res.Status == 0
                    ? (string.IsNullOrWhiteSpace(res.Error) ? "sem resposta" : res.Error)
                    : "HTTP " + res.Status;
                if (res.Status == 404) break;
                if (attempt == 0) await Task.Delay(350).ConfigureAwait(false);
            }
            throw new Exception("MangaLivre: " + (lastError ?? "falha de rede"));
        }

        private static string Abs(string u)
        {
            if (string.IsNullOrWhiteSpace(u)) return string.Empty;
            u = u.Trim();
            if (u.StartsWith("//")) return "https:" + u;
            if (u.StartsWith("/")) return Base + u;
            return u;
        }

        private static string SlugOf(string href)
        {
            var m = Regex.Match(href ?? string.Empty, "/manga/([^/?#]+)");
            return m.Success ? m.Groups[1].Value : string.Empty;
        }

        private static string Txt(HtmlNode n) =>
            n == null ? string.Empty : Regex.Replace(HtmlEntity.DeEntitize(n.InnerText ?? string.Empty), "\\s+", " ").Trim();

        private static string Attr(HtmlNode n, string name)
        {
            if (n == null) return string.Empty;
            var v = n.GetAttributeValue(name, string.Empty);
            return string.IsNullOrWhiteSpace(v) ? string.Empty : v.Trim();
        }

        private static string Norm(string v)
        {
            if (string.IsNullOrEmpty(v)) return string.Empty;
            var s = v.Normalize(System.Text.NormalizationForm.FormD);
            var sb = new System.Text.StringBuilder();
            foreach (var c in s)
                if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
                    sb.Append(c);
            return Regex.Replace(sb.ToString(), "\\s+", " ").Trim().ToLowerInvariant();
        }

        private static bool LooksAdult(string title, string description, IEnumerable<string> genres)
        {
            foreach (var g in genres ?? Enumerable.Empty<string>())
                if (Regex.IsMatch(Norm(g), "adult|hentai|smut|ecchi|\\+18")) return true;
            var text = Norm((title ?? string.Empty) + " " + (description ?? string.Empty));
            return Regex.IsMatch(text, "\\bhentai\\b|\\bsmut\\b|\\bnsfw\\b|\\becchi\\b|\\+18\\b");
        }

        // ------------------------------------------------------------------
        // Cards
        // ------------------------------------------------------------------
        private static List<Manga> ParseCards(HtmlDocument doc)
        {
            var outp = new List<Manga>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var card in doc.DocumentNode.SelectNodes("//*[contains(@class,'home-manga-card') or contains(@class,'manga-card')]")
                     ?? Enumerable.Empty<HtmlNode>())
            {
                var a = card.SelectSingleNode(".//a[contains(@class,'home-manga-cover') and contains(@href,'/manga/')]")
                        ?? card.SelectSingleNode(".//a[contains(@class,'manga-card-link') and contains(@href,'/manga/')]")
                        ?? card.SelectSingleNode(".//a[contains(@href,'/manga/')]");
                if (a == null) continue;
                var href = Abs(a.GetAttributeValue("href", string.Empty));
                var slug = SlugOf(href);
                if (string.IsNullOrEmpty(slug) || !seen.Add(slug)) continue;
                var img = card.SelectSingleNode(".//img");
                var titleEl = card.SelectSingleNode(".//h3//a") ?? card.SelectSingleNode(".//*[contains(@class,'manga-card-title')]")
                              ?? card.SelectSingleNode(".//h3");
                var title = Txt(titleEl);
                if (string.IsNullOrWhiteSpace(title) && img != null)
                    title = Regex.Replace(img.GetAttributeValue("alt", string.Empty), "\\s+", " ").Trim();
                var desc = Txt(card.SelectSingleNode(".//p"));
                outp.Add(new Manga
                {
                    Source = "mangalivre",
                    Id = slug,
                    Title = string.IsNullOrWhiteSpace(title) ? "Sem titulo" : title,
                    Cover = img == null ? string.Empty : Abs(Attr(img, "src") is var s && s.Length > 0
                        ? s : Attr(img, "data-src")),
                    Description = desc,
                    Url = href,
                    Latest = Txt(card.SelectSingleNode(".//*[contains(@class,'latest-chapter')]")),
                    Adult = LooksAdult(title, desc, null),
                });
            }
            return outp;
        }

        public async Task<CatalogResult> CatalogAsync(SourceQuery query)
        {
            var page = new CatalogResult { Page = Math.Max(1, query.Page) };
            var q = (query.Q ?? string.Empty).Trim();
            var genre = (query.Genre ?? string.Empty).Trim();
            string url;

            if (q.Length > 0)
            {
                url = Base + "/?s=" + Uri.EscapeDataString(q) + (page.Page > 1 ? "&paged=" + page.Page : string.Empty);
            }
            else if (genre.Length > 0 && Norm(genre) != "tudo")
            {
                var hit = GenreSlugs.FirstOrDefault(g => Norm(g.Name) == Norm(genre));
                if (string.IsNullOrEmpty(hit.Slug)) return page;
                url = Base + "/genre/" + hit.Slug + "/" + (page.Page > 1 ? "page/" + page.Page + "/" : string.Empty);
            }
            else
            {
                url = page.Page > 1 ? Base + "/manga/page/" + page.Page + "/?ordem=popular" : Base + "/manga/?ordem=popular";
            }

            var html = await GetTextAsync(url).ConfigureAwait(false);
            var doc = new HtmlDocument();
            doc.LoadHtml(html);
            page.Items = ParseCards(doc);
            var next = doc.DocumentNode.SelectSingleNode("//link[@rel='next']")
                       ?? doc.DocumentNode.SelectSingleNode("//a[contains(@class,'next') and contains(@class,'page-numbers')]");
            page.HasMore = next != null && page.Items.Count > 0;
            page.Total = 0;
            return page;
        }

        // ------------------------------------------------------------------
        // Detalhe + capítulos (uma requisição só)
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
                if (_cache.TryGetValue(slug, out var hit) && DateTime.UtcNow - hit.At < TimeSpan.FromMinutes(10))
                    return hit;
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
            var html = await GetTextAsync(Base + "/manga/" + Uri.EscapeDataString(slug) + "/").ConfigureAwait(false);
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var cover = doc.DocumentNode.SelectSingleNode("//img[contains(@class,'manga-cover-image')]")
                        ?? doc.DocumentNode.SelectSingleNode("//*[contains(@class,'manga-cover')]//img");
            var coverUrl = cover == null ? string.Empty : Abs(Attr(cover, "src") is var s && s.Length > 0
                ? s : Attr(cover, "data-src"));

            var title = Txt(doc.DocumentNode.SelectSingleNode("//*[contains(@class,'manga-title')]"));
            if (string.IsNullOrWhiteSpace(title)) title = Txt(doc.DocumentNode.SelectSingleNode("//h1"));
            if (string.IsNullOrWhiteSpace(title) && cover != null)
                title = Regex.Replace(cover.GetAttributeValue("alt", string.Empty), "\\s+", " ").Trim();
            if (string.IsNullOrWhiteSpace(title)) title = slug.Replace('-', ' ');

            var desc = Txt(doc.DocumentNode.SelectSingleNode("//*[contains(@class,'synopsis-content')]"));
            if (string.IsNullOrWhiteSpace(desc))
                desc = Txt(doc.DocumentNode.SelectSingleNode("//*[contains(@class,'manga-synopsis')]"));

            var status = string.Empty;
            foreach (var item in doc.DocumentNode.SelectNodes("//*[contains(@class,'manga-meta-item')]")
                     ?? Enumerable.Empty<HtmlNode>())
            {
                var label = Txt(item.SelectSingleNode(".//*[contains(@class,'meta-label')]")).TrimEnd(':', ' ');
                if (!label.Equals("Status", StringComparison.OrdinalIgnoreCase)) continue;
                status = Txt(item.SelectSingleNode(".//*[contains(@class,'meta-value')]"));
                break;
            }

            var genres = new List<string>();
            foreach (var a in doc.DocumentNode.SelectNodes("//*[contains(@class,'manga-tags')]//a | " +
                                                           "//a[contains(@href,'/genero/')] | " +
                                                           "//a[contains(@href,'/genre/')]")
                     ?? Enumerable.Empty<HtmlNode>())
            {
                var g = Txt(a);
                if (string.IsNullOrWhiteSpace(g) || g.Length > 40) continue;
                if (!genres.Contains(g)) genres.Add(g);
            }

            var chapters = ParseChapterList(html);
            if (chapters.Any(c => c.Number > 0))
                chapters = chapters.OrderBy(c => c.Number).ThenBy(c => c.Title, StringComparer.OrdinalIgnoreCase).ToList();
            else
                chapters.Reverse();

            var manga = new Manga
            {
                Source = "mangalivre",
                Id = slug,
                Title = title,
                Cover = coverUrl,
                Description = desc,
                Status = status,
                Adult = LooksAdult(title, desc, genres),
                Genres = genres,
                Chapters = chapters.Count,
                Latest = chapters.LastOrDefault()?.Title ?? string.Empty,
                Url = Base + "/manga/" + slug + "/",
            };

            var detail = new Detail { At = DateTime.UtcNow, Manga = manga, Chapters = chapters };
            lock (_cacheGate) _cache[slug] = detail;
            return detail;
        }

        public async Task<Manga> MangaAsync(string id, bool adult)
        {
            var d = await DetailAsync(id).ConfigureAwait(false);
            var m = d.Manga;
            return new Manga
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
        }

        // ------------------------------------------------------------------
        // Capítulos
        // ------------------------------------------------------------------
        private static List<Chapter> ParseChapterList(string html)
        {
            var list = new List<Chapter>();
            if (string.IsNullOrWhiteSpace(html)) return list;
            var doc = new HtmlDocument();
            doc.LoadHtml(html);
            foreach (var li in doc.DocumentNode.SelectNodes("//*[contains(@class,'chapter-item')]") ?? Enumerable.Empty<HtmlNode>())
            {
                var a = li.SelectSingleNode(".//a[contains(@class,'chapter-link') and contains(@href,'/capitulo/')]")
                        ?? li.SelectSingleNode(".//a[contains(@href,'/capitulo/')]");
                if (a == null) continue;
                var href = Abs(a.GetAttributeValue("href", string.Empty));
                var path = href.StartsWith(Base, StringComparison.OrdinalIgnoreCase) ? href.Substring(Base.Length) : href;
                var label = Txt(a.SelectSingleNode(".//*[contains(@class,'chapter-number')]"));
                if (string.IsNullOrWhiteSpace(label)) label = Txt(a);
                if (string.IsNullOrWhiteSpace(path)) continue;
                list.Add(new Chapter
                {
                    Id = path,
                    Title = string.IsNullOrWhiteSpace(label) ? "Capitulo" : label,
                    Number = ManhastroSource.NumberFrom(label),
                    Date = Txt(li.SelectSingleNode(".//*[contains(@class,'chapter-date')]")),
                });
            }
            return list;
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
            {
                path = Uri.TryCreate(path, UriKind.Absolute, out var uri) ? uri.PathAndQuery : path;
            }
            if (!path.StartsWith("/", StringComparison.Ordinal)) path = "/" + path;

            var html = await GetTextAsync(Base + path).ConfigureAwait(false);
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var nodes = doc.DocumentNode.SelectNodes(
                "//*[contains(@class,'chapter-images')]//img[contains(@class,'chapter-image')] | " +
                "//*[contains(@class,'chapter-image-container')]//img") ?? Enumerable.Empty<HtmlNode>();

            var ordered = new List<(int Page, string Url)>();
            foreach (var img in nodes)
            {
                var box = img.Ancestors().FirstOrDefault(x => x.Attributes["data-page"] != null);
                var page = 0;
                if (box != null) int.TryParse(box.GetAttributeValue("data-page", "0"), out page);
                var u = Attr(img, "src");
                if (string.IsNullOrWhiteSpace(u)) u = Attr(img, "data-src");
                if (string.IsNullOrWhiteSpace(u)) u = Attr(img, "data-original");
                if (string.IsNullOrWhiteSpace(u)) continue;
                ordered.Add((page, Abs(u)));
            }

            var set = new PageSet();
            foreach (var it in ordered.OrderBy(x => x.Page))
                if (!set.Images.Contains(it.Url)) set.Images.Add(it.Url);
            if (set.Images.Count == 0) throw new Exception("Capítulo sem páginas disponíveis.");
            return set;
        }

        public Task<List<string>> GenresAsync() => Task.FromResult(GenreSlugs.Select(g => g.Name).ToList());
    }
}
