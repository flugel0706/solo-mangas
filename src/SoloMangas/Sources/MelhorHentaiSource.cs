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
    /// Melhor Hentai (melhorhentai.com): doujinshi, mangas hentai e comics em
    /// portugues. Cada obra e um one-shot com todas as paginas na propria pagina,
    /// servidas de /wp-content/uploads/mh-obras/.
    /// </summary>
    public sealed class MelhorHentaiSource : ISource
    {
        private const string Site = "https://melhorhentai.com";

        private static readonly (string Name, string Slug)[] GenreSlugs =
        {
            ("Doujinshi", "doujinshi"),
            ("Mangá Hentai", "manga-hentai"),
            ("Comics", "comics"),
        };

        public string Key => "melhorhentai";
        public string Name => "Melhor Hentai";
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
            if (parts.Length >= 2 && string.Equals(parts[parts.Length - 2], "categoria", StringComparison.OrdinalIgnoreCase))
                return string.Empty;
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
                url = Site + "/?s=" + Uri.EscapeDataString(query.Q.Trim());
            }
            else
            {
                var slug = "doujinshi";
                if (!string.IsNullOrWhiteSpace(query.Genre))
                {
                    var mapped = GenreSlugs.FirstOrDefault(g =>
                        string.Equals(g.Name, query.Genre.Trim(), StringComparison.OrdinalIgnoreCase)).Slug;
                    if (!string.IsNullOrWhiteSpace(mapped)) slug = mapped;
                }
                url = Site + "/categoria/" + slug + "/" +
                      (page > 1 ? "page/" + page.ToString(CultureInfo.InvariantCulture) + "/" : string.Empty);
            }

            var html = await HtmlHttp.GetAsync(url, Site + "/").ConfigureAwait(false);
            var doc = Load(html);
            var result = new CatalogResult { Page = page };

            var cards = doc.DocumentNode.SelectNodes(
                "//a[contains(concat(' ', normalize-space(@class), ' '), ' card ')]");
            if (cards != null)
            {
                foreach (var card in cards)
                {
                    var href = Attr(card, "href");
                    var id = SlugFromUrl(href);
                    if (string.IsNullOrWhiteSpace(id)) continue;
                    var title = Text(card.SelectSingleNode(".//div[contains(@class,'card-title')]")?.InnerText);
                    if (string.IsNullOrWhiteSpace(title)) title = id.Replace('-', ' ');
                    var img = card.SelectSingleNode(".//img[contains(@class,'mh-capa-img')]")
                              ?? card.SelectSingleNode(".//img");
                    var cover = Attr(img, "src");
                    if (cover.Length == 0) cover = Attr(img, "data-src");
                    result.Items.Add(new Manga
                    {
                        Source = Key,
                        Id = id,
                        Title = title,
                        Cover = Abs(cover),
                        Adult = true,
                        Url = Site + "/" + id + "/",
                    });
                }
            }

            result.HasMore = search ? false : result.Items.Count >= 20;
            return result;
        }

        // ------------------------------------------------------------------
        // Obra
        // ------------------------------------------------------------------
        public async Task<Manga> MangaAsync(string id, bool adult)
        {
            var slug = (id ?? string.Empty).Trim().Trim('/');
            if (string.IsNullOrWhiteSpace(slug)) throw new Exception("Obra invalida.");
            var url = Site + "/" + Uri.EscapeDataString(slug) + "/";

            var html = await HtmlHttp.GetAsync(url, Site + "/").ConfigureAwait(false);
            var doc = Load(html);

            var title = Text(doc.DocumentNode.SelectSingleNode("//h1")?.InnerText);
            if (string.IsNullOrWhiteSpace(title)) title = slug.Replace('-', ' ');

            var cover = string.Empty;
            var coverImg = doc.DocumentNode.SelectSingleNode("//img[contains(@src,'-capa')]");
            if (coverImg != null) cover = Attr(coverImg, "src");
            if (string.IsNullOrWhiteSpace(cover))
            {
                var og = Regex.Match(html, "property=\"og:image\"\\s+content=\"([^\"]+)\"", RegexOptions.IgnoreCase);
                if (og.Success) cover = WebUtility.HtmlDecode(og.Groups[1].Value);
            }

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
                    if (string.IsNullOrWhiteSpace(g) || g.Length > 30) continue;
                    if (!genres.Contains(g, StringComparer.OrdinalIgnoreCase)) genres.Add(g);
                }
            }

            return new Manga
            {
                Source = Key,
                Id = slug,
                Title = title,
                Cover = cover,
                Description = desc,
                Adult = true,
                Genres = genres,
                Chapters = 1,
                Url = url,
            };
        }

        public Task<List<Chapter>> ChaptersAsync(string id)
        {
            var slug = (id ?? string.Empty).Trim().Trim('/');
            var list = new List<Chapter>
            {
                new Chapter
                {
                    Id = Site + "/" + slug + "/",
                    Title = "Capítulo único",
                    Number = 1,
                },
            };
            return Task.FromResult(list);
        }

        // ------------------------------------------------------------------
        // Leitor
        // ------------------------------------------------------------------
        public async Task<PageSet> PagesAsync(string token, bool adult)
        {
            var url = Abs((token ?? string.Empty).Trim());
            if (string.IsNullOrWhiteSpace(url)) throw new Exception("Capitulo invalido.");
            if (url.IndexOf("melhorhentai.com", StringComparison.OrdinalIgnoreCase) < 0)
                throw new Exception("Capitulo invalido.");

            var html = await HtmlHttp.GetAsync(url, Site + "/").ConfigureAwait(false);
            var set = new PageSet();

            foreach (Match m in Regex.Matches(html,
                         "https://melhorhentai\\.com/wp-content/uploads/mh-obras/[^\"'\\s>]+\\.(?:jpg|jpeg|png|webp)",
                         RegexOptions.IgnoreCase))
            {
                var u = WebUtility.HtmlDecode(m.Value);
                if (u.Length == 0 || set.Images.Contains(u)) continue;
                set.Images.Add(u);
            }

            if (set.Images.Count == 0) throw new Exception("Capitulo sem paginas disponiveis.");
            return set;
        }

        public Task<List<string>> GenresAsync() => Task.FromResult(GenreSlugs.Select(g => g.Name).ToList());
    }
}
