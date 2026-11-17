using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HtmlAgilityPack;

namespace SoloMangasNative
{
    /// <summary>
    /// Hanami Heaven (WordPress/Madara em portugues, exclusivamente +18).
    /// O catalogo usa a pagina propria "todas-as-obras" com busca e genero por
    /// querystring; a obra e o leitor seguem o padrao Madara.
    /// </summary>
    public sealed class HanamiSource : ISource
    {
        private const string Site = "https://hanamiheaven.org";

        private static readonly string[] UiGenres =
        {
            "Ação", "Aventura", "Comédia", "Drama", "Fantasia", "Romance", "Harem", "Ecchi",
            "Pornhwa", "Campus", "Slice of Life", "Sobrenatural",
        };

        public string Key => "hanami";
        public string Name => "Hanami Heaven";
        public bool SupportsLogin => false;
        public bool AdultOnly => true;

        // ------------------------------------------------------------------
        // HTTP / util
        // ------------------------------------------------------------------
        private static string Text(string html)
        {
            if (string.IsNullOrEmpty(html)) return string.Empty;
            var s = Regex.Replace(html, "<br\\s*/?>", " ", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, "<[^>]+>", " ");
            s = WebUtility.HtmlDecode(s);
            return Regex.Replace(s, "\\s+", " ").Trim();
        }

        private static string Attr(HtmlNode node, string name)
        {
            if (node == null) return string.Empty;
            return WebUtility.HtmlDecode(node.GetAttributeValue(name, string.Empty) ?? string.Empty).Trim();
        }

        private static string Abs(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return string.Empty;
            if (url.StartsWith("//", StringComparison.Ordinal)) return "https:" + url;
            if (url.StartsWith("/", StringComparison.Ordinal)) return Site + url;
            if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return url;
            return Site + "/" + url.TrimStart('/');
        }

        private static string SlugFromUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return string.Empty;
            var path = url;
            var q = path.IndexOfAny(new[] { '?', '#' });
            if (q >= 0) path = path.Substring(0, q);
            var parts = path.Trim('/').Split('/');
            return parts.Length == 0 ? string.Empty : parts[parts.Length - 1];
        }

        private static string GenreSlug(string genre)
        {
            if (string.IsNullOrWhiteSpace(genre)) return string.Empty;
            var s = genre.Trim().ToLowerInvariant()
                .Replace('á', 'a').Replace('à', 'a').Replace('â', 'a').Replace('ã', 'a')
                .Replace('é', 'e').Replace('ê', 'e').Replace('í', 'i')
                .Replace('ó', 'o').Replace('ô', 'o').Replace('õ', 'o').Replace('ú', 'u')
                .Replace('ç', 'c');
            return Regex.Replace(s, "[^a-z0-9]+", "-").Trim('-');
        }

        private static double NumberOf(string title)
        {
            var m = Regex.Match(title ?? string.Empty, "(\\d+(?:[.,]\\d+)?)");
            if (!m.Success) return 0;
            return double.TryParse(m.Groups[1].Value.Replace(',', '.'), NumberStyles.Float,
                CultureInfo.InvariantCulture, out var n) ? n : 0;
        }

