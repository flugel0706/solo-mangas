using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HtmlAgilityPack;

namespace SoloMangasNative
{
    /// <summary>
    /// Mangapill (EN): HTML estatico sem JavaScript. A busca aceita termo e genero,
    /// a pagina da obra lista todos os capitulos e a pagina do capitulo entrega as
    /// imagens direto no HTML (CDN readdetectiveconan).
    /// </summary>
    public sealed class MangaPillSource : ISource
    {
        private const string Base = "https://mangapill.com";
        private const string Cdn = "readdetectiveconan.com";

        // Valor enviado em ?genre= (o site usa o nome em ingles).
        private static readonly (string Name, string Value)[] Genres =
        {
            ("Ação", "Action"), ("Aventura", "Adventure"), ("Carros", "Cars"),
            ("Comédia", "Comedy"), ("Demência", "Dementia"), ("Demônios", "Demons"),
            ("Doujinshi", "Doujinshi"), ("Drama", "Drama"), ("Ecchi", "Ecchi"),
            ("Fantasia", "Fantasy"), ("Jogos", "Game"), ("Gender Bender", "Gender Bender"),
            ("Harém", "Harem"), ("Histórico", "Historical"), ("Terror", "Horror"),
            ("Isekai", "Isekai"), ("Josei", "Josei"), ("Infantil", "Kids"),
            ("Magia", "Magic"), ("Artes Marciais", "Martial Arts"), ("Mecha", "Mecha"),
            ("Militar", "Military"), ("Música", "Music"), ("Mistério", "Mystery"),
            ("Paródia", "Parody"), ("Policial", "Police"), ("Psicológico", "Psychological"),
            ("Romance", "Romance"), ("Samurai", "Samurai"), ("Escolar", "School"),
            ("Ficção Científica", "Sci-Fi"), ("Seinen", "Seinen"), ("Shoujo", "Shoujo"),
            ("Shoujo Ai", "Shoujo Ai"), ("Shounen", "Shounen"), ("Shounen Ai", "Shounen Ai"),
            ("Slice of Life", "Slice of Life"), ("Espaço", "Space"), ("Esportes", "Sports"),
            ("Super Poder", "Super Power"), ("Sobrenatural", "Supernatural"),
            ("Suspense", "Thriller"), ("Tragédia", "Tragedy"), ("Vampiro", "Vampire"),
            ("Yaoi", "Yaoi"), ("Yuri", "Yuri"),
        };

