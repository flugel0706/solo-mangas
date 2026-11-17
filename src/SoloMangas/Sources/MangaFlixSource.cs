using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HtmlAgilityPack;
using SoloMangas.Shared;

namespace SoloMangasNative
{
    /// <summary>MangaFlix (PT-BR): catalogo SSR + API publica.</summary>
    public sealed class MangaFlixSource : ISource
    {
        private const string Base = "https://mangaflix.net";
        private const string Api = "https://api.mangaflix.net";
        private const string Lang = "pt-BR";

        private static readonly HashSet<string> AdultCats = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "hentai", "bara", "adulto", "adult", "+18", "maduro",
        };

        private static readonly (string Name, string Id)[] CatsRaw =
        {
            ("Ação", "6511e95bae08773cd411c21c"), ("Aventura", "6511e95bae08773cd411c224"),
            ("Artes Marciais", "6511e95bae08773cd411c228"), ("Sobrenatural", "6511e95bae08773cd411c236"),
            ("Super Poderes", "6511e95bae08773cd411c23d"), ("Shounen", "6511e95bae08773cd411c22f"),
            ("Comédia", "6511e95fae08773cd411c257"), ("Fantasia", "6511e95fae08773cd411c25d"),
            ("Romance", "6511e960ae08773cd411c280"), ("Drama", "6511e960ae08773cd411c271"),
            ("Militar", "6511e960ae08773cd411c278"), ("Magia", "6511e962ae08773cd411c29a"),
            ("Ecchi", "6511e962ae08773cd411c293"), ("Horror", "6511e963ae08773cd411c2ac"),
            ("Psicológico", "6511e965ae08773cd411c2e2"), ("Seinen", "6511e968ae08773cd411c3ab"),
            ("Jogos", "6511e969ae08773cd411c3e2"), ("Sci-Fi", "6511e969ae08773cd411c3ef"),
            ("Escolar", "6511e96eae08773cd411c7c4"), ("Harém", "6511ea40ae08773cd4135ba4"),
            ("Histórico", "6511ea16ae08773cd412de8d"), ("Adulto", "6511eab6ae08773cd4156a0b"),
            ("Samurai", "6511eb1bae08773cd4174d34"), ("Slice of Life", "6511eb5eae08773cd4189ec5"),
            ("Esportes", "6511eba9ae08773cd419ed91"), ("Shoujo", "6511ecc7ae08773cd41f3c01"),
            ("Suspense", "6511f3fdae08773cd43fd689"), ("Vampiros", "6511f470ae08773cd441a5f0"),
            ("Música", "6511f6e0ae08773cd44cdd2b"), ("Hentai", "65120081ae08773cd47825b7"),
            ("Carros", "6512023fae08773cd47f1ebd"), ("One Shot", "6512040fae08773cd486e722"),
            ("Yaoi", "6512060dae08773cd48ff596"), ("Yuri", "65120d6aae08773cd4b1aea5"),
            ("Gastronomia", "651216e7ae08773cd4da63c6"), ("Isekai", "65123532ae08773cd4312786"),
            ("Doujinshi", "65124189ae08773cd44f9283"), ("Light Novel", "6512470eae08773cd460dd91"),
            ("Bara", "6513a232d0e2acdd1e0b1611"), ("Celebridade", "686bfa804f1137c67fa9ec86"),
            ("Mafia", "686bfbb3a2ef8b3ff65f7a1b"), ("Vida Escolar", "686c0306596e7bd72f7263e9"),
            ("Adult", "686c05d8bff8586a92e44696"), ("Zumbi", "686c08a1a5d3434ba7f0afb5"),
            ("Sci-fi", "686c0dcf596e7bd72f72f098"), ("Policial", "686c1119a5d3434ba7f1096a"),
            ("Terror", "686c1ef3a5d3434ba7f1a012"), ("Reencarnação", "686c741f596e7bd72f765154"),
            ("Historico", "6888f3c9a6835c4d386dfe34"), ("Demônio", "68898b513aadd2f9c93a5ac8"),
            ("Adaptação", "68898b833aadd2f9c93a5c2c"), ("Regressão", "68898bc83aadd2f9c93a5d9f"),
            ("Vilã", "68898be93aadd2f9c93a5def"), ("+18", "6889971f259b62c998d28e21"),
        };

