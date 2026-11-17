using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using SoloMangas.Shared;

namespace SoloMangasNative
{
    /// <summary>Fonte Mangeek (API v2 assinada). Compartilha a sessao de %APPDATA%\MangeekPC.</summary>
    public sealed class MangeekSource : ISource
    {
        private readonly MangeekApi _api;

        // Cache do feed: a pagina 1 vem das secoes do /home; as paginas seguintes
        // completam com o catalogo da tag mais ampla do Mangeek ("Acao"), sem
        // repetir obras. A home e refeita a cada "Atualizar"; o catalogo usa o
        // cache interno de 10 minutos do MangeekApi.
        private readonly object _cacheGate = new object();
        private List<Manga> _homeItems;
        private List<Manga> _feedItems;

        public MangeekSource(MangeekApi api)
        {
            _api = api;
        }

        public string Key => "mangeek";
        public string Name => "Mangeek";
        public bool SupportsLogin => true;
        public bool AdultOnly => false;

        private static string Str(JsonElement e, string name)
        {
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v)) return string.Empty;
            switch (v.ValueKind)
            {
                case JsonValueKind.String: return v.GetString() ?? string.Empty;
                case JsonValueKind.Number: return v.ToString();
                default: return string.Empty;
            }
        }

        private static string First(JsonElement e, params string[] names)
        {
            foreach (var n in names)
            {
                var s = Str(e, n);
                if (!string.IsNullOrWhiteSpace(s)) return s.Trim();
            }
            return string.Empty;
        }

        private static bool Bool(JsonElement e, string name)
        {
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v)) return false;
            switch (v.ValueKind)
            {
                case JsonValueKind.True: return true;
                case JsonValueKind.Number: return v.GetDouble() != 0;
                case JsonValueKind.String:
                    var s = v.GetString();
                    return s == "1" || string.Equals(s, "true", StringComparison.OrdinalIgnoreCase);
                default: return false;
            }
        }

        private static string FixUrl(string u)
        {
            if (string.IsNullOrWhiteSpace(u)) return string.Empty;
            u = u.Trim();
            if (u.StartsWith("//")) return "https:" + u;
            if (u.StartsWith("/")) return "https://geekstations.com.br" + u;
            return u;
        }

        private static List<string> Tags(JsonElement e)
        {
            var list = new List<string>();
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty("tags", out var tags)) return list;
            if (tags.ValueKind != JsonValueKind.Array) return list;
            foreach (var t in tags.EnumerateArray())
            {
                if (t.ValueKind == JsonValueKind.String)
                {
                    var s = t.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) list.Add(s.Trim());
                }
                else if (t.ValueKind == JsonValueKind.Object)
                {
                    var s = First(t, "name", "nome", "title");
                    if (!string.IsNullOrWhiteSpace(s)) list.Add(s);
                }
            }
            return list;
        }

        public static Manga Map(JsonElement e)
        {
            if (e.ValueKind != JsonValueKind.Object) return null;
            var id = First(e, "id", "manga_id");
            if (string.IsNullOrEmpty(id)) return null;

            var m = new Manga
            {
                Source = "mangeek",
                Id = id,
                Title = First(e, "title", "titulo") is var t && t.Length > 0 ? t : "Sem titulo",
                TitleAlt = First(e, "alternative_title", "title_alt", "title_orig"),
                Cover = FixUrl(First(e, "thumbnail", "cover", "image")),
                Description = First(e, "description", "descricao"),
                Latest = First(e, "latest", "ultimo_capitulo"),
                Adult = Bool(e, "adult"),
                Url = "https://geekstation.com.br/manga/" + id,
                Genres = Tags(e),
            };

            var finished = Bool(e, "finished");
            if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty("finished", out _))
                m.Status = finished ? "Completo" : "Em andamento";
            else
                m.Status = First(e, "status");

            if (e.TryGetProperty("chapters", out var ch))
            {
                if (ch.ValueKind == JsonValueKind.Array) m.Chapters = ch.GetArrayLength();
                else if (ch.ValueKind == JsonValueKind.Number && ch.TryGetInt32(out var n)) m.Chapters = n;
            }

            if (e.TryGetProperty("release_date", out var rd) && rd.ValueKind == JsonValueKind.String)
            {
                if (DateTime.TryParse(rd.GetString(), out var d)) m.Year = d.Year;
            }
            return m;
        }

        private static string Adopt(JsonElement data)
        {
            if (data.ValueKind == JsonValueKind.Object && data.TryGetProperty("data", out var inner) &&
                inner.ValueKind == JsonValueKind.Object)
                return null; // no-op placeholder
            return null;
        }

        public async Task<CatalogResult> CatalogAsync(SourceQuery query)
        {
            var page = new CatalogResult { Page = Math.Max(1, query.Page) };
            var limit = Math.Max(1, Math.Min(60, query.Limit));

            if (!string.IsNullOrWhiteSpace(query.Q))
            {
                var r = await _api.SearchAsync(query.Q.Trim(), Array.Empty<string>()).ConfigureAwait(false);
                if (r.Status < 200 || r.Status >= 300 || !r.Data.HasValue)
                    throw new Exception(ErrorMessage(r));
                if (r.Data.Value.ValueKind == JsonValueKind.Array)
                {
                    foreach (var e in r.Data.Value.EnumerateArray())
                    {
                        var m = Map(e);
                        if (m != null) page.Items.Add(m);
                    }
                }
                page.Total = page.Items.Count;
                page.HasMore = false;
                return page;
            }

            if (!string.IsNullOrWhiteSpace(query.Genre))
            {
                var r = await _api.TagAllAsync(query.Genre.Trim()).ConfigureAwait(false);
                if (r.Status < 200 || r.Status >= 300 || !r.Data.HasValue)
                    throw new Exception(ErrorMessage(r));
                if (r.Data.Value.ValueKind == JsonValueKind.Object &&
                    r.Data.Value.TryGetProperty("items", out var items) &&
                    items.ValueKind == JsonValueKind.Array)
                {
                    foreach (var e in items.EnumerateArray())
                    {
                        var m = Map(e);
                        if (m != null) page.Items.Add(m);
                    }
                }
                page.Total = page.Items.Count;
                page.HasMore = false;
                return page;
            }

            var pageNo = Math.Max(1, query.Page);
            var start = (pageNo - 1) * limit;

            // Pagina 1 sempre busca a home de novo (bota "Atualizar" funciona);
            // paginas seguintes reaproveitam a mesma lista para continuar a rolagem.
            var homeItems = await GetHomeItemsAsync(pageNo == 1).ConfigureAwait(false);

            // Quando a pagina passa do que as secoes da home entregam, o feed e
            // completado com o catalogo inteiro da tag "Acao" (milhares de obras).
            var feed = start + limit > homeItems.Count
                ? await GetFeedAsync(homeItems).ConfigureAwait(false)
                : homeItems;

            if (start < feed.Count)
                page.Items = feed.Skip(start).Take(limit).ToList();

            page.Total = feed.Count;
            page.HasMore = page.Items.Count > 0 && start + page.Items.Count < feed.Count;
            return page;
        }

        /// <summary>
        /// O /home devolve secoes curadas (trending, tops, novidades, banner e
        /// categorias) com cerca de 125 obras unicas. Devolve a lista completa,
        /// sem cortar no limite da pagina, para a rolagem continuar depois dela.
        /// </summary>
        private async Task<List<Manga>> GetHomeItemsAsync(bool fresh)
        {
            if (!fresh)
            {
                lock (_cacheGate)
                {
                    if (_homeItems != null) return _homeItems;
                }
            }

            Log.Write("mangeek: chamando /home");
            var home = await _api.SimpleAsync("/home").ConfigureAwait(false);
            Log.Write("mangeek: /home status " + home.Status);
            if (home.Status < 200 || home.Status >= 300 || !home.Data.HasValue)
                throw new Exception(ErrorMessage(home));

            var root = home.Data.Value;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var items = new List<Manga>();

            void AddOne(JsonElement e)
            {
                if (e.ValueKind != JsonValueKind.Object) return;
                // novidades trazem { id, title, manga: {...} }
                if (e.TryGetProperty("manga", out var inner) && inner.ValueKind == JsonValueKind.Object)
                {
                    var mm = Map(inner);
                    if (mm != null && seen.Add(mm.Id)) items.Add(mm);
                    return;
                }
                var m2 = Map(e);
                if (m2 != null && seen.Add(m2.Id)) items.Add(m2);
            }

            void AddSection(params string[] names)
            {
                foreach (var name in names)
                {
                    if (!root.TryGetProperty(name, out var arr)) continue;
                    if (arr.ValueKind != JsonValueKind.Array) continue;
                    foreach (var e in arr.EnumerateArray()) AddOne(e);
                }
            }

            AddSection("trending", "tops", "news", "banner");
            if (root.TryGetProperty("categories", out var cats) && cats.ValueKind == JsonValueKind.Array)
            {
                foreach (var cat in cats.EnumerateArray())
                {
                    if (cat.ValueKind != JsonValueKind.Object || !cat.TryGetProperty("list", out var list)) continue;
                    if (list.ValueKind != JsonValueKind.Array) continue;
                    foreach (var e in list.EnumerateArray()) AddOne(e);
                }
            }

            lock (_cacheGate)
            {
                _homeItems = items;
                _feedItems = null;
            }
            Log.Write("mangeek: home unica " + items.Count);
            return items;
        }

        /// <summary>
        /// Junta a home com o catalogo completo da tag "Acao" (a mais ampla do
        /// Mangeek) removendo IDs repetidos. O /discover nao pagina e devolve
        /// sempre os mesmos 25 itens, por isso o catalogo da tag e a fonte da
        /// rolagem infinita. A lista e montada uma vez por atualizacao da home.
        /// </summary>
        private async Task<List<Manga>> GetFeedAsync(List<Manga> homeItems)
        {
            lock (_cacheGate)
            {
                if (_feedItems != null) return _feedItems;
            }

            Log.Write("mangeek: montando feed paginado (" + homeItems.Count + " da home)");
            var feed = new List<Manga>(homeItems);
            var seen = new HashSet<string>(homeItems.Select(m => m.Id), StringComparer.Ordinal);

            var all = await _api.TagAllAsync("Ação").ConfigureAwait(false);
            if (all.Status >= 200 && all.Status < 300 && all.Data.HasValue &&
                all.Data.Value.ValueKind == JsonValueKind.Object &&
                all.Data.Value.TryGetProperty("items", out var arr) &&
                arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var e in arr.EnumerateArray())
                {
                    var m = Map(e);
                    if (m != null && seen.Add(m.Id)) feed.Add(m);
                }
                Log.Write("mangeek: catalogo completo +" + (feed.Count - homeItems.Count));
            }
            else
            {
                Log.Write("mangeek: catalogo completo indisponivel status " + all.Status);
            }

            lock (_cacheGate)
            {
                _feedItems = feed;
            }
            return feed;
        }

        public async Task<Manga> MangaAsync(string id, bool adult)
        {
            var r = await _api.IdAsync("/manga", id).ConfigureAwait(false);
            if (r.Status < 200 || r.Status >= 300 || !r.Data.HasValue)
                throw new Exception(ErrorMessage(r));
            var m = Map(r.Data.Value);
            if (m == null) throw new Exception("Obra nao encontrada.");
            m.Chapters = 0;
            if (r.Data.Value.TryGetProperty("chapters", out var ch) && ch.ValueKind == JsonValueKind.Array)
                m.Chapters = ch.GetArrayLength();
            return m;
        }

        public async Task<List<Chapter>> ChaptersAsync(string id)
        {
            var r = await _api.IdAsync("/manga", id).ConfigureAwait(false);
            if (r.Status < 200 || r.Status >= 300 || !r.Data.HasValue)
                throw new Exception(ErrorMessage(r));
            var list = new List<Chapter>();
            if (!r.Data.Value.TryGetProperty("chapters", out var arr) || arr.ValueKind != JsonValueKind.Array)
                return list;
            foreach (var e in arr.EnumerateArray())
            {
                var ch = new Chapter
                {
                    Id = First(e, "id"),
                    Title = First(e, "title") is var t && t.Length > 0 ? t : "Capitulo",
                    Date = First(e, "date", "created_at"),
                };
                ch.Number = ParseNumber(ch.Title);
                if (!string.IsNullOrEmpty(ch.Id)) list.Add(ch);
            }
            SortChapters(list);
            return list;
        }

        private static double ParseNumber(string title)
        {
            var m = System.Text.RegularExpressions.Regex.Match(title ?? string.Empty, @"(\d+(?:[.,]\d+)?)");
            if (!m.Success) return 0;
            return double.TryParse(m.Groups[1].Value.Replace(',', '.'),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : 0;
        }

        private static void SortChapters(List<Chapter> list)
        {
            list.Sort((a, b) =>
            {
                var c = a.Number.CompareTo(b.Number);
                if (c != 0) return c;
                return string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase);
            });
        }

        public async Task<PageSet> PagesAsync(string token, bool adult)
        {
            var r = await _api.IdAsync("/chapter", token).ConfigureAwait(false);
            if (r.Status < 200 || r.Status >= 300 || !r.Data.HasValue)
                throw new Exception(ErrorMessage(r));
            var set = new PageSet();
            if (r.Data.Value.TryGetProperty("pages", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var p in arr.EnumerateArray())
                {
                    if (p.ValueKind == JsonValueKind.String)
                    {
                        var u = p.GetString();
                        if (!string.IsNullOrWhiteSpace(u)) set.Images.Add(u.Trim());
                    }
                    else if (p.ValueKind == JsonValueKind.Object)
                    {
                        var u = First(p, "url", "src", "image");
                        if (!string.IsNullOrWhiteSpace(u)) set.Images.Add(u);
                    }
                }
            }
            return set;
        }

        public async Task<List<string>> GenresAsync()
        {
            var r = await _api.TagsUniverseAsync().ConfigureAwait(false);
            var list = new List<string>();
            if (r.Status >= 200 && r.Status < 300 && r.Data.HasValue && r.Data.Value.ValueKind == JsonValueKind.Array)
            {
                foreach (var e in r.Data.Value.EnumerateArray())
                {
                    if (e.ValueKind == JsonValueKind.String)
                    {
                        var s = e.GetString();
                        if (!string.IsNullOrWhiteSpace(s)) list.Add(s.Trim());
                    }
                    else if (e.ValueKind == JsonValueKind.Object)
                    {
                        var s = First(e, "name", "nome", "title");
                        if (!string.IsNullOrWhiteSpace(s)) list.Add(s);
                    }
                }
            }
            list.Sort(StringComparer.Create(new System.Globalization.CultureInfo("pt-BR"), true));
            return list;
        }

        private static string ErrorMessage(ApiResult r)
        {
            if (r.Status == 0) return "Sem conexao com o Mangeek.";
            if (r.Status == 401) return "Sessao expirada. Entre novamente.";
            if (r.Data.HasValue && r.Data.Value.ValueKind == JsonValueKind.Object &&
                r.Data.Value.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String)
                return m.GetString();
            return "Mangeek respondeu HTTP " + r.Status + ".";
        }
    }
}