        public string Key => "mangapill";
        public string Name => "Mangapill";
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
                throw new Exception(msg.StartsWith("Mangapill", StringComparison.Ordinal)
                    ? msg
                    : "Mangapill: " + msg);
            }
        }

        private static string Txt(HtmlNode n) =>
            n == null ? string.Empty : Regex.Replace(HtmlEntity.DeEntitize(n.InnerText ?? string.Empty), "\\s+", " ").Trim();

        private static string Attr(HtmlNode n, string name)
        {
            if (n == null) return string.Empty;
            var v = n.GetAttributeValue(name, string.Empty);
            return string.IsNullOrWhiteSpace(v) ? string.Empty : v.Trim();
        }

        private static string Abs(string u)
        {
            if (string.IsNullOrWhiteSpace(u)) return string.Empty;
            u = u.Trim();
            if (u.StartsWith("//")) return "https:" + u;
            if (u.StartsWith("/")) return Base + u;
            return u;
        }

        private static string Img(HtmlNode img)
        {
            if (img == null) return string.Empty;
            foreach (var key in new[] { "data-src", "src", "data-lazy-src" })
            {
                var v = Attr(img, key);
                if (v.Length > 0 && !v.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return Abs(v);
            }
            return string.Empty;
        }

        private static string GenreValue(string label)
        {
            var n = Norm(label);
            foreach (var (name, value) in Genres)
                if (Norm(name) == n || Norm(value) == n) return value;
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

        // ------------------------------------------------------------------
        // Cards (home = ultimos lancamentos, busca = resultados)
        // ------------------------------------------------------------------
        private static List<Manga> ParseCards(HtmlDocument doc)
        {
            var outp = new List<Manga>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var anchors = (doc.DocumentNode.SelectNodes("//a[starts-with(@href,'/manga/')]")
                           ?? Enumerable.Empty<HtmlNode>()).ToList();

            // Duas passadas: primeiro os links com titulo visivel (cards normais),
            // depois os links so com capa, para nao herdar o alt duplicado do site.
            for (var pass = 0; pass < 2; pass++)
            {
                foreach (var a in anchors)
                {
                    var href = Attr(a, "href");
                    var m = Regex.Match(href, "^/manga/([0-9]+)/([^/?#]+)");
                    if (!m.Success) continue;

                    var id = m.Groups[1].Value + "/" + m.Groups[2].Value;
                    if (seen.Contains(id)) continue;

                    var box = a.Ancestors("div").FirstOrDefault(x => x.SelectSingleNode(".//img") != null);
                    var img = box?.SelectSingleNode(".//img") ?? a.SelectSingleNode(".//img");

                    var title = Txt(a);
                    if (title.Length == 0)
                    {
                        if (pass == 0) continue;
                        if (img != null)
                            title = Regex.Replace(Attr(img, "alt"), "\\s+Chapter\\s+[\\d.]+\\s*$", "", RegexOptions.IgnoreCase).Trim();
                    }
                    if (title.Length == 0)
                        title = Regex.Replace(m.Groups[2].Value.Replace('-', ' '), "\\s+", " ").Trim();
                    if (title.Length == 0) continue;

                    var latest = string.Empty;
                    if (box != null)
                    {
                        var chap = box.SelectSingleNode(".//a[starts-with(@href,'/chapters/')]");
                        latest = Txt(chap) ?? Txt(chap?.SelectSingleNode(".//div"));
                        if (latest.StartsWith("#", StringComparison.Ordinal)) latest = "Chapter " + latest.Substring(1);
                    }

                    seen.Add(id);
                    outp.Add(new Manga
                    {
                        Source = "mangapill",
                        Id = id,
                        Title = title,
                        Cover = Img(img),
                        Url = Base + "/manga/" + id,
                        Latest = latest,
                    });
                }
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
                url = Base + "/search?q=" + Uri.EscapeDataString(q) + "&page=" + page.Page;
            }
            else if (genre.Length > 0 && Norm(genre) != "tudo" && Norm(genre) != "todos")
            {
                var value = GenreValue(genre);
                if (value.Length == 0) return page;
                url = Base + "/search?genre=" + Uri.EscapeDataString(value) + "&page=" + page.Page;
            }
            else
            {
                // A home do Mangapill so lista os lancamentos recentes (sem paginacao).
                if (page.Page > 1) return page;
                url = Base + "/";
            }

            var html = await GetTextAsync(url).ConfigureAwait(false);
            var doc = new HtmlDocument();
            doc.LoadHtml(html);
            page.Items = ParseCards(doc);

            if (q.Length > 0 || genre.Length > 0)
            {
                var next = doc.DocumentNode.SelectSingleNode("//a[contains(@class,'next') and contains(@href,'page=')]")
                           ?? doc.DocumentNode.SelectSingleNode("//link[@rel='next']");
                page.HasMore = next != null && page.Items.Count > 0;
            }
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

        private async Task<Detail> DetailAsync(string id)
        {
            Task<Detail> task;
            lock (_cacheGate)
            {
                if (_cache.TryGetValue(id, out var hit) && DateTime.UtcNow - hit.At < TimeSpan.FromMinutes(10))
                    return hit;
                if (!_inflight.TryGetValue(id, out task))
                {
                    task = LoadDetailAsync(id);
                    _inflight[id] = task;
                }
            }

            try
            {
                return await task.ConfigureAwait(false);
            }
            finally
            {
                lock (_cacheGate) _inflight.Remove(id);
            }
        }

        private async Task<Detail> LoadDetailAsync(string id)
        {
            var clean = (id ?? string.Empty).Trim().Trim('/');
            if (clean.StartsWith("manga/", StringComparison.OrdinalIgnoreCase))
                clean = clean.Substring(6);

            var html = await GetTextAsync(Base + "/manga/" + clean).ConfigureAwait(false);
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var title = Txt(doc.DocumentNode.SelectSingleNode("//h1"));
            if (title.Length == 0) title = clean.Replace('-', ' ');

            var cover = string.Empty;
            foreach (var img in doc.DocumentNode.SelectNodes("//img[@data-src]") ?? Enumerable.Empty<HtmlNode>())
            {
                var src = Img(img);
                if (src.Length > 0 && src.Contains("/mangapill/i/", StringComparison.OrdinalIgnoreCase))
                {
                    cover = src;
                    break;
                }
            }

            var desc = Attr(doc.DocumentNode.SelectSingleNode("//meta[@property='og:description']"), "content");
            if (desc.Length > 0) desc = Regex.Replace(desc, "\\s+", " ").Trim();

            var chapters = ParseChapters(doc);

            var manga = new Manga
            {
                Source = "mangapill",
                Id = clean,
                Title = title,
                Cover = cover,
                Description = desc,
                Chapters = chapters.Count,
                Latest = chapters.LastOrDefault()?.Title ?? string.Empty,
                Url = Base + "/manga/" + clean,
            };

            var detail = new Detail { At = DateTime.UtcNow, Manga = manga, Chapters = chapters };
            lock (_cacheGate) _cache[clean] = detail;
            return detail;
        }

        private static List<Chapter> ParseChapters(HtmlDocument doc)
        {
            var list = new List<Chapter>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var a in doc.DocumentNode.SelectNodes("//a[starts-with(@href,'/chapters/')]")
                     ?? Enumerable.Empty<HtmlNode>())
            {
                var href = Attr(a, "href");
                if (href.Length == 0 || !seen.Add(href)) continue;

                var label = Txt(a.SelectSingleNode(".//div")) ?? Txt(a);
                if (string.IsNullOrWhiteSpace(label)) label = Txt(a);
                if (string.IsNullOrWhiteSpace(label)) label = "Capitulo";

                list.Add(new Chapter
                {
                    Id = href,
                    Title = label,
                    Number = ManhastroSource.NumberFrom(label),
                });
            }

            if (list.Count > 1)
            {
                var numbered = list.Count(c => c.Number > 0);
                if (numbered >= Math.Max(2, (int)Math.Ceiling(list.Count * 0.6)))
                    list = list.OrderBy(c => c.Number).ThenBy(c => c.Title, StringComparer.OrdinalIgnoreCase).ToList();
                else
                    list.Reverse();
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
            foreach (var img in doc.DocumentNode.SelectNodes("//img") ?? Enumerable.Empty<HtmlNode>())
            {
                var src = Img(img);
                if (src.Length == 0) continue;
                if (!src.Contains(Cdn, StringComparison.OrdinalIgnoreCase)) continue;
                if (src.Contains("/mangapill/i/", StringComparison.OrdinalIgnoreCase)) continue; // capas
                if (!set.Images.Contains(src)) set.Images.Add(src);
            }

            if (set.Images.Count == 0) throw new Exception("Capitulo sem paginas disponiveis.");
            return set;
        }

        public Task<List<string>> GenresAsync() => Task.FromResult(Genres.Select(g => g.Name).ToList());
    }
}
