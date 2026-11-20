using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SoloMangas.Shared;

namespace SoloMangasNative
{
    /// <summary>MangaDex (REST publica). Catalogo apenas em pt-br.</summary>
    public sealed class MangaDexSource : ISource
    {
        private const string Api = "https://api.mangadex.org";
        private const string Uploads = "https://uploads.mangadex.org";

        private static readonly Dictionary<string, string> GenreTags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Ação"] = "Action", ["Aventura"] = "Adventure", ["Comédia"] = "Comedy", ["Drama"] = "Drama",
            ["Fantasia"] = "Fantasy", ["Romance"] = "Romance", ["Slice of Life"] = "Slice of Life",
            ["Terror"] = "Horror", ["Suspense"] = "Thriller", ["Mistério"] = "Mystery",
            ["Ficção Científica"] = "Sci-Fi", ["Artes Marciais"] = "Martial Arts", ["Escolar"] = "School Life",
            ["Histórico"] = "Historical", ["Psicológico"] = "Psychological", ["Sobrenatural"] = "Supernatural",
            ["Reencarnação"] = "Reincarnation", ["Regressão"] = "Time Travel", ["Isekai"] = "Isekai",
            ["Mecha"] = "Mecha", ["Esporte"] = "Sports", ["Culinária"] = "Cooking", ["Música"] = "Music",
            ["Wuxia"] = "Wuxia", ["Tragédia"] = "Tragedy", ["Policial"] = "Police", ["Militar"] = "Military",
            ["Vampiro"] = "Vampires", ["Zumbi"] = "Zombies", ["Super-herói"] = "Superhero", ["Crime"] = "Crime",
            ["BL"] = "Boys' Love", ["GL"] = "Girls' Love", ["Yaoi"] = "Boys' Love", ["Yuri"] = "Girls' Love",
            ["Shounen Ai"] = "Boys' Love", ["Shoujo Ai"] = "Girls' Love",
        };