        private static readonly List<(string Name, string Id)> Cats = BuildCats();

        private static List<(string Name, string Id)> BuildCats()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var outp = new List<(string, string)>();
            foreach (var (rawName, id) in CatsRaw)
            {
                var key = Norm(rawName);
                if (!seen.Add(key)) continue;
                var name = rawName.Trim();
                if (name.Length > 0 && char.IsLower(name[0])) name = char.ToUpper(name[0]) + name.Substring(1);
                outp.Add((name, id));
            }
            return outp;
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

        private static bool IsAdultCat(string name) => AdultCats.Contains(Norm(name));

        private static string CatId(string name)
        {
            var n = Norm(name);
            foreach (var (label, id) in Cats) if (Norm(label) == n) return id;
            return string.Empty;
        }

        private List<Manga> _browse;

        public string Key => "mangaflix";
        public string Name => "MangaFlix";
        public bool SupportsLogin => false;
        public bool AdultOnly => false;

        // ------------------------------------------------------------------
        // HTTP
        // ------------------------------------------------------------------
        private static async Task<string> GetTextAsync(string url)
        {
            var res = await HttpUtil.RequestAsync(url, headers: new Dictionary<string, string>
            {
                ["Accept"] = "text/html,application/xhtml+xml",
            }, timeoutMs: 30000, userAgent: HttpUtil.ChromeUa).ConfigureAwait(false);
            if (res.Status < 200 || res.Status >= 300)
                throw new Exception("MangaFlix respondeu " + res.Status + ".");
            return res.Raw ?? string.Empty;
        }

