using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HtmlAgilityPack;

namespace SoloMangasNative
{
    /// <summary>
    /// MangaRead (EN): tema Madara com leitor proprio. O HTML da obra ja traz a
    /// lista completa de capitulos, entao detalhe e capitulos custam uma unica
    /// requisicao. Sem termo de busca o catalogo lista as obras mais vistas.
    /// </summary>
    public sealed class MangaReadSource : ISource
    {
        private const string Base = "https://www.mangaread.org";

        private static readonly (string Name, string Slug)[] GenreSlugs =
        {
            ("Ação", "action"), ("Aventura", "adventure"), ("Comédia", "comedy"),
            ("Drama", "drama"), ("Fantasia", "fantasy"), ("Romance", "romance"),
            ("Shounen", "shounen"), ("Shoujo", "shoujo"), ("Seinen", "seinen"), ("Josei", "josei"),
            ("Sobrenatural", "supernatural"), ("Mistério", "mystery"), ("Terror", "horror"),
            ("Ficção Científica", "sci-fi"), ("Slice of Life", "slice-of-life"), ("Esportes", "sports"),
            ("Artes Marciais", "martial-arts"), ("Escolar", "school-life"), ("Histórico", "historical"),
            ("Psicológico", "psychological"), ("Mecha", "mecha"), ("Tragédia", "tragedy"),
            ("Isekai", "isekai"), ("Harém", "harem"), ("Culinária", "cooking"),
            ("Manhwa", "manhwa"), ("Manhua", "manhua"), ("Webtoon", "webtoon"),
            ("One Shot", "one-shot"),
        };

        private static readonly Dictionary<string, string> PtGenres =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Action"] = "Ação", ["Adventure"] = "Aventura", ["Comedy"] = "Comédia",
                ["Cooking"] = "Culinária", ["Drama"] = "Drama", ["Fantasy"] = "Fantasia",
                ["Historical"] = "Histórico", ["Horror"] = "Terror", ["Isekai"] = "Isekai",
                ["Josei"] = "Josei", ["Manga"] = "Mangá", ["Manhua"] = "Manhua", ["Manhwa"] = "Manhwa",
                ["Martial Arts"] = "Artes Marciais", ["Mecha"] = "Mecha", ["Mystery"] = "Mistério",
                ["Psychological"] = "Psicológico", ["Romance"] = "Romance", ["School Life"] = "Escolar",
                ["Sci-fi"] = "Ficção Científica", ["Sci-Fi"] = "Ficção Científica", ["Seinen"] = "Seinen",
                ["Shoujo"] = "Shoujo", ["Shounen"] = "Shounen", ["Slice of Life"] = "Slice of Life",
                ["Sports"] = "Esportes", ["Smut"] = "Smut", ["Supernatural"] = "Sobrenatural",
                ["Tragedy"] = "Tragédia", ["Webtoon"] = "Webtoon", ["Ecchi"] = "Ecchi",
                ["Harem"] = "Harém", ["Gender Bender"] = "Gender Bender", ["Doujinshi"] = "Doujinshi",
                ["One Shot"] = "One Shot", ["Shounen Ai"] = "Shounen Ai", ["Shoujo Ai"] = "Shoujo Ai",
                ["Comic"] = "Comic",
            };

        public string Key => "mangaread";
        public string Name => "MangaRead";
        public bool SupportsLogin => false;
        public bool AdultOnly => false;

