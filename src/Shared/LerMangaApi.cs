using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace SoloMangas.Shared
{
    /// <summary>
    /// Fonte Ler Mangá Online (lermangaonline.com.br).
    /// O catalogo e HTML (tema proprio, cards .mlo-card). Os dados da obra vem
    /// do REST do WordPress (post type mbi_manga) e os capitulos/paginas usam
    /// os endpoints publicos do tema (mlo_chapter_batch e mbi_chapter).
    /// Conteudo gratuito, em portugues, com capitulos completos.
    /// </summary>
    public sealed class LerMangaApi
    {
        private const string Site = "https://lermangaonline.com.br";
        private const string Rest = Site + "/wp-json/wp/v2";

        private readonly HttpClient _client;
        private readonly object _gate = new object();
        private readonly Dictionary<string, Tuple<DateTime, object>> _cache =
            new Dictionary<string, Tuple<DateTime, object>>(StringComparer.Ordinal);

        private static readonly Regex CardRe = new Regex(
            "<article\\s+class=\"mlo-card\"[^>]*>([\\s\\S]*?)</article>",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex ListItemRe = new Regex(
            "<li\\s+data-chapter-number=\"([^\"]*)\"[^>]*>\\s*<a\\s+href=\"([^\"]+)\"[^>]*>([\\s\\S]*?)</a>",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex StrongRe = new Regex(
            "<strong>([\\s\\S]*?)</strong>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly HashSet<string> AdultSlugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "18", "18-26", "adulto", "adulto-29", "hentai", "smut", "ahegao", "boquete",
            "creampie", "incesto", "masturbacao", "loli", "milf", "grupal", "bunda-grande",
            "lingerie", "shotacon", "lolicon", "nsfw",
        };

        private static readonly Dictionary<string, string> GenreSlugs =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Ação"] = "acao",
                ["Aventura"] = "aventura",
                ["Comédia"] = "comedia",
                ["Drama"] = "drama",
                ["Fantasia"] = "fantasia",
                ["Romance"] = "romance",
                ["Isekai"] = "isekai",
                ["Escolar"] = "escolar",
                ["Artes Marciais"] = "artes-marciais",
                ["Terror"] = "horror",
                ["Suspense"] = "thriller",
                ["Mistério"] = "misterio",
                ["Ficção Científica"] = "ficcao-cientifica",
                ["Histórico"] = "historico",
                ["Esporte"] = "esportes",
                ["Mecha"] = "mecha",
                ["Cultivo"] = "cultivo",
                ["Murim"] = "murim",
                ["Sobrenatural"] = "sobrenatural",
                ["Magia"] = "magia",
                ["Harem"] = "harem",
                ["Slice of Life"] = "slice-of-life",
                ["Culinária"] = "cooking",
                ["Shounen"] = "shounen",
                ["Seinen"] = "seinen",
                ["Josei"] = "josei",
                ["Manhwa"] = "manhwa",
                ["Manhua"] = "manhuas",
                ["BL"] = "boys-love",
                ["GL"] = "girls-love",
                ["Yaoi"] = "boys-love",
                ["Yuri"] = "girls-love",
                ["Shounen Ai"] = "shounen-ai",
                ["Ecchi"] = "ecchi",
                ["Adulto"] = "adulto",
            };

        private static readonly Dictionary<string, string> GenrePt =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["acao"] = "Ação",
                ["action"] = "Ação",
                ["aventura"] = "Aventura",
                ["adventure"] = "Aventura",
                ["comedia"] = "Comédia",
                ["comedy"] = "Comédia",
                ["drama"] = "Drama",
                ["fantasia"] = "Fantasia",
                ["fantasy"] = "Fantasia",
                ["romance"] = "Romance",
                ["isekai"] = "Isekai",
                ["escolar"] = "Escolar",
                ["escola"] = "Escolar",
                ["school-life"] = "Escolar",
                ["artes-marciais"] = "Artes Marciais",
                ["martial-arts"] = "Artes Marciais",
                ["horror"] = "Terror",
                ["terror"] = "Terror",
                ["thriller"] = "Suspense",
                ["suspense"] = "Suspense",
                ["misterio"] = "Mistério",
                ["mystery"] = "Mistério",
                ["ficcao-cientifica"] = "Ficção Científica",
                ["sci-fi"] = "Ficção Científica",
                ["historico"] = "Histórico",
                ["historical"] = "Histórico",
                ["esportes"] = "Esporte",
                ["sports"] = "Esporte",
                ["mecha"] = "Mecha",
                ["cultivo"] = "Cultivo",
                ["murim"] = "Murim",
                ["sobrenatural"] = "Sobrenatural",
                ["supernatural"] = "Sobrenatural",
                ["magia"] = "Magia",
                ["magic"] = "Magia",
                ["harem"] = "Harem",
                ["slice-of-life"] = "Slice of Life",
                ["cooking"] = "Culinária",
                ["shounen"] = "Shounen",
                ["seinen"] = "Seinen",
                ["josei"] = "Josei",
                ["manhwa"] = "Manhwa",
                ["manhuas"] = "Manhua",
                ["boys-love"] = "BL",
                ["girls-love"] = "GL",
                ["shounen-ai"] = "Shounen Ai",
                ["ecchi"] = "Ecchi",
                ["adulto"] = "Adulto",
                ["18"] = "Adulto",
                ["18-26"] = "Adulto",
                ["hentai"] = "Hentai",
                ["gore"] = "Gore",
                ["psychological"] = "Psicológico",
                ["psicologico"] = "Psicológico",
                ["reincarnation"] = "Reencarnação",
                ["regressao"] = "Regressão",
                ["time-travel"] = "Regressão",
                ["villainess"] = "Vilã",
                ["vampiros"] = "Vampiro",
                ["vampires"] = "Vampiro",
                ["zumbi"] = "Zumbi",
                ["zombies"] = "Zumbi",
                ["demons"] = "Demônios",
                ["demonios"] = "Demônios",
                ["monsters"] = "Monstros",
                ["monstros"] = "Monstros",
            };

        public static readonly string[] UiGenres =
        {
            "Ação", "Aventura", "Comédia", "Drama", "Fantasia", "Romance", "Isekai", "Escolar",
            "Artes Marciais", "Terror", "Suspense", "Mistério", "Ficção Científica", "Histórico",
            "Esporte", "Mecha", "Cultivo", "Murim", "Sobrenatural", "Magia", "Harem",
            "Slice of Life", "Culinária", "Shounen", "Seinen", "Josei", "Manhwa", "Manhua",
            "BL", "GL", "Yaoi", "Yuri", "Shounen Ai", "Ecchi",
        };

        public LerMangaApi()
        {
            var handler = new SocketsHttpHandler
            {
                AllowAutoRedirect = true,
                AutomaticDecompression = DecompressionMethods.All,
                UseCookies = false,
                ConnectTimeout = TimeSpan.FromSeconds(15),
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                MaxConnectionsPerServer = 8,
            };
            _client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        }

        // ------------------------------------------------------------------
        // HTTP
        // ------------------------------------------------------------------
        private async Task<string> GetStringAsync(string url, string referer = null, string accept = null,
            int timeoutMs = 25000)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("Accept",
                accept ?? "text/html,application/xhtml+xml,application/json;q=0.9,*/*;q=0.8");
            req.Headers.TryAddWithoutValidation("User-Agent", HttpUtil.ChromeUa);
            req.Headers.TryAddWithoutValidation("Accept-Language", "pt-BR,pt;q=0.9,en;q=0.6");
            if (!string.IsNullOrEmpty(referer))
            {
                req.Headers.TryAddWithoutValidation("Referer", referer);
                req.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
            }
            using var cts = new CancellationTokenSource(timeoutMs);
            using var res = await _client.SendAsync(req, HttpCompletionOption.ResponseContentRead, cts.Token)
                .ConfigureAwait(false);
            var body = await res.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            if ((int)res.StatusCode >= 400)
                throw new Exception("Ler Mangá Online respondeu " + (int)res.StatusCode + ".");
            return body;
        }

        private static string Html(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            s = Regex.Replace(s, "<br\\s*/?>", "\n", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, "<[^>]+>", " ");
            s = WebUtility.HtmlDecode(s);
            s = Regex.Replace(s, "[ \\t]+", " ");
            s = Regex.Replace(s, "\\n{3,}", "\n\n");
            return s.Trim();
        }

        private static string CleanTitle(string raw)
        {
            var s = Html(raw);
            s = Regex.Replace(s, "^Ler\\s+", string.Empty, RegexOptions.IgnoreCase);
            s = Regex.Replace(s, "\\s+Online\\s+Gr[áa]tis\\s*$", string.Empty, RegexOptions.IgnoreCase);
            s = Regex.Replace(s, "\\s+Online\\s*$", string.Empty, RegexOptions.IgnoreCase);
            return s.Trim();
        }

        private static string SlugFromUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return string.Empty;
            var u = url.Split('?')[0].TrimEnd('/');
            var i = u.LastIndexOf('/');
            return i >= 0 ? u.Substring(i + 1) : u;
        }

        private static string DeSlug(string slug)
        {
            if (string.IsNullOrWhiteSpace(slug)) return string.Empty;
            if (GenrePt.TryGetValue(slug, out var known)) return known;
            var s = slug.Replace('-', ' ').Replace('_', ' ').Trim();
            if (s.Length == 0) return s;
            return char.ToUpperInvariant(s[0]) + s.Substring(1);
        }

        private static List<string> GenresFromClasses(string[] classes)
        {
            var list = new List<string>();
            if (classes == null) return list;
            foreach (var c in classes)
            {
                if (string.IsNullOrWhiteSpace(c)) continue;
                string slug = null;
                if (c.StartsWith("mbi_genre-", StringComparison.OrdinalIgnoreCase))
                    slug = c.Substring("mbi_genre-".Length);
                else if (c.StartsWith("tag-", StringComparison.OrdinalIgnoreCase))
                    slug = c.Substring("tag-".Length);
                if (string.IsNullOrWhiteSpace(slug)) continue;
                // classes tecnicas e slugs que nao sao genero
                if (slug.StartsWith("ler-manga") || slug.StartsWith("manga") || slug == "mangas" ||
                    slug == "manga-online-gratis" || slug == "hentry" || slug == "mangas") continue;
                if (slug.Contains("online-gratis")) continue;
                var name = DeSlug(slug);
                if (!list.Contains(name, StringComparer.OrdinalIgnoreCase)) list.Add(name);
            }
            return list;
        }

        private static bool IsAdultClasses(string[] classes)
        {
            if (classes == null) return false;
            foreach (var c in classes)
            {
                if (c == null) continue;
                if (c.StartsWith("mbi_genre-", StringComparison.OrdinalIgnoreCase) ||
                    c.StartsWith("tag-", StringComparison.OrdinalIgnoreCase))
                {
                    var slug = c.Substring(c.IndexOf('-') + 1);
                    if (AdultSlugs.Contains(slug)) return true;
                }
            }
            return false;
        }

        private static string StatusFromClasses(string[] classes)
        {
            if (classes == null) return string.Empty;
            foreach (var c in classes)
            {
                if (!c.StartsWith("mbi_status-", StringComparison.OrdinalIgnoreCase)) continue;
                var s = c.Substring("mbi_status-".Length);
                if (s.Contains("andamento") || s.Contains("ongoing")) return "Em andamento";
                if (s.Contains("completo") || s.Contains("completed")) return "Completo";
                if (s.Contains("hiato") || s.Contains("hiatus")) return "Em hiato";
                if (s.Contains("cancel")) return "Cancelado";
                return DeSlug(s);
            }
            return string.Empty;
        }

        private static string TypeFromClasses(string[] classes)
        {
            if (classes == null) return string.Empty;
            foreach (var c in classes)
            {
                if (!c.StartsWith("mbi_type-", StringComparison.OrdinalIgnoreCase)) continue;
                var s = c.Substring("mbi_type-".Length);
                if (s.Contains("manhwa")) return "Manhwa";
                if (s.Contains("manhua")) return "Manhua";
                if (s.Contains("manga")) return "Mangá";
                return DeSlug(s);
            }
            return string.Empty;
        }

        // ------------------------------------------------------------------
        // Cards do catalogo (HTML)
        // ------------------------------------------------------------------
        private static List<Dictionary<string, object>> ParseCards(string html)
        {
            var list = new List<Dictionary<string, object>>();
            foreach (Match m in CardRe.Matches(html ?? string.Empty))
            {
                var block = m.Groups[1].Value;

                var href = Regex.Match(block,
                    "<a\\s+class=\"mlo-card-cover\"\\s+href=\"([^\"]+)\"",
                    RegexOptions.IgnoreCase);
                if (!href.Success)
                    href = Regex.Match(block, "href=\"(" + Regex.Escape(Site) + "/manga/[^\"]+)\"",
                        RegexOptions.IgnoreCase);
                if (!href.Success) continue;

                var img = Regex.Match(block, "<img[^>]+src=\"([^\"]+)\"", RegexOptions.IgnoreCase);
                var type = Regex.Match(block, "<span\\s+class=\"mlo-card-type\">([\\s\\S]*?)</span>",
                    RegexOptions.IgnoreCase);
                var title = Regex.Match(block,
                    "<h3\\s+class=\"mlo-card-title\">\\s*<a[^>]*>([\\s\\S]*?)</a>",
                    RegexOptions.IgnoreCase);
                if (!title.Success)
                    title = Regex.Match(block, "<h3[^>]*>([\\s\\S]*?)</h3>", RegexOptions.IgnoreCase);

                var url = WebUtility.HtmlDecode(href.Groups[1].Value);
                var slug = SlugFromUrl(url);
                if (string.IsNullOrWhiteSpace(slug)) continue;

                list.Add(new Dictionary<string, object>
                {
                    ["id"] = slug,
                    ["title"] = CleanTitle(title.Success ? title.Groups[1].Value : slug),
                    ["title_alt"] = string.Empty,
                    ["cover"] = img.Success ? WebUtility.HtmlDecode(img.Groups[1].Value) : string.Empty,
                    ["description"] = string.Empty,
                    ["adult"] = false,
                    ["chapters"] = 0,
                    ["status"] = type.Success ? Html(type.Groups[1].Value) : string.Empty,
                    ["genres"] = new List<string>(),
                    ["source"] = "lermanga",
                    ["url"] = url,
                });
            }
            return list;
        }

        private async Task<Dictionary<string, JsonElement>> MetaBySlugsAsync(List<string> slugs)
        {
            var map = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            if (slugs == null || slugs.Count == 0) return map;

            var url = new StringBuilder(Rest + "/mbi_manga?per_page=100&_fields=slug,class_list,title,mbi_genre,mbi_status,mbi_type");
            foreach (var slug in slugs.Distinct(StringComparer.OrdinalIgnoreCase))
                url.Append("&slug=").Append(Uri.EscapeDataString(slug));

            JsonElement root;
            try
            {
                var raw = await GetStringAsync(url.ToString(), Site + "/manga/", "application/json")
                    .ConfigureAwait(false);
                using var doc = JsonDocument.Parse(raw);
                root = doc.RootElement.Clone();
            }
            catch
            {
                return map;
            }

            if (root.ValueKind != JsonValueKind.Array) return map;
            foreach (var item in root.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                if (!item.TryGetProperty("slug", out var sv) || sv.ValueKind != JsonValueKind.String) continue;
                map[sv.GetString()] = item;
            }
            return map;
        }

        private static string[] ClassesOf(JsonElement item)
        {
            if (item.ValueKind != JsonValueKind.Object) return Array.Empty<string>();
            if (!item.TryGetProperty("class_list", out var cl) || cl.ValueKind != JsonValueKind.Array)
                return Array.Empty<string>();
            return cl.EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.String)
                .Select(x => x.GetString())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToArray();
        }

        private static void ApplyMeta(Dictionary<string, object> item, JsonElement meta)
        {
            var classes = ClassesOf(meta);
            if (classes.Length == 0) return;
            item["genres"] = GenresFromClasses(classes);
            item["adult"] = IsAdultClasses(classes);
            var status = StatusFromClasses(classes);
            if (!string.IsNullOrWhiteSpace(status)) item["status"] = status;
            var type = TypeFromClasses(classes);
            if (!string.IsNullOrWhiteSpace(type)) item["type"] = type;
        }

        // ------------------------------------------------------------------
        // API publica
        // ------------------------------------------------------------------
        public async Task<Dictionary<string, object>> CatalogAsync(
            int page, int limit, string q, bool adult, string genre)
        {
            page = Math.Max(1, page);
            var baseLimit = 28;

            string url;
            var search = !string.IsNullOrWhiteSpace(q);
            if (search)
            {
                url = Site + "/?s=" + Uri.EscapeDataString(q.Trim());
            }
            else if (!string.IsNullOrWhiteSpace(genre))
            {
                var slug = GenreSlugs.TryGetValue(genre.Trim(), out var mapped) ? mapped : Slugify(genre);
                url = Site + "/genero/" + slug + "/" + (page > 1 ? "page/" + page + "/" : string.Empty);
            }
            else
            {
                url = Site + "/manga/" + (page > 1 ? "page/" + page + "/" : string.Empty);
            }

            string html;
            try
            {
                html = await GetStringAsync(url, Site + "/manga/").ConfigureAwait(false);
            }
            catch (Exception e)
            {
                if (search) throw;
                if (page <= 1) throw;
                return new Dictionary<string, object>
                {
                    ["items"] = new List<Dictionary<string, object>>(),
                    ["page"] = page,
                    ["hasMore"] = false,
                    ["total"] = 0,
                };
            }

            var items = ParseCards(html);

            try
            {
                var meta = await MetaBySlugsAsync(items.Select(i => Convert.ToString(i["id"], CultureInfo.InvariantCulture))
                    .ToList()).ConfigureAwait(false);
                foreach (var it in items)
                {
                    var slug = Convert.ToString(it["id"], CultureInfo.InvariantCulture);
                    if (meta.TryGetValue(slug, out var m)) ApplyMeta(it, m);
                }
            }
            catch { }

            if (!adult) items = items.Where(i => !(bool)i["adult"]).ToList();
            if (items.Count > limit) items = items.Take(limit).ToList();

            var hasMore = !search && ParseCards(html).Count >= 20;
            return new Dictionary<string, object>
            {
                ["items"] = items,
                ["page"] = page,
                ["hasMore"] = hasMore,
                ["total"] = 0,
            };
        }

        private static string Slugify(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return string.Empty;
            var norm = s.Trim().ToLowerInvariant()
                .Replace('á', 'a').Replace('à', 'a').Replace('â', 'a').Replace('ã', 'a')
                .Replace('é', 'e').Replace('ê', 'e').Replace('í', 'i')
                .Replace('ó', 'o').Replace('ô', 'o').Replace('õ', 'o').Replace('ú', 'u')
                .Replace('ç', 'c');
            norm = Regex.Replace(norm, "[^a-z0-9]+", "-").Trim('-');
            return norm;
        }

        public async Task<Dictionary<string, object>> MangaAsync(string id, bool adult)
        {
            var slug = (id ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(slug)) throw new Exception("Obra invalida.");

            var key = "m:" + slug;
            lock (_gate)
            {
                if (_cache.TryGetValue(key, out var hit) && DateTime.UtcNow - hit.Item1 < TimeSpan.FromMinutes(10))
                {
                    var cached = (Dictionary<string, object>)hit.Item2;
                    if ((bool)cached["adult"] && !adult)
                        throw new Exception("Esta obra e +18. Ative o catalogo +18 para abrir.");
                    return cached;
                }
            }

            var raw = await GetStringAsync(Rest + "/mbi_manga?slug=" + Uri.EscapeDataString(slug),
                Site + "/manga/", "application/json").ConfigureAwait(false);
            JsonElement root;
            using (var doc = JsonDocument.Parse(raw)) root = doc.RootElement.Clone();
            if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
                throw new Exception("Obra nao encontrada no Ler Mangá Online.");

            var m = root[0];
            var classes = ClassesOf(m);
            var link = m.TryGetProperty("link", out var lv) && lv.ValueKind == JsonValueKind.String
                ? lv.GetString()
                : Site + "/manga/" + slug + "/";

            var title = string.Empty;
            if (m.TryGetProperty("title", out var tv) && tv.ValueKind == JsonValueKind.Object &&
                tv.TryGetProperty("rendered", out var tr) && tr.ValueKind == JsonValueKind.String)
                title = CleanTitle(tr.GetString());

            var desc = string.Empty;
            if (m.TryGetProperty("content", out var cv) && cv.ValueKind == JsonValueKind.Object &&
                cv.TryGetProperty("rendered", out var cr) && cr.ValueKind == JsonValueKind.String)
                desc = Html(cr.GetString());
            if (string.IsNullOrWhiteSpace(desc) &&
                m.TryGetProperty("excerpt", out var ev) && ev.ValueKind == JsonValueKind.Object &&
                ev.TryGetProperty("rendered", out var er) && er.ValueKind == JsonValueKind.String)
                desc = Html(er.GetString());

            var item = new Dictionary<string, object>
            {
                ["id"] = slug,
                ["title"] = string.IsNullOrWhiteSpace(title) ? DeSlug(slug) : title,
                ["title_alt"] = string.Empty,
                ["cover"] = string.Empty,
                ["description"] = desc,
                ["adult"] = IsAdultClasses(classes),
                ["chapters"] = 0,
                ["status"] = StatusFromClasses(classes),
                ["genres"] = GenresFromClasses(classes),
                ["source"] = "lermanga",
                ["type"] = TypeFromClasses(classes),
                ["url"] = link,
            };

            var pageHtml = string.Empty;
            try { pageHtml = await GetStringAsync(link, Site + "/manga/").ConfigureAwait(false); }
            catch { }

            if (!string.IsNullOrEmpty(pageHtml))
            {
                var og = Regex.Match(pageHtml,
                    "<meta\\s+property=\"og:image\"\\s+content=\"([^\"]+)\"", RegexOptions.IgnoreCase);
                if (og.Success) item["cover"] = WebUtility.HtmlDecode(og.Groups[1].Value);

                var total = Regex.Match(pageHtml, "data-manga-id=\"(\\d+)\"\\s+data-total=\"(\\d+)\"");
                if (total.Success)
                {
                    item["_wpId"] = total.Groups[1].Value;
                    if (int.TryParse(total.Groups[2].Value, out var tc)) item["chapters"] = tc;
                }

                if (!(bool)item["adult"] &&
                    Regex.IsMatch(pageHtml, "\\+\\s*18|Adulto", RegexOptions.IgnoreCase) &&
                    Regex.IsMatch(pageHtml, "genero/(?:18|adulto|hentai)", RegexOptions.IgnoreCase))
                    item["adult"] = true;
            }

            if ((bool)item["adult"] && !adult)
                throw new Exception("Esta obra e +18. Ative o catalogo +18 para abrir.");

            lock (_gate) _cache[key] = Tuple.Create(DateTime.UtcNow, (object)item);
            return item;
        }

        private async Task<Tuple<long, int, string>> MangaInfoAsync(string slug)
        {
            var m = await GetStringAsync(Rest + "/mbi_manga?slug=" + Uri.EscapeDataString(slug) +
                                          "&_fields=id,link,meta", Site + "/manga/", "application/json")
                .ConfigureAwait(false);
            JsonElement root;
            using (var doc = JsonDocument.Parse(m)) root = doc.RootElement.Clone();
            if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
                throw new Exception("Obra nao encontrada no Ler Mangá Online.");

            var item = root[0];
            long wpId = 0;
            if (item.TryGetProperty("id", out var iv) && iv.ValueKind == JsonValueKind.Number)
                iv.TryGetInt64(out wpId);
            var link = item.TryGetProperty("link", out var lv) && lv.ValueKind == JsonValueKind.String
                ? lv.GetString()
                : Site + "/manga/" + slug + "/";

            var total = 0;
            if (item.TryGetProperty("meta", out var mv) && mv.ValueKind == JsonValueKind.Object &&
                mv.TryGetProperty("_mbi_expected_chapters", out var ec) && ec.ValueKind == JsonValueKind.Number)
                ec.TryGetInt32(out total);

            return Tuple.Create(wpId, total, link);
        }

        public async Task<List<Dictionary<string, object>>> ChaptersAsync(string id)
        {
            var slug = (id ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(slug)) throw new Exception("Obra invalida.");

            var key = "c:" + slug;
            lock (_gate)
            {
                if (_cache.TryGetValue(key, out var hit) && DateTime.UtcNow - hit.Item1 < TimeSpan.FromMinutes(8))
                    return (List<Dictionary<string, object>>)hit.Item2;
            }

            var info = await MangaInfoAsync(slug).ConfigureAwait(false);
            var wpId = info.Item1;
            var total = info.Item2;
            var link = info.Item3;
            if (wpId <= 0) throw new Exception("Obra invalida no Ler Mangá Online.");

            var pageHtml = await GetStringAsync(link, Site + "/manga/").ConfigureAwait(false);
            var pageTotal = Regex.Match(pageHtml, "data-total=\"(\\d+)\"");
            if (pageTotal.Success && int.TryParse(pageTotal.Groups[1].Value, out var pt) && pt > 0)
                total = pt;

            var found = new Dictionary<string, Dictionary<string, object>>(StringComparer.Ordinal);

            void AddBlock(string html)
            {
                foreach (Match m in ListItemRe.Matches(html ?? string.Empty))
                {
                    var numberText = m.Groups[1].Value;
                    var href = WebUtility.HtmlDecode(m.Groups[2].Value);
                    var inner = m.Groups[3].Value;

                    var chapterSlug = SlugFromUrl(href);
                    if (string.IsNullOrWhiteSpace(chapterSlug) || !found.ContainsKey(chapterSlug))
                    {
                        var strong = StrongRe.Match(inner);
                        var title = CleanTitle(strong.Success ? strong.Groups[1].Value : Html(inner));
                        if (string.IsNullOrWhiteSpace(title)) title = "Capítulo";
                        double num = 0;
                        double.TryParse(numberText, NumberStyles.Float, CultureInfo.InvariantCulture, out num);
                        var entry = new Dictionary<string, object>
                        {
                            ["id"] = chapterSlug,
                            ["title"] = title,
                            ["number"] = num,
                            ["date"] = string.Empty,
                            ["locked"] = false,
                        };
                        if (!string.IsNullOrWhiteSpace(chapterSlug)) found[chapterSlug] = entry;
                    }
                }
            }

            // primeira pagina ja vem no HTML da obra
            var ol = Regex.Match(pageHtml,
                "<ol[^>]*data-chapter-list[^>]*>([\\s\\S]*?)</ol>", RegexOptions.IgnoreCase);
            if (ol.Success) AddBlock(ol.Groups[1].Value);

            var perPage = 25;
            var totalPages = Math.Max(1, (int)Math.Ceiling(Math.Max(total, found.Count) / (double)perPage));
            for (var p = 2; p <= totalPages; p++)
            {
                var url = Site + "/wp-admin/admin-post.php?action=mlo_chapter_batch&manga_id=" +
                          wpId.ToString(CultureInfo.InvariantCulture) + "&page=" + p.ToString(CultureInfo.InvariantCulture);
                string body;
                try { body = await GetStringAsync(url, link, "application/json").ConfigureAwait(false); }
                catch { break; }

                try
                {
                    using var doc = JsonDocument.Parse(body);
                    var root = doc.RootElement;
                    if (!root.TryGetProperty("success", out var sv) || sv.ValueKind != JsonValueKind.True) break;
                    if (!root.TryGetProperty("data", out var data)) break;
                    if (!data.TryGetProperty("html", out var hv) || hv.ValueKind != JsonValueKind.String) break;
                    var before = found.Count;
                    AddBlock(hv.GetString());
                    if (found.Count == before) break;
                }
                catch { break; }
            }

            var list = found.Values.ToList();
            list.Sort((a, b) =>
            {
                var na = Convert.ToDouble(a["number"], CultureInfo.InvariantCulture);
                var nb = Convert.ToDouble(b["number"], CultureInfo.InvariantCulture);
                var cmp = na.CompareTo(nb);
                if (cmp != 0) return cmp;
                return string.Compare(Convert.ToString(a["title"], CultureInfo.InvariantCulture),
                    Convert.ToString(b["title"], CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);
            });

            lock (_gate) _cache[key] = Tuple.Create(DateTime.UtcNow, (object)list);
            return list;
        }

        public async Task<Dictionary<string, object>> PagesAsync(string token, bool adult)
        {
            var slug = (token ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(slug)) throw new Exception("Capitulo invalido.");

            var chapterLink = Site + "/capitulo/" + slug + "/";
            var restImages = new List<string>();

            // 1) REST do tema: link canonical do capitulo + lista de paginas (proxy).
            try
            {
                var raw = await GetStringAsync(Rest + "/mbi_chapter?slug=" + Uri.EscapeDataString(slug),
                    Site + "/", "application/json").ConfigureAwait(false);
                using var doc = JsonDocument.Parse(raw);
                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
                {
                    var c = root[0];
                    if (c.TryGetProperty("link", out var lv) && lv.ValueKind == JsonValueKind.String &&
                        !string.IsNullOrWhiteSpace(lv.GetString()))
                        chapterLink = lv.GetString();

                    if (c.TryGetProperty("meta", out var meta) && meta.ValueKind == JsonValueKind.Object &&
                        meta.TryGetProperty("_mbi_chapter_page_urls", out var arr) && arr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var x in arr.EnumerateArray())
                        {
                            if (x.ValueKind != JsonValueKind.String) continue;
                            var u = x.GetString();
                            if (!string.IsNullOrWhiteSpace(u)) restImages.Add(u.Trim());
                        }
                    }
                }
            }
            catch
            {
                // sem REST ainda da para tentar o HTML do capitulo
            }

            // 2) HTML do capitulo: o bloco JSON `mlo-reader-batch-data` traz a lista
            // completa do capitulo (o HTML so renderiza 5 <figure> por lote, com o
            // botao "Carregar proximas 5 paginas"). `originals` guarda as URLs R2,
            // que funcionam direto; `pages` guarda os proxies bloqueados (403/502).
            var images = new List<string>();
            try
            {
                var html = await GetStringAsync(chapterLink, Site + "/manga/").ConfigureAwait(false);

                var batch = Regex.Match(html,
                    "<script[^>]*id=\"mlo-reader-batch-data\"[^>]*>([\\s\\S]*?)</script>",
                    RegexOptions.IgnoreCase);
                if (batch.Success)
                {
                    try
                    {
                        var json = WebUtility.HtmlDecode(batch.Groups[1].Value).Trim();
                        using var batchDoc = JsonDocument.Parse(json);
                        var originals = new List<string>();
                        var proxies = new List<string>();

                        if (batchDoc.RootElement.TryGetProperty("originals", out var ov) &&
                            ov.ValueKind == JsonValueKind.Array)
                            foreach (var x in ov.EnumerateArray())
                                if (x.ValueKind == JsonValueKind.String)
                                    originals.Add((x.GetString() ?? string.Empty).Trim());

                        if (batchDoc.RootElement.TryGetProperty("pages", out var pv) &&
                            pv.ValueKind == JsonValueKind.Array)
                            foreach (var x in pv.EnumerateArray())
                                if (x.ValueKind == JsonValueKind.String)
                                    proxies.Add((x.GetString() ?? string.Empty).Trim());

                        var total = Math.Max(originals.Count, proxies.Count);
                        for (var i = 0; i < total; i++)
                        {
                            // Ordem de confianca por pagina: R2 original -> REST do tema
                            // -> proxy do site. Espelhos em mangalivre.to estao mortos.
                            var pick = i < originals.Count ? originals[i] : string.Empty;
                            if (pick.IndexOf("mangalivre.to", StringComparison.OrdinalIgnoreCase) >= 0)
                                pick = string.Empty;
                            if (pick.Length == 0 && i < restImages.Count)
                            {
                                pick = restImages[i];
                                if (pick.IndexOf("mangalivre.to", StringComparison.OrdinalIgnoreCase) >= 0)
                                    pick = string.Empty;
                            }
                            if (pick.Length == 0 && i < proxies.Count) pick = proxies[i];
                            if (pick.Length > 0 && !images.Contains(pick)) images.Add(pick);
                        }
                    }
                    catch
                    {
                        // JSON invalido: cai para os atributos das figuras
                    }
                }

                // 2b) Fallback: data-mlo-page-urls das figuras ja renderizadas.
                if (images.Count == 0)
                foreach (Match m in Regex.Matches(html, "data-mlo-page-urls=\"([^\"]+)\"", RegexOptions.IgnoreCase))
                {
                    var json = WebUtility.HtmlDecode(m.Groups[1].Value);
                    try
                    {
                        using var arrDoc = JsonDocument.Parse(json);
                        if (arrDoc.RootElement.ValueKind != JsonValueKind.Array) continue;
                        var candidates = new List<string>();
                        foreach (var x in arrDoc.RootElement.EnumerateArray())
                        {
                            if (x.ValueKind != JsonValueKind.String) continue;
                            var u = x.GetString();
                            if (!string.IsNullOrWhiteSpace(u)) candidates.Add(u.Trim());
                        }
                        if (candidates.Count == 0) continue;

                        // prefere um espelho que nao seja o dominio morto mangalivre.to
                        var pick = candidates.FirstOrDefault(u => !u.Contains("mangalivre.to", StringComparison.OrdinalIgnoreCase))
                                   ?? candidates[0];
                        if (!images.Contains(pick)) images.Add(pick);
                    }
                    catch
                    {
                        // atributo invalido: ignora esta pagina
                    }
                }

                // 3) Ultimo recurso: imgs do proprio leitor (proxies). So usa se nao houver originais.
                if (images.Count == 0)
                {
                    var mi = Regex.Match(html, "id=\"mlo-reader-pages\"[\\s\\S]*?</div>", RegexOptions.IgnoreCase);
                    var scope = mi.Success ? mi.Value : html;
                    foreach (Match im in Regex.Matches(scope, "<img[^>]+(?:data-src|src)=\"([^\"]+)\"",
                                 RegexOptions.IgnoreCase))
                    {
                        var u = WebUtility.HtmlDecode(im.Groups[1].Value);
                        if (u.Contains("img.lermangaonline.com.br") && !images.Contains(u)) images.Add(u);
                    }
                }
            }
            catch
            {
                // HTML fora do ar: cai para a lista do REST
            }

            if (images.Count == 0 && restImages.Count > 0) images = restImages;
            if (images.Count == 0)
                throw new Exception("Capitulo sem paginas disponiveis (a origem deste capitulo pode estar fora do ar).");

            return new Dictionary<string, object> { ["images"] = images, ["text"] = string.Empty };
        }

        public List<string> Genres() => UiGenres.ToList();
    }
}