        private static async Task<JsonElement> GetJsonAsync(string url)
        {
            var res = await HttpUtil.RequestAsync(url, headers: new Dictionary<string, string>
            {
                ["Accept"] = "application/json",
            }, timeoutMs: 30000, userAgent: HttpUtil.ChromeUa).ConfigureAwait(false);
            if (res.Status < 200 || res.Status >= 300) throw new Exception("MangaFlix respondeu " + res.Status + ".");
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(res.Raw) ? "{}" : res.Raw);
            return doc.RootElement.Clone();
        }

        private static string Abs(string u)
        {
            if (string.IsNullOrWhiteSpace(u)) return string.Empty;
            if (u.StartsWith("//")) return "https:" + u;
            if (u.StartsWith("/")) return Base + u;
            return u;
        }

        private static string Txt(HtmlNode n) =>
            n == null ? string.Empty : Regex.Replace(HtmlEntity.DeEntitize(n.InnerText ?? string.Empty), "\\s+", " ").Trim();

        // ------------------------------------------------------------------
        // Scraping
        // ------------------------------------------------------------------
        private static List<Manga> ParseCards(HtmlDocument doc)
        {
            var outp = new List<Manga>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var a in doc.DocumentNode.SelectNodes("//a[starts-with(@href,'/br/manga/')]") ?? Enumerable.Empty<HtmlNode>())
            {
                var href = a.GetAttributeValue("href", string.Empty);
                var m = Regex.Match(href, "/br/manga/([a-f0-9]{24})", RegexOptions.IgnoreCase);
                if (!m.Success) continue;
                var id = m.Groups[1].Value;
                if (!seen.Add(id)) continue;
                var img = a.SelectSingleNode(".//img");
                var titleNode = a.SelectSingleNode(".//span[contains(@class,'text-white') and contains(@class,'font-bold')]")
                                ?? a.SelectSingleNode(".//span[contains(@class,'text-gray-100') and contains(@class,'font-bold')]");
                var title = Txt(titleNode);
                if (string.IsNullOrWhiteSpace(title) && img != null)
                    title = Regex.Replace(img.GetAttributeValue("alt", string.Empty), "^Poster do Manga\\s*", "", RegexOptions.IgnoreCase).Trim();
                var desc = Txt(a.SelectSingleNode(".//p[contains(@class,'line-clamp-4')]"));
                outp.Add(new Manga
                {
                    Source = "mangaflix",
                    Id = id,
                    Title = string.IsNullOrWhiteSpace(title) ? "Sem titulo" : title,
                    Cover = img == null ? string.Empty : Abs(img.GetAttributeValue("src", string.Empty)),
                    Description = desc,
                    Url = Base + "/br/manga/" + id,
                });
            }
            return outp;
        }

        private async Task<List<Manga>> BrowseAllAsync()
        {
            if (_browse != null) return _browse;
            var html = await GetTextAsync(Base + "/br/browse").ConfigureAwait(false);
            var doc = new HtmlDocument();
            doc.LoadHtml(html);
            _browse = ParseCards(doc);
            return _browse;
        }

        private async Task<(List<Manga> Items, bool HasMore)> CategoryPageAsync(string id, int page)
        {
            var html = await GetTextAsync(Base + "/br/categories/" + id + (page > 1 ? "?page=" + page : string.Empty)).ConfigureAwait(false);
            var doc = new HtmlDocument();
            doc.LoadHtml(html);
            var items = ParseCards(doc);
            var hasMore = false;
            foreach (var a in doc.DocumentNode.SelectNodes("//a[contains(@href,'page=')]") ?? Enumerable.Empty<HtmlNode>())
            {
                var m = Regex.Match(a.GetAttributeValue("href", string.Empty), "page=(\\d+)");
                if (m.Success && int.TryParse(m.Groups[1].Value, out var n) && n == page + 1) { hasMore = true; break; }
            }
            return (items, hasMore);
        }

        // ------------------------------------------------------------------
        // Detalhe
        // ------------------------------------------------------------------
        private readonly Dictionary<string, (DateTime At, JsonElement Data)> _detailCache =
            new Dictionary<string, (DateTime, JsonElement)>();

        private async Task<JsonElement> DetailAsync(string id)
        {
            lock (_detailCache)
            {
                if (_detailCache.TryGetValue(id, out var hit) && DateTime.UtcNow - hit.At < TimeSpan.FromMinutes(10))
                    return hit.Data;
            }
            var j = await GetJsonAsync(Api + "/v1/mangas/" + Uri.EscapeDataString(id) + "?selected_language=" + Lang).ConfigureAwait(false);
            if (!j.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
                throw new Exception("Obra nao encontrada no MangaFlix.");
            lock (_detailCache) _detailCache[id] = (DateTime.UtcNow, data);
            return data;
        }

        private static string JStr(JsonElement e, string name)
        {
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v)) return string.Empty;
            return v.ValueKind == JsonValueKind.String ? v.GetString() ?? string.Empty : string.Empty;
        }

        private static string Poster(JsonElement d)
        {
            if (d.TryGetProperty("poster", out var p) && p.ValueKind == JsonValueKind.Object)
                return JStr(p, "default_url");
            return string.Empty;
        }

        public async Task<CatalogResult> CatalogAsync(SourceQuery query)
        {
            var page = new CatalogResult { Page = Math.Max(1, query.Page) };
            const int limit = 24;
            var q = (query.Q ?? string.Empty).Trim();
            var genre = (query.Genre ?? string.Empty).Trim();

            if (q.Length > 0)
            {
                var j = await GetJsonAsync(Api + "/v1/search/mangas?query=" + Uri.EscapeDataString(q) +
                                           "&selected_language=" + Lang).ConfigureAwait(false);
                if (j.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object &&
                    data.TryGetProperty("works", out var works) && works.ValueKind == JsonValueKind.Array)
                {
                    foreach (var w in works.EnumerateArray())
                    {
                        var id = JStr(w, "_id");
                        if (string.IsNullOrEmpty(id)) continue;
                        page.Items.Add(new Manga
                        {
                            Source = "mangaflix",
                            Id = id,
                            Title = JStr(w, "name") is var t && t.Length > 0 ? t : "Sem titulo",
                            Cover = Poster(w),
                            Description = JStr(w, "description"),
                            Chapters = w.TryGetProperty("chapters_count", out var cc) && cc.ValueKind == JsonValueKind.Number ? cc.GetInt32() : 0,
                            Url = Base + "/br/manga/" + id,
                        });
                    }
                }
                page.Total = page.Items.Count;
                return page;
            }

            if (genre.Length > 0 && Norm(genre) != "tudo")
            {
                var cid = CatId(genre);
                if (string.IsNullOrEmpty(cid)) return page;
                if (IsAdultCat(genre) && !query.Adult) return page;
                var r = await CategoryPageAsync(cid, page.Page).ConfigureAwait(false);
                page.Items = r.Items;
                page.HasMore = r.HasMore;
                return page;
            }

            var all = await BrowseAllAsync().ConfigureAwait(false);
            var start = (page.Page - 1) * limit;
            page.Items = all.Skip(start).Take(limit).ToList();
            page.Total = all.Count;
            page.HasMore = start + limit < all.Count;
            return page;
        }

        public async Task<Manga> MangaAsync(string id, bool adult)
        {
            var d = await DetailAsync(id).ConfigureAwait(false);
            var genres = new List<string>();
            var isAdult = false;
            if (d.TryGetProperty("genres", out var gs) && gs.ValueKind == JsonValueKind.Array)
            {
                foreach (var g in gs.EnumerateArray())
                {
                    var name = JStr(g, "name");
                    if (!string.IsNullOrWhiteSpace(name) && !genres.Contains(name)) genres.Add(name);
                    if (g.TryGetProperty("adult", out var ad) && ad.ValueKind == JsonValueKind.True) isAdult = true;
                }
            }
            var chapters = 0;
            if (d.TryGetProperty("chapters_count", out var cc) && cc.ValueKind == JsonValueKind.Number) cc.TryGetInt32(out chapters);
            else if (d.TryGetProperty("chapters", out var carr) && carr.ValueKind == JsonValueKind.Array) chapters = carr.GetArrayLength();

            return new Manga
            {
                Source = "mangaflix",
                Id = JStr(d, "_id") is var did && did.Length > 0 ? did : id,
                Title = JStr(d, "name") is var t && t.Length > 0 ? t : "Sem titulo",
                Cover = Poster(d),
                Description = JStr(d, "description"),
                Adult = isAdult,
                Chapters = chapters,
                Genres = genres,
                Url = Base + "/br/manga/" + id,
            };
        }

        public async Task<List<Chapter>> ChaptersAsync(string id)
        {
            var d = await DetailAsync(id).ConfigureAwait(false);
            var list = new List<Chapter>();
            if (!d.TryGetProperty("chapters", out var arr) || arr.ValueKind != JsonValueKind.Array) return list;
            foreach (var c in arr.EnumerateArray())
            {
                var cid = JStr(c, "_id");
                if (string.IsNullOrEmpty(cid)) continue;
                var name = JStr(c, "name").Trim();
                var number = JStr(c, "number").Trim();
                list.Add(new Chapter
                {
                    Id = cid,
                    Title = name.Length > 0 ? "Cap. " + number + " — " + name : "Cap. " + number,
                    Number = double.TryParse(number.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : 0,
                    Date = JStr(c, "release_date"),
                });
            }
            return list;
        }

        public async Task<PageSet> PagesAsync(string token, bool adult)
        {
            var j = await GetJsonAsync(Api + "/v1/chapters/" + Uri.EscapeDataString(token) +
                                       "?selected_language=" + Lang).ConfigureAwait(false);
            var set = new PageSet();
            if (j.TryGetProperty("data", out var d) && d.ValueKind == JsonValueKind.Object &&
                d.TryGetProperty("images", out var imgs) && imgs.ValueKind == JsonValueKind.Array)
            {
                var ordered = new List<(double Order, string Url)>();
                foreach (var img in imgs.EnumerateArray())
                {
                    var url = JStr(img, "default_url");
                    if (string.IsNullOrWhiteSpace(url)) continue;
                    double order = 0;
                    if (img.TryGetProperty("order", out var ov) && ov.ValueKind == JsonValueKind.Number) ov.TryGetDouble(out order);
                    ordered.Add((order, url));
                }
                foreach (var it in ordered.OrderBy(x => x.Order)) set.Images.Add(it.Url);
            }
            if (set.Images.Count == 0) throw new Exception("Capitulo sem paginas disponiveis.");
            return set;
        }

        public Task<List<string>> GenresAsync() => Task.FromResult(Cats.Select(c => c.Name).ToList());
    }
}
