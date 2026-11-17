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
    /// HQ Brasa (hqbrasa.com): quadrinhos hentai em portugues publicados como
    /// obra unica (1 capitulo) ou series curtas. Catalogo em /lista, busca em
    /// /busca?q= e leitor com imagens no CDN proprio.
    /// </summary>
    public sealed class HQBrasaSource : ISource
    {
        private const string Site = "https://hqbrasa.com";

        private static readonly (string Name, string Slug)[] GenreSlugs =
        {
            ("Comics", "comics"),
            ("Mangá Hentai", "manga-hentai"),
        };

        public string Key => "hqbrasa";
        public string Name => "HQ Brasa";
        public bool SupportsLogin => false;
        public bool AdultOnly => true;

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
            var i = Array.FindIndex(parts, p => string.Equals(p, "hq", StringComparison.OrdinalIgnoreCase));
            if (i >= 0 && i + 1 < parts.Length) return parts[i + 1];
            return parts.Length == 0 ? string.Empty : parts[parts.Length - 1];
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
            string url;
            var search = !string.IsNullOrWhiteSpace(query.Q);

            if (search)
            {
                url = Site + "/busca?q=" + Uri.EscapeDataString(query.Q.Trim());
            }
            else if (!string.IsNullOrWhiteSpace(query.Genre))
            {
                var slug = GenreSlugs.FirstOrDefault(g =>
                    string.Equals(g.Name, query.Genre.Trim(), StringComparison.OrdinalIgnoreCase)).Slug;
                if (string.IsNullOrWhiteSpace(slug)) slug = "comics";
                url = Site + "/categoria/" + slug + (page > 1 ? "?page=" + page.ToString(CultureInfo.InvariantCulture) : string.Empty);
            }
            else
            {
                url = Site + "/lista?page=" + page.ToString(CultureInfo.InvariantCulture);
            }

            var html = await HtmlHttp.GetAsync(url, Site + "/").ConfigureAwait(false);
            var doc = Load(html);
            var result = new CatalogResult { Page = page };

            var cards = doc.DocumentNode.SelectNodes("//article[contains(@class,'manga-card')]");
            if (cards != null)
            {
                foreach (var card in cards)
                {
                    var link = card.SelectSingleNode(".//a[contains(@class,'manga-card-link')]")
                               ?? card.SelectSingleNode(".//a");
                    var href = Attr(link, "href");
                    var id = SlugFromUrl(href);
                    if (string.IsNullOrWhiteSpace(id)) continue;

                    var img = card.SelectSingleNode(".//img");
                    var title = Attr(link, "title");
                    if (string.IsNullOrWhiteSpace(title))
                        title = Text(card.SelectSingleNode(".//span[contains(@class,'title')]")?.InnerText);
                    if (string.IsNullOrWhiteSpace(title)) title = id.Replace('-', ' ');

                    var meta = Text(card.SelectSingleNode(".//div[contains(@class,'meta')]")?.InnerText);
                    result.Items.Add(new Manga
                    {
                        Source = Key,
                        Id = id,
                        Title = title,
                        Cover = Abs(Attr(img, "src") is var c && c.Length > 0 ? c : Attr(img, "data-src")),
                        Description = meta,
                        Adult = true,
                        Url = Site + "/hq/" + id,
                    });
                }
            }

            var pages = 0;
            var pm = Regex.Match(html, "class=\"current\"[^>]*>\\s*(\\d+)\\s*/\\s*(\\d+)", RegexOptions.IgnoreCase);
            if (pm.Success) int.TryParse(pm.Groups[2].Value, out pages);
            result.HasMore = search ? false : (pages > 0 && page < pages);
            result.Total = pages > 0 ? pages * 24L : result.Items.Count;
            return result;
        }

        // ------------------------------------------------------------------
        // Obra
        // ------------------------------------------------------------------
        public async Task<Manga> MangaAsync(string id, bool adult)
        {
            var slug = (id ?? string.Empty).Trim().Trim('/');
            if (string.IsNullOrWhiteSpace(slug)) throw new Exception("Obra invalida.");
            var url = Site + "/hq/" + Uri.EscapeDataString(slug);

            var html = await HtmlHttp.GetAsync(url, Site + "/").ConfigureAwait(false);
            var doc = Load(html);

            var title = Text(doc.DocumentNode.SelectSingleNode("//h1")?.InnerText);
            if (string.IsNullOrWhiteSpace(title)) title = slug.Replace('-', ' ');

            var cover = string.Empty;
            var og = Regex.Match(html, "property=\"og:image\"\\s+content=\"([^\"]+)\"", RegexOptions.IgnoreCase);
            if (og.Success) cover = WebUtility.HtmlDecode(og.Groups[1].Value);

            var desc = string.Empty;
            var md = Regex.Match(html, "name=\"description\"\\s+content=\"([^\"]+)\"", RegexOptions.IgnoreCase);
            if (md.Success) desc = WebUtility.HtmlDecode(md.Groups[1].Value);

            var genres = new List<string>();
            var catNodes = doc.DocumentNode.SelectNodes("//a[contains(@href,'/categoria/')]");
            if (catNodes != null)
            {
                foreach (var a in catNodes)
                {
                    var g = Text(a.InnerText);
                    if (string.IsNullOrWhiteSpace(g)) continue;
                    g = g.Trim();
                    if (g.Length > 30) continue;
                    if (!genres.Contains(g, StringComparer.OrdinalIgnoreCase)) genres.Add(g);
                }
            }

            var chapters = new List<Chapter>();
            var links = doc.DocumentNode.SelectNodes("//a[contains(@href,'/capitulo/')]");
            if (links != null)
            {
                foreach (var a in links)
                {
                    var href = Attr(a, "href");
                    if (href.IndexOf("/download", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    if (href.Length == 0) continue;
                    var abs = Abs(href);
                    if (chapters.Any(c => string.Equals(c.Id, abs, StringComparison.OrdinalIgnoreCase))) continue;
                    var label = Text(a.InnerText);
                    if (string.IsNullOrWhiteSpace(label) || label.IndexOf("download", StringComparison.OrdinalIgnoreCase) >= 0)
                        label = "Capítulo";
                    double num = 0;
                    var nm = Regex.Match(abs, "/capitulo/(\\d+)", RegexOptions.IgnoreCase);
                    if (nm.Success) double.TryParse(nm.Groups[1].Value, NumberStyles.Float,
                        CultureInfo.InvariantCulture, out num);
                    chapters.Add(new Chapter { Id = abs, Title = label, Number = num });
                }
            }
            chapters.Sort((x, y) => x.Number.CompareTo(y.Number));

            return new Manga
            {
                Source = Key,
                Id = slug,
                Title = title,
                Cover = cover,
                Description = desc,
                Adult = true,
                Genres = genres,
                Chapters = chapters.Count,
                Url = url,
            };
        }

        public Task<List<Chapter>> ChaptersAsync(string id) => ChaptersOnlyAsync(id);

        private async Task<List<Chapter>> ChaptersOnlyAsync(string id)
        {
            var slug = (id ?? string.Empty).Trim().Trim('/');
            var html = await HtmlHttp.GetAsync(Site + "/hq/" + Uri.EscapeDataString(slug), Site + "/")
                .ConfigureAwait(false);
            var doc = Load(html);
            var list = new List<Chapter>();
            var links = doc.DocumentNode.SelectNodes("//a[contains(@href,'/capitulo/')]");
            if (links == null) return list;
            foreach (var a in links)
            {
                var href = Attr(a, "href");
                if (href.IndexOf("/download", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                var abs = Abs(href);
                if (abs.Length == 0 || list.Any(c => string.Equals(c.Id, abs, StringComparison.OrdinalIgnoreCase)))
                    continue;
                var label = Text(a.InnerText);
                if (string.IsNullOrWhiteSpace(label) || label.IndexOf("download", StringComparison.OrdinalIgnoreCase) >= 0)
                    label = "Capítulo";
                double num = 0;
                var nm = Regex.Match(abs, "/capitulo/(\\d+)", RegexOptions.IgnoreCase);
                if (nm.Success) double.TryParse(nm.Groups[1].Value, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out num);
                list.Add(new Chapter { Id = abs, Title = label, Number = num });
            }
            list.Sort((x, y) => x.Number.CompareTo(y.Number));
            return list;
        }

        // ------------------------------------------------------------------
        // Leitor
        // ------------------------------------------------------------------
        public async Task<PageSet> PagesAsync(string token, bool adult)
        {
            var url = Abs((token ?? string.Empty).Trim());
            if (string.IsNullOrWhiteSpace(url) || url.IndexOf("/capitulo/", StringComparison.OrdinalIgnoreCase) < 0)
                throw new Exception("Capitulo invalido.");

            var html = await HtmlHttp.GetAsync(url, Site + "/").ConfigureAwait(false);
            var doc = Load(html);
            var set = new PageSet();

            var imgs = doc.DocumentNode.SelectNodes("//img[contains(@class,'reader-page')]")
                       ?? doc.DocumentNode.SelectNodes("//img[contains(@class,'reader-img')]");
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

        public Task<List<string>> GenresAsync() => Task.FromResult(GenreSlugs.Select(g => g.Name).ToList());
    }
}