        // ------------------------------------------------------------------
        // HTTP / util
        // ------------------------------------------------------------------
        private static async Task<string> GetTextAsync(string url)
        {
            try
            {
                return await HtmlHttp.GetAsync(url, Base + "/", 25000).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                var msg = ex.Message ?? "falha de rede";
                throw new Exception(msg.StartsWith("MangaRead", StringComparison.Ordinal)
                    ? msg
                    : "MangaRead: " + msg);
            }
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

        private static string Src(HtmlNode img)
        {
            if (img == null) return string.Empty;
            foreach (var key in new[] { "data-src", "src", "data-lazy-src" })
            {
                var v = Attr(img, key);
                if (v.Length > 0) return Abs(v);
            }
            return string.Empty;
        }

        private static string Norm(string v)
        {
            if (string.IsNullOrEmpty(v)) return string.Empty;
            var s = v.Normalize(System.Text.NormalizationForm.FormD);
            var sb = new System.Text.StringBuilder();
            foreach (var c in s)
                if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) !=
                    System.Globalization.UnicodeCategory.NonSpacingMark)
                    sb.Append(c);
            return Regex.Replace(sb.ToString(), "\\s+", " ").Trim().ToLowerInvariant();
        }

        private static bool LooksAdult(string title, string description, IEnumerable<string> genres)
        {
            foreach (var g in genres ?? Enumerable.Empty<string>())
            {
                var n = Norm(g);
                if (n == "smut" || n == "adult" || n == "hentai" || n == "+18") return true;
            }
            var text = Norm((title ?? string.Empty) + " " + (description ?? string.Empty));
            return Regex.IsMatch(text, "\\bhentai\\b|\\bsmut\\b|\\bnsfw\\b|\\+18\\b");
        }

        /// <summary>Status do site em ingles para o padrao usado no app.</summary>
        private static string StatusPt(string status)
        {
            switch (Norm(status))
            {
                case "ongoing": return "Em andamento";
                case "completed": return "Completo";
                case "hiatus": return "Em hiato";
                case "cancelled":
                case "canceled":
                case "dropped": return "Cancelado";
                default: return status ?? string.Empty;
            }
        }

        // ------------------------------------------------------------------
        // Cards
        // ------------------------------------------------------------------
        private static List<Manga> ParseCards(HtmlDocument doc)
        {
            var outp = new List<Manga>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var cards = doc.DocumentNode.SelectNodes(
                "//div[contains(@class,'page-item-detail')] | //div[contains(@class,'c-tabs-item__content')]");
            foreach (var card in cards ?? Enumerable.Empty<HtmlNode>())
            {
                var a = card.SelectSingleNode(".//div[contains(@class,'post-title')]//a[contains(@href,'/manga/')]")
                        ?? card.SelectSingleNode(".//h3//a[contains(@href,'/manga/')]")
                        ?? card.SelectSingleNode(".//div[contains(@class,'item-thumb')]//a[contains(@href,'/manga/')]")
                        ?? card.SelectSingleNode(".//a[contains(@href,'/manga/')]");
                if (a == null) continue;

                var href = Abs(Attr(a, "href"));
                var slug = SlugOf(href);
                if (slug.Length == 0 || !seen.Add(slug)) continue;

                var img = card.SelectSingleNode(".//img");
                var title = Txt(a);
                if (title.Length == 0 && img != null)
                    title = Regex.Replace(Attr(img, "alt"), "^Manhwa Read\\s*", "", RegexOptions.IgnoreCase).Trim();
                if (title.Length == 0 && img != null)
                    title = Regex.Replace(Attr(img, "alt"), "\\s+", " ").Trim();

                var latest = Txt(card.SelectSingleNode(".//*[contains(@class,'latest-chap')]"));
                if (latest.Length == 0)
                {
                    var chap = card.SelectSingleNode(".//*[contains(@class,'chapter-item')]//a")
                               ?? card.SelectSingleNode(".//*[contains(@class,'list-chapter')]//a");
                    latest = Txt(chap);
                }

                outp.Add(new Manga
                {
                    Source = "mangaread",
                    Id = slug,
                    Title = title.Length == 0 ? "Sem titulo" : title,
                    Cover = Src(img),
                    Url = Base + "/manga/" + slug + "/",
                    Latest = latest,
                    Adult = LooksAdult(title, null, null),
                });
            }
            return outp;
        }

        private static string GenreSlug(string label)
        {
            var n = Norm(label);
            foreach (var (name, slug) in GenreSlugs)
                if (Norm(name) == n) return slug;
            return string.Empty;
        }

        public async Task<CatalogResult> CatalogAsync(SourceQuery query)
        {
            var page = new CatalogResult { Page = Math.Max(1, query.Page) };
            var q = (query.Q ?? string.Empty).Trim();
            var genre = (query.Genre ?? string.Empty).Trim();
            string url;

            if (q.Length > 0)
            {
                var search = "?s=" + Uri.EscapeDataString(q) + "&post_type=wp-manga";
                url = page.Page <= 1
                    ? Base + "/" + search
                    : Base + "/page/" + page.Page + "/" + search;
            }
            else if (genre.Length > 0 && Norm(genre) != "tudo" && Norm(genre) != "todos")
            {
                var slug = GenreSlug(genre);
                if (slug.Length == 0) return page;
                url = Base + "/genres/" + slug + "/" + (page.Page > 1 ? "page/" + page.Page + "/" : string.Empty);
            }
            else
            {
                url = Base + "/manga/" + (page.Page > 1 ? "page/" + page.Page + "/" : string.Empty) + "?m_orderby=views";
            }

            var html = await GetTextAsync(url).ConfigureAwait(false);
            var doc = new HtmlDocument();
            doc.LoadHtml(html);
            page.Items = ParseCards(doc);

            var next = doc.DocumentNode.SelectSingleNode("//a[contains(@class,'next') and contains(@class,'page-numbers')]")
                       ?? doc.DocumentNode.SelectSingleNode("//link[@rel='next']");
            page.HasMore = next != null && page.Items.Count > 0;
            page.Total = 0;
            return page;
        }

        // ------------------------------------------------------------------
        // Detalhe + capitulos (uma requisicao so)
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

            var title = Txt(doc.DocumentNode.SelectSingleNode("//div[contains(@class,'post-title')]//h1"))
                        ?? string.Empty;
            if (title.Length == 0) title = Txt(doc.DocumentNode.SelectSingleNode("//h1"));
            if (title.Length == 0) title = slug.Replace('-', ' ');

            var coverNode = doc.DocumentNode.SelectSingleNode("//div[contains(@class,'summary_image')]//img")
                            ?? doc.DocumentNode.SelectSingleNode("//div[contains(@class,'tab-thumb')]//img");

            var desc = Txt(doc.DocumentNode.SelectSingleNode("//div[contains(@class,'summary__content')]"))
                       ?? string.Empty;
            if (desc.Length == 0)
                desc = Txt(doc.DocumentNode.SelectSingleNode("//div[contains(@class,'description-summary')]"));
            desc = Regex.Replace(desc, "\\s*show more\\s*$", string.Empty, RegexOptions.IgnoreCase).Trim();

            var genres = new List<string>();
            foreach (var a in doc.DocumentNode.SelectNodes("//div[contains(@class,'genres-content')]//a")
                     ?? Enumerable.Empty<HtmlNode>())
            {
                var raw = Txt(a);
                if (raw.Length == 0 || raw.Length > 40) continue;
                var name = PtGenres.TryGetValue(raw, out var pt) ? pt : raw;
                if (!genres.Contains(name)) genres.Add(name);
            }

            var status = string.Empty;
            var altTitle = string.Empty;
            foreach (var item in doc.DocumentNode.SelectNodes("//div[contains(@class,'post-content_item')]")
                     ?? Enumerable.Empty<HtmlNode>())
            {
                var label = Norm(Txt(item.SelectSingleNode(".//div[contains(@class,'summary-heading')]")));
                if (label.Length == 0) continue;
                var value = Txt(item.SelectSingleNode(".//div[contains(@class,'summary-content')]"));
                if (label == "status" && status.Length == 0) status = value;
                else if ((label == "alternative" || label == "alternate name") && altTitle.Length == 0) altTitle = value;
            }

            var chapters = ParseChapterList(doc);
            if (chapters.Count > 0)
            {
                var numbered = chapters.Count(c => c.Number > 0);
                if (numbered >= Math.Max(2, (int)Math.Ceiling(chapters.Count * 0.6)))
                    chapters = chapters.OrderBy(c => c.Number).ThenBy(c => c.Title, StringComparer.OrdinalIgnoreCase).ToList();
                else
                    chapters.Reverse();
            }

            var manga = new Manga
            {
                Source = "mangaread",
                Id = slug,
                Title = title,
                TitleAlt = altTitle,
                Cover = Src(coverNode),
                Description = desc,
                Status = StatusPt(status),
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

        private static List<Chapter> ParseChapterList(HtmlDocument doc)
        {
            var list = new List<Chapter>();
            foreach (var li in doc.DocumentNode.SelectNodes("//li[contains(@class,'wp-manga-chapter')]")
                     ?? Enumerable.Empty<HtmlNode>())
            {
                var a = li.SelectSingleNode(".//a[contains(@href,'/manga/')]");
                if (a == null) continue;
                var href = Attr(a, "href");
                if (href.Length == 0) continue;
                var path = Uri.TryCreate(href, UriKind.Absolute, out var uri) ? uri.PathAndQuery : href;
                if (!path.StartsWith("/", StringComparison.Ordinal)) continue;

                var label = Txt(a);
                if (label.Length == 0) label = "Capitulo";
                list.Add(new Chapter
                {
                    Id = path,
                    Title = label,
                    Number = ManhastroSource.NumberFrom(label),
                    Date = Txt(li.SelectSingleNode(".//*[contains(@class,'chapter-release-date')]"))
                           ?? Txt(li.SelectSingleNode(".//*[contains(@class,'chapter-date')]")),
                });
            }
            return list;
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

            var html = await GetTextAsync(Base + path).ConfigureAwait(false);
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var set = new PageSet();
            var nodes = doc.DocumentNode.SelectNodes(
                "//div[contains(@class,'reading-content')]//img[contains(@class,'wp-manga-chapter-img')]")
                ?? doc.DocumentNode.SelectNodes("//img[contains(@class,'wp-manga-chapter-img')]");
            foreach (var img in nodes ?? Enumerable.Empty<HtmlNode>())
            {
                var src = Attr(img, "data-src");
                if (src.Length == 0) src = Attr(img, "src");
                if (src.Length == 0) src = Attr(img, "data-lazy-src");
                src = Regex.Replace(src ?? string.Empty, "\\s+", string.Empty);
                if (src.Length == 0) continue;
                var abs = Abs(src);
                if (abs.Length > 0 && !set.Images.Contains(abs)) set.Images.Add(abs);
            }

            if (set.Images.Count == 0) throw new Exception("Capitulo sem paginas disponiveis.");
            return set;
        }

        public Task<List<string>> GenresAsync() => Task.FromResult(GenreSlugs.Select(g => g.Name).ToList());
    }
}