        private static readonly Dictionary<string, string> TagPt = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Action"] = "Ação", ["Adventure"] = "Aventura", ["Comedy"] = "Comédia", ["Drama"] = "Drama",
            ["Fantasy"] = "Fantasia", ["Romance"] = "Romance", ["Slice of Life"] = "Slice of Life",
            ["Horror"] = "Terror", ["Thriller"] = "Suspense", ["Mystery"] = "Mistério", ["Sci-Fi"] = "Ficção Científica",
            ["Martial Arts"] = "Artes Marciais", ["School Life"] = "Escolar", ["Historical"] = "Histórico",
            ["Psychological"] = "Psicológico", ["Supernatural"] = "Sobrenatural", ["Reincarnation"] = "Reencarnação",
            ["Time Travel"] = "Regressão", ["Isekai"] = "Isekai", ["Mecha"] = "Mecha", ["Sports"] = "Esporte",
            ["Cooking"] = "Culinária", ["Music"] = "Música", ["Boys' Love"] = "BL", ["Girls' Love"] = "GL",
            ["Wuxia"] = "Wuxia", ["Tragedy"] = "Tragédia", ["Police"] = "Policial", ["Military"] = "Militar",
            ["Vampires"] = "Vampiro", ["Zombies"] = "Zumbi", ["Superhero"] = "Super-herói", ["Crime"] = "Crime",
            ["Demons"] = "Demônios", ["Monsters"] = "Monstros", ["Ghosts"] = "Fantasmas", ["Magic"] = "Magia",
            ["Harem"] = "Harem", ["Reverse Harem"] = "Harem Reverso", ["Gore"] = "Gore", ["Villainess"] = "Vilã",
            ["Delinquents"] = "Delinquentes", ["Office Workers"] = "Trabalho", ["Video Games"] = "Jogos",
            ["Post-Apocalyptic"] = "Pós-apocalíptico", ["Survival"] = "Sobrevivência", ["Animals"] = "Animais",
            ["Monster Girls"] = "Garotas Monstro", ["Gyaru"] = "Gyaru", ["Crossdressing"] = "Crossdressing",
            ["Mafia"] = "Máfia", ["Ninja"] = "Ninja", ["Samurai"] = "Samurai", ["Aliens"] = "Alienígenas",
            ["Medical"] = "Médico", ["Philosophical"] = "Filosófico", ["Award Winning"] = "Premiado",
            ["Long Strip"] = "Tira Longa", ["Full Color"] = "Colorido", ["4-Koma"] = "4-Koma",
            ["Adaptation"] = "Adaptação", ["Hentai"] = "Hentai", ["Ecchi"] = "Ecchi",
        };

        public static readonly string[] UiGenres =
        {
            "Ação", "Aventura", "Comédia", "Drama", "Fantasia", "Romance", "Slice of Life", "Terror",
            "Suspense", "Mistério", "Ficção Científica", "Artes Marciais", "Escolar", "Histórico",
            "Psicológico", "Sobrenatural", "Reencarnação", "Regressão", "Isekai", "Mecha", "Esporte",
            "Culinária", "Música", "Wuxia", "Tragédia", "Policial", "Militar", "Vampiro", "Zumbi",
            "Super-herói", "Crime", "BL", "GL", "Yaoi", "Yuri",
        };

        private Dictionary<string, string> _tagCache;
        private readonly object _tagGate = new object();
        private readonly string _key;
        private readonly string _name;
        private readonly bool _adultOnly;

        public MangaDexSource() : this("mangadex", "MangaDex", false)
        {
        }

        /// <summary>Variante +18: mesma API, mas so devolve obras pornograficas/eroticas em pt-br.</summary>
        public MangaDexSource(string key, string name, bool adultOnly)
        {
            _key = key;
            _name = name;
            _adultOnly = adultOnly;
        }

        public string Key => _key;
        public string Name => _name;
        public bool SupportsLogin => false;
        public bool AdultOnly => _adultOnly;

        // ------------------------------------------------------------------
        // HTTP / JSON
        // ------------------------------------------------------------------
        // A MangaDex limita requisicoes por IP e responde 429 quando o limite e
        // passado. Espacamos as chamadas e repetimos usando o Retry-After do
        // servidor, em vez de mostrar erro na primeira falha.
        private static readonly SemaphoreSlim TrafficGate = new SemaphoreSlim(1, 1);
        private static DateTime _lastCall = DateTime.MinValue;
        private const int MinIntervalMs = 230;

        private static async Task SpaceAsync()
        {
            await TrafficGate.WaitAsync().ConfigureAwait(false);
            try
            {
                var since = (DateTime.UtcNow - _lastCall).TotalMilliseconds;
                var wait = MinIntervalMs - since;
                if (wait > 1) await Task.Delay((int)Math.Min(MinIntervalMs, wait)).ConfigureAwait(false);
                _lastCall = DateTime.UtcNow;
            }
            finally
            {
                TrafficGate.Release();
            }
        }

        private static int RetryAfterMs(MdResponse res)
        {
            if (res == null || !res.Headers.TryGetValue("retry-after", out var raw) || string.IsNullOrWhiteSpace(raw))
                return 0;
            raw = raw.Trim();
            if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
                return (int)Math.Min(15000, Math.Max(0, seconds * 1000));
            if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var when))
                return (int)Math.Min(15000, Math.Max(0, (when - DateTimeOffset.UtcNow).TotalMilliseconds));
            return 0;
        }

        private static async Task<JsonElement> GetAsync(string path)
        {
            // A MangaDex exige TLS 1.3; o SChannel do Windows nao oferece como cliente,
            // entao usamos o cliente BouncyCastle (MangaDexHttp).
            Exception last = null;
            for (var attempt = 0; attempt < 3; attempt++)
            {
                await SpaceAsync().ConfigureAwait(false);

                MdResponse res = null;
                try
                {
                    res = await MangaDexHttp.GetAsync(Api + path, "application/json", 30000).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    last = new Exception("MangaDex: falha de rede (" + ex.Message + ").");
                }

                if (res != null)
                {
                    if (res.Status >= 200 && res.Status < 300)
                    {
                        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(res.Text) ? "{}" : res.Text);
                        return doc.RootElement.Clone();
                    }

                    if (res.Status == 429)
                    {
                        var wait = RetryAfterMs(res);
                        last = new Exception("A MangaDex limitou as requisicoes (429).");
                        if (attempt < 2)
                        {
                            await Task.Delay(wait > 0 ? wait : 1200 * (attempt + 1)).ConfigureAwait(false);
                            continue;
                        }
                        throw new Exception("A MangaDex limitou as requisicoes. Aguarde alguns segundos e tente de novo.");
                    }

                    if (res.Status == 0)
                    {
                        last = new Exception("Nao foi possivel conectar a MangaDex (TLS/rede).");
                    }
                    else if (res.Status >= 500)
                    {
                        last = new Exception("A MangaDex esta instavel (HTTP " + res.Status + ").");
                    }
                    else
                    {
                        var detail = res.Text ?? string.Empty;
                        if (detail.Length > 120) detail = detail.Substring(0, 120);
                        throw new Exception("MangaDex respondeu " + res.Status + (detail.Length > 0 ? " - " + detail : string.Empty));
                    }
                }

                if (attempt < 2) await Task.Delay(700 * (attempt + 1)).ConfigureAwait(false);
            }
            throw last ?? new Exception("MangaDex: sem resposta.");
        }

        private async Task<Dictionary<string, string>> TagsAsync()
        {
            lock (_tagGate) { if (_tagCache != null) return _tagCache; }
            var j = await GetAsync("/manga/tag").ConfigureAwait(false);
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (j.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            {
                foreach (var t in data.EnumerateArray())
                {
                    var id = t.TryGetProperty("id", out var idv) ? idv.GetString() : null;
                    if (string.IsNullOrEmpty(id)) continue;
                    if (t.TryGetProperty("attributes", out var a) && a.TryGetProperty("name", out var n) &&
                        n.TryGetProperty("en", out var en) && en.ValueKind == JsonValueKind.String)
                    {
                        var name = en.GetString();
                        if (!string.IsNullOrWhiteSpace(name) && !map.ContainsKey(name)) map[name] = id;
                    }
                }
            }
            lock (_tagGate) _tagCache = map;
            return map;
        }

        // ------------------------------------------------------------------
        // Normalizacao
        // ------------------------------------------------------------------
        private static string Pick(JsonElement map, params string[] order)
        {
            if (map.ValueKind != JsonValueKind.Object) return string.Empty;
            foreach (var k in order)
            {
                if (map.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String)
                {
                    var s = v.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) return s.Trim();
                }
            }
            foreach (var p in map.EnumerateObject())
                if (p.Value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(p.Value.GetString()))
                    return p.Value.GetString().Trim();
            return string.Empty;
        }

        private static string Clean(string html)
        {
            if (string.IsNullOrEmpty(html)) return string.Empty;
            var t = Regex.Replace(html, "<br\\s*/?>", "\n", RegexOptions.IgnoreCase);
            t = Regex.Replace(t, "<[^>]+>", " ");
            t = WebUtility.HtmlDecode(t);
            t = Regex.Replace(t, "[ \\t]+", " ");
            t = Regex.Replace(t, "\\n{3,}", "\n\n");
            return t.Trim();
        }

        private Manga Normalize(JsonElement m)
        {
            var id = m.TryGetProperty("id", out var idv) ? idv.GetString() : null;
            if (string.IsNullOrEmpty(id)) return null;
            var a = m.TryGetProperty("attributes", out var av) ? av : default;

            var titleMap = a.ValueKind == JsonValueKind.Object && a.TryGetProperty("title", out var tm) ? tm : default;
            var altMap = new Dictionary<string, string>(StringComparer.Ordinal);
            if (a.ValueKind == JsonValueKind.Object && a.TryGetProperty("altTitles", out var alts) &&
                alts.ValueKind == JsonValueKind.Array)
            {
                foreach (var o in alts.EnumerateArray())
                {
                    if (o.ValueKind != JsonValueKind.Object) continue;
                    foreach (var p in o.EnumerateObject())
                    {
                        if (!altMap.ContainsKey(p.Name) && p.Value.ValueKind == JsonValueKind.String)
                        {
                            var s = p.Value.GetString();
                            if (!string.IsNullOrWhiteSpace(s)) altMap[p.Name] = s.Trim();
                        }
                    }
                }
            }

            var title = Pick(titleMap, "pt-br", "pt", "en", "ja-ro", "ko-ro", "zh-ro", "ja", "ko");
            if (string.IsNullOrWhiteSpace(title))
                title = PickMap(altMap, "pt-br", "pt", "en", "ja-ro", "ko-ro");
            var altEn = PickMap(altMap, "en");
            if (string.IsNullOrEmpty(altEn) && titleMap.ValueKind == JsonValueKind.Object &&
                titleMap.TryGetProperty("en", out var ten) && ten.ValueKind == JsonValueKind.String)
                altEn = ten.GetString();
            var desc = Clean(Pick(a.ValueKind == JsonValueKind.Object && a.TryGetProperty("description", out var dv) ? dv : default,
                "pt-br", "pt", "en"));

            var genres = new List<string>();
            if (a.ValueKind == JsonValueKind.Object && a.TryGetProperty("tags", out var tags) && tags.ValueKind == JsonValueKind.Array)
            {
                foreach (var t in tags.EnumerateArray())
                {
                    if (t.ValueKind != JsonValueKind.Object) continue;
                    if (!t.TryGetProperty("attributes", out var ta)) continue;
                    if (!ta.TryGetProperty("name", out var tn) || !tn.TryGetProperty("en", out var en) ||
                        en.ValueKind != JsonValueKind.String) continue;
                    var nameEn = en.GetString();
                    if (string.IsNullOrWhiteSpace(nameEn)) continue;
                    var pt = TagPt.TryGetValue(nameEn, out var mapped) ? mapped : nameEn;
                    if (!genres.Contains(pt)) genres.Add(pt);
                }
            }

            var cover = string.Empty;
            if (m.TryGetProperty("relationships", out var rels) && rels.ValueKind == JsonValueKind.Array)
            {
                foreach (var rel in rels.EnumerateArray())
                {
                    if (rel.ValueKind != JsonValueKind.Object) continue;
                    if (!rel.TryGetProperty("type", out var rt) || rt.GetString() != "cover_art") continue;
                    if (rel.TryGetProperty("attributes", out var ra) && ra.TryGetProperty("fileName", out var fn) &&
                        fn.ValueKind == JsonValueKind.String)
                    {
                        cover = Uploads + "/covers/" + id + "/" + fn.GetString() + ".512.jpg";
                    }
                    break;
                }
            }

            var status = a.ValueKind == JsonValueKind.Object && a.TryGetProperty("status", out var st)
                ? st.GetString() : string.Empty;
            var statusPt = status switch
            {
                "ongoing" => "Em andamento",
                "completed" => "Completo",
                "hiatus" => "Em hiato",
                "cancelled" => "Cancelado",
                _ => status ?? string.Empty,
            };

            var rating = a.ValueKind == JsonValueKind.Object && a.TryGetProperty("contentRating", out var cr)
                ? cr.GetString() : string.Empty;
            var year = 0;
            if (a.ValueKind == JsonValueKind.Object && a.TryGetProperty("year", out var yv) &&
                yv.ValueKind == JsonValueKind.Number) yv.TryGetInt32(out year);

            return new Manga
            {
                Source = _key,
                Id = id,
                Title = string.IsNullOrWhiteSpace(title) ? "Sem titulo" : title,
                TitleAlt = altEn != null && !string.Equals(altEn, title, StringComparison.OrdinalIgnoreCase) ? altEn : string.Empty,
                Cover = cover,
                Description = desc,
                Adult = _adultOnly || rating == "erotica" || rating == "pornographic",
                Status = statusPt,
                Genres = genres,
                Year = year,
                Url = "https://mangadex.org/title/" + id,
            };
        }

        private static string PickMap(Dictionary<string, string> map, params string[] order)
        {
            foreach (var k in order)
                if (map.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v)) return v;
            return map.Values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? string.Empty;
        }

        // ------------------------------------------------------------------
        // Endpoints
        // ------------------------------------------------------------------
        private static string Q(string key, string value) => "&" + Uri.EscapeDataString(key) + "=" + Uri.EscapeDataString(value);

        /// <summary>Catalogo somente em pt-br. Nao cai para ingles.</summary>
        public async Task<CatalogResult> CatalogAsync(SourceQuery query)
        {
            return await CatalogPageAsync(query, "pt-br").ConfigureAwait(false);
        }

        private async Task<CatalogResult> CatalogPageAsync(SourceQuery query, string lang)
        {
            var page = new CatalogResult { Page = Math.Max(1, query.Page) };
            var limit = Math.Max(1, Math.Min(60, query.Limit));
            var offset = (page.Page - 1) * limit;

            var qs = "?limit=" + limit.ToString(CultureInfo.InvariantCulture) +
                     "&offset=" + offset.ToString(CultureInfo.InvariantCulture) +
                     Q("availableTranslatedLanguage[]", lang) +
                     Q("includes[]", "cover_art") +
                     Q("order[followedCount]", "desc");
            if (_adultOnly)
            {
                qs += Q("contentRating[]", "pornographic") + Q("contentRating[]", "erotica");
            }
            else
            {
                qs += Q("contentRating[]", "safe") + Q("contentRating[]", "suggestive");
                if (query.Adult)
                    qs += Q("contentRating[]", "erotica") + Q("contentRating[]", "pornographic");
            }
            if (!string.IsNullOrWhiteSpace(query.Q)) qs += Q("title", query.Q.Trim());
            if (!string.IsNullOrWhiteSpace(query.Genre))
            {
                if (query.Genre == "Adulto" || query.Genre == "+18")
                {
                    if (!query.Adult) return page;
                    qs += Q("contentRating[]", "erotica") + Q("contentRating[]", "pornographic");
                }
                else
                {
                    try
                    {
                        var tags = await TagsAsync().ConfigureAwait(false);
                        var en = GenreTags.TryGetValue(query.Genre, out var mapped) ? mapped : query.Genre;
                        if (tags.TryGetValue(en, out var tagId)) qs += Q("includedTags[]", tagId);
                    }
                    catch { }
                }
            }

            var j = await GetAsync("/manga" + qs).ConfigureAwait(false);
            if (j.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            {
                foreach (var e in data.EnumerateArray())
                {
                    var m = Normalize(e);
                    if (m == null) continue;
                    if (!query.Adult && m.Adult) continue;
                    page.Items.Add(m);
                }
            }
            long total = 0;
            if (j.TryGetProperty("total", out var tv) && tv.ValueKind == JsonValueKind.Number) tv.TryGetInt64(out total);
            page.Total = total;
            page.HasMore = page.Page * limit < total && page.Items.Count > 0;
            return page;
        }

        public async Task<Manga> MangaAsync(string id, bool adult)
        {
            var j = await GetAsync("/manga/" + Uri.EscapeDataString(id) +
                                   "?includes[]=cover_art&includes[]=author&includes[]=artist").ConfigureAwait(false);
            if (!j.TryGetProperty("data", out var d)) throw new Exception("Obra nao encontrada na MangaDex.");
            var m = Normalize(d);
            if (m == null) throw new Exception("Obra nao encontrada na MangaDex.");
            if (m.Adult && !adult) throw new Exception("Esta obra e +18. Ative o catalogo +18 para abrir.");
            return m;
        }

        private async Task<List<Chapter>> FeedAsync(string id, string lang)
        {
            var found = new Dictionary<string, Chapter>(StringComparer.Ordinal);
            var offset = 0;
            const int size = 500;
            while (true)
            {
                var qs = "?translatedLanguage%5B%5D=" + lang + "&order%5Bchapter%5D=asc&limit=" + size +
                         "&offset=" + offset + "&includeExternalUrl=0&includeFutureUpdates=0";
                var j = await GetAsync("/manga/" + Uri.EscapeDataString(id) + "/feed" + qs).ConfigureAwait(false);
                if (!j.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) break;
                var count = data.GetArrayLength();
                foreach (var c in data.EnumerateArray())
                {
                    if (!c.TryGetProperty("attributes", out var a)) continue;
                    if (a.TryGetProperty("externalUrl", out var ext) && ext.ValueKind == JsonValueKind.String &&
                        !string.IsNullOrWhiteSpace(ext.GetString())) continue;
                    if (!a.TryGetProperty("pages", out var pg) || pg.ValueKind != JsonValueKind.Number ||
                        pg.GetInt32() <= 0) continue;
                    var chapter = a.TryGetProperty("chapter", out var cv) && cv.ValueKind != JsonValueKind.Null
                        ? Convert.ToString(cv.ValueKind == JsonValueKind.String ? cv.GetString() : cv.ToString(), CultureInfo.InvariantCulture)
                        : string.Empty;
                    chapter = chapter?.Trim() ?? string.Empty;
                    var volume = a.TryGetProperty("volume", out var vv) && vv.ValueKind != JsonValueKind.Null
                        ? Convert.ToString(vv.ValueKind == JsonValueKind.String ? vv.GetString() : vv.ToString(), CultureInfo.InvariantCulture)
                        : string.Empty;
                    var ctitle = a.TryGetProperty("title", out var tt) && tt.ValueKind == JsonValueKind.String
                        ? tt.GetString().Trim() : string.Empty;
                    var label = !string.IsNullOrEmpty(chapter)
                        ? "Capítulo " + chapter + (ctitle.Length > 0 && !Regex.IsMatch(ctitle, "^chapter", RegexOptions.IgnoreCase)
                            ? " — " + ctitle : string.Empty)
                        : (ctitle.Length > 0 ? ctitle : "Capítulo");
                    var key = (volume ?? string.Empty) + "|" + chapter;
                    var cid = c.TryGetProperty("id", out var cidv) ? cidv.GetString() : null;
                    if (string.IsNullOrEmpty(cid)) continue;
                    var date = a.TryGetProperty("readableAt", out var rd) && rd.ValueKind == JsonValueKind.String
                        ? rd.GetString()
                        : (a.TryGetProperty("publishAt", out var pd) && pd.ValueKind == JsonValueKind.String ? pd.GetString() : string.Empty);

                    var entry = new Chapter
                    {
                        Id = cid,
                        Title = label,
                        Number = double.TryParse(chapter, NumberStyles.Float, CultureInfo.InvariantCulture, out var num) ? num : 0,
                        Date = date ?? string.Empty,
                    };
                    if (!found.TryGetValue(key, out var old) || string.Compare(entry.Date, old.Date, StringComparison.Ordinal) > 0)
                        found[key] = entry;
                }
                if (count < size) break;
                offset += size;
                if (offset > 5000) break;
            }

            return found.Values.ToList();
        }

        /// <summary>Feed somente em pt-br, sem completar com capitulos em ingles.</summary>
        public async Task<List<Chapter>> ChaptersAsync(string id)
        {
            var list = await FeedAsync(id, "pt-br").ConfigureAwait(false);

            list.Sort((x, y) =>
            {
                var c = x.Number.CompareTo(y.Number);
                if (c != 0) return c;
                return string.Compare(x.Title, y.Title, StringComparison.OrdinalIgnoreCase);
            });
            return list;
        }

        public async Task<PageSet> PagesAsync(string token, bool adult)
        {
            var j = await GetAsync("/at-home/server/" + Uri.EscapeDataString(token)).ConfigureAwait(false);
            var set = new PageSet();
            var baseUrl = j.TryGetProperty("baseUrl", out var bu) && bu.ValueKind == JsonValueKind.String ? bu.GetString() : null;
            if (string.IsNullOrEmpty(baseUrl) || !j.TryGetProperty("chapter", out var ch)) 
                throw new Exception("Capitulo sem paginas disponiveis.");
            var hash = ch.TryGetProperty("hash", out var hv) && hv.ValueKind == JsonValueKind.String ? hv.GetString() : null;
            if (string.IsNullOrEmpty(hash)) throw new Exception("Capitulo sem paginas disponiveis.");
            baseUrl = baseUrl.TrimEnd('/');

            void Add(string kind, JsonElement arr)
            {
                if (arr.ValueKind != JsonValueKind.Array) return;
                foreach (var f in arr.EnumerateArray())
                    if (f.ValueKind == JsonValueKind.String) set.Images.Add(baseUrl + "/" + kind + "/" + hash + "/" + f.GetString());
            }

            if (ch.TryGetProperty("data", out var dataArr)) Add("data", dataArr);
            if (set.Images.Count == 0 && ch.TryGetProperty("dataSaver", out var saver)) Add("data-saver", saver);
            if (set.Images.Count == 0) throw new Exception("Capitulo sem paginas disponiveis.");
            return set;
        }

        public Task<List<string>> GenresAsync()
        {
            var list = UiGenres.ToList();
            if (_adultOnly)
            {
                if (!list.Contains("Hentai")) list.Add("Hentai");
                if (!list.Contains("Ecchi")) list.Add("Ecchi");
            }
            return Task.FromResult(list);
        }
    }
}