        private static HtmlDocument Load(string html)
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(html ?? string.Empty);
            return doc;
        }

        // ------------------------------------------------------------------
        // Catalogo
        // ------------------------------------------------------------------
        public async Task<CatalogResult> CatalogAsync(SourceQuery query)
        {
            var page = Math.Max(1, query.Page);
            var url = Site + "/todas-as-obras/?pagina=" + page.ToString(CultureInfo.InvariantCulture);
            if (!string.IsNullOrWhiteSpace(query.Q))
                url += "&busca=" + Uri.EscapeDataString(query.Q.Trim());
            if (!string.IsNullOrWhiteSpace(query.Genre))
                url += "&genero=" + Uri.EscapeDataString(GenreSlug(query.Genre));

            var html = await HtmlHttp.GetAsync(url, Site + "/").ConfigureAwait(false);
            var doc = Load(html);
            var result = new CatalogResult { Page = page };

            // layout novo da pagina "todas-as-obras"
            var cards = doc.DocumentNode.SelectNodes("//li[contains(@class,'mch-catalogue-card')]");
            if (cards != null)
            {
                foreach (var card in cards)
                {
                    var link = card.SelectSingleNode(".//h2[contains(@class,'mch-catalogue-card__title')]/a")
                               ?? card.SelectSingleNode(".//a[contains(@class,'mch-catalogue-card__cover')]");
                    var id = SlugFromUrl(Attr(link, "href"));
                    if (string.IsNullOrWhiteSpace(id)) continue;

                    var img = card.SelectSingleNode(".//img[contains(@class,'mch-catalogue-card__image')]")
                              ?? card.SelectSingleNode(".//img");
                    var meta = card.SelectSingleNode(".//span[contains(@class,'mch-catalogue-card__meta')]");
                    var status = string.Empty;
                    var metaSpans = meta?.SelectNodes(".//span");
                    if (metaSpans != null && metaSpans.Count > 1) status = Text(metaSpans[1].InnerText);

                    result.Items.Add(new Manga
                    {
                        Source = Key,
                        Id = id,
                        Title = Text(link?.InnerText) is var t && t.Length > 0 ? t : id,
                        Cover = Abs(Attr(img, "src") is var c && c.Length > 0 ? c : Attr(img, "data-src")),
                        Adult = true,
                        Status = status,
                        Url = Site + "/manga/" + id + "/",
                    });
                }
            }

            // fallback: arquivo Madara classico
            if (result.Items.Count == 0)
            {
                var old = doc.DocumentNode.SelectNodes("//div[contains(@class,'page-item-detail')]");
                if (old != null)
                {
                    foreach (var card in old)
                    {
                        var link = card.SelectSingleNode(".//div[contains(@class,'post-title')]//a")
                                   ?? card.SelectSingleNode(".//a");
                        var id = SlugFromUrl(Attr(link, "href"));
                        if (string.IsNullOrWhiteSpace(id)) continue;
                        var img = card.SelectSingleNode(".//div[contains(@class,'item-thumb')]//img")
                                  ?? card.SelectSingleNode(".//img");
                        var latest = card.SelectSingleNode(".//div[contains(@class,'list-chapter')]//a");
                        result.Items.Add(new Manga
                        {
                            Source = Key,
                            Id = id,
                            Title = Text(link?.InnerText) is var t && t.Length > 0 ? t : id,
                            Cover = Abs(Attr(img, "src") is var c && c.Length > 0 ? c : Attr(img, "data-src")),
                            Latest = Text(latest?.InnerText),
                            Adult = true,
                            Url = Site + "/manga/" + id + "/",
                        });
                    }
                }
            }

            var total = 0;
            long totalLong = 0;
            var tm = Regex.Match(html, "(\\d[\\d\\.]*)\\s+obras", RegexOptions.IgnoreCase);
            if (tm.Success)
            {
                long.TryParse(tm.Groups[1].Value.Replace(".", string.Empty), out totalLong);
                total = (int)Math.Min(int.MaxValue, totalLong);
            }
            result.Total = totalLong;

            var maxPage = page;
            foreach (Match pm in Regex.Matches(html, "[?&](?:pagina|page)=(\\d+)", RegexOptions.IgnoreCase))
                if (int.TryParse(pm.Groups[1].Value, out var pn) && pn > maxPage) maxPage = pn;
            result.HasMore = maxPage > page || (result.Items.Count >= 25 && page < 20);
            return result;
        }

        // ------------------------------------------------------------------
        // Obra
        // ------------------------------------------------------------------
        public async Task<Manga> MangaAsync(string id, bool adult)
        {
            var slug = (id ?? string.Empty).Trim().Trim('/');
            if (string.IsNullOrWhiteSpace(slug)) throw new Exception("Obra invalida.");
            var url = Site + "/manga/" + Uri.EscapeDataString(slug) + "/";

            var html = await HtmlHttp.GetAsync(url, Site + "/").ConfigureAwait(false);
            var doc = Load(html);

            var title = Text(doc.DocumentNode.SelectSingleNode("//h1[@id='mch-work-title']")?.InnerText);
            if (string.IsNullOrWhiteSpace(title))
                title = Text(doc.DocumentNode.SelectSingleNode(
                    "//div[contains(@class,'post-title')]//h1")?.InnerText);
            if (string.IsNullOrWhiteSpace(title)) title = slug.Replace('-', ' ');

            var cover = string.Empty;
            var og = Regex.Match(html, "property=\"og:image\"\\s+content=\"([^\"]+)\"", RegexOptions.IgnoreCase);
            if (og.Success) cover = WebUtility.HtmlDecode(og.Groups[1].Value);
            if (string.IsNullOrWhiteSpace(cover))
            {
                var img = doc.DocumentNode.SelectSingleNode("//div[contains(@class,'summary_image')]//img");
                cover = Abs(Attr(img, "src"));
            }

            var status = Text(doc.DocumentNode.SelectSingleNode(
                "//span[contains(@class,'mch-work__status')]")?.InnerText);
            if (string.IsNullOrWhiteSpace(status))
                status = Text(doc.DocumentNode.SelectSingleNode(
                    "//div[contains(@class,'post-status')]//div[contains(@class,'summary-content')]")?.InnerText);

            var desc = Text(doc.DocumentNode.SelectSingleNode(
                "//div[@id='mch-work-synopsis']")?.InnerText);
            if (string.IsNullOrWhiteSpace(desc))
                desc = Text(doc.DocumentNode.SelectSingleNode(
                    "//div[contains(@class,'description-summary')]//div[contains(@class,'summary-content')]")?.InnerText);

            var genres = new List<string>();
            var nodes = doc.DocumentNode.SelectNodes("//dl[contains(@class,'mch-work__metadata')]//a") ??
                        doc.DocumentNode.SelectNodes("//div[contains(@class,'genres-content')]//a");
            if (nodes != null)
            {
                foreach (var a in nodes)
                {
                    var href = Attr(a, "href");
                    if (href.IndexOf("genero=", StringComparison.OrdinalIgnoreCase) < 0 &&
                        href.IndexOf("manga-genre", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    var g = Text(a.InnerText);
                    if (g.Length > 0 && !genres.Contains(g, StringComparer.OrdinalIgnoreCase)) genres.Add(g);
                }
            }

            var chapters = ParseChapters(doc);
            var year = 0;
            if (doc.DocumentNode.SelectSingleNode("//a[contains(@href,'?ano=')]") is HtmlNode y)
                int.TryParse(Regex.Match(Attr(y, "href"), "ano=(\\d{4})").Groups[1].Value, out year);

            return new Manga
            {
                Source = Key,
                Id = slug,
                Title = title,
                Cover = cover,
                Description = desc,
                Status = status,
                Genres = genres,
                Year = year,
                Chapters = chapters.Count,
                Adult = true,
                Url = url,
            };
        }

        private static List<Chapter> ParseChapters(HtmlDocument doc)
        {
            var list = new List<Chapter>();
            var nodes = doc.DocumentNode.SelectNodes("//li[contains(@class,'wp-manga-chapter')]");
            if (nodes == null) return list;

            foreach (var li in nodes)
            {
                var a = li.SelectSingleNode(".//a");
                if (a == null) continue;
                var href = Attr(a, "href");
                if (href.Length == 0) continue;
                var title = Text(a.InnerText);
                if (title.Length == 0) title = "Capítulo";
                var time = li.SelectSingleNode(".//time");
                var date = time != null ? Attr(time, "datetime") : string.Empty;
                list.Add(new Chapter
                {
                    Id = Abs(href),
                    Title = title,
                    Number = NumberOf(title),
                    Date = date,
                });
            }

            list.Sort((x, y) => x.Number.CompareTo(y.Number));
            return list;
        }

        public async Task<List<Chapter>> ChaptersAsync(string id)
        {
            var slug = (id ?? string.Empty).Trim().Trim('/');
            if (string.IsNullOrWhiteSpace(slug)) return new List<Chapter>();
            var html = await HtmlHttp.GetAsync(Site + "/manga/" + Uri.EscapeDataString(slug) + "/",
                Site + "/").ConfigureAwait(false);
            return ParseChapters(Load(html));
        }

        // ------------------------------------------------------------------
        // Leitor
        // ------------------------------------------------------------------
        public async Task<PageSet> PagesAsync(string token, bool adult)
        {
            var url = Abs((token ?? string.Empty).Trim());
            if (string.IsNullOrWhiteSpace(url)) throw new Exception("Capitulo invalido.");
            if (url.IndexOf("/cap-", StringComparison.OrdinalIgnoreCase) < 0 &&
                url.IndexOf("/chapter", StringComparison.OrdinalIgnoreCase) < 0 &&
                url.IndexOf("/capitulo", StringComparison.OrdinalIgnoreCase) < 0)
                throw new Exception("Capitulo invalido.");

            var html = await HtmlHttp.GetAsync(url, Site + "/").ConfigureAwait(false);
            var doc = Load(html);
            var set = new PageSet();

            var imgs = doc.DocumentNode.SelectNodes(
                "//div[contains(@class,'reading-content')]//img[contains(@class,'wp-manga-chapter-img')]");
            if (imgs == null || imgs.Count == 0)
                imgs = doc.DocumentNode.SelectNodes("//div[contains(@class,'reading-content')]//img");
            if (imgs == null)
                imgs = doc.DocumentNode.SelectNodes("//img[contains(@class,'wp-manga-chapter-img')]");

            if (imgs != null)
            {
                foreach (var img in imgs)
                {
                    var u = Attr(img, "data-src");
                    if (u.Length == 0) u = Attr(img, "src");
                    if (u.Length == 0 || u.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) continue;
                    u = Abs(u);
                    if (!set.Images.Contains(u)) set.Images.Add(u);
                }
            }

            if (set.Images.Count == 0) throw new Exception("Capitulo sem paginas disponiveis.");
            return set;
        }

        public Task<List<string>> GenresAsync() => Task.FromResult(UiGenres.ToList());
    }
}
