using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using SoloMangas.Shared;

namespace SoloMangasNative
{
    /// <summary>
    /// Suki Mangás: catálogo próprio em português servido por uma API JSON.
    /// /api/mangas devolve todas as obras (com a lista de capítulos) e
    /// /api/mangas/{id} devolve a obra com as páginas de cada capítulo.
    /// Não exige login e as imagens ficam num CDN público (R2).
    /// </summary>
    public sealed class SukiSource : ISource
    {
        private const string Base = "https://www.sukimangas.com";

        private static readonly HashSet<string> AdultGenres = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "hentai", "smut", "adulto", "adult", "adultos", "adulto (18+)", "adulto (18 +)",
            "+18", "18+", "pornografico", "pornográfico", "explícito", "explicito",
        };

        private static readonly TimeSpan DbTtl = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan DetailTtl = TimeSpan.FromMinutes(15);

        public string Key => "suki";
        public string Name => "Suki Mangás";
        public bool SupportsLogin => false;
        public bool AdultOnly => false;

        // ------------------------------------------------------------------
        // HTTP / JSON
        // ------------------------------------------------------------------
        private static async Task<string> GetTextAsync(string url)
        {
            var res = await HttpUtil.RequestAsync(url, headers: new Dictionary<string, string>
            {
                ["Accept"] = "application/json, text/plain, */*",
                ["Accept-Language"] = "pt-BR,pt;q=0.9",
                ["Referer"] = Base + "/",
            }, timeoutMs: 30000, userAgent: HttpUtil.ChromeUa).ConfigureAwait(false);

            if (res.Status >= 200 && res.Status < 300) return res.Raw ?? string.Empty;
            if (res.Status == 404) throw new Exception("Obra não encontrada no Suki Mangás.");
            var detail = res.Status == 0
                ? (string.IsNullOrWhiteSpace(res.Error) ? "sem resposta" : res.Error)
                : "HTTP " + res.Status;
            throw new Exception("Suki Mangás: " + detail + ".");
        }

        private static async Task<JsonElement> GetJsonAsync(string url)
        {
            var raw = await GetTextAsync(url).ConfigureAwait(false);
            try
            {
                using var doc = JsonDocument.Parse(raw.TrimStart('\uFEFF'));
                return doc.RootElement.Clone();
            }
            catch (JsonException)
            {
                throw new Exception("Suki Mangás devolveu uma resposta inválida.");
            }
        }

        // ------------------------------------------------------------------
        // Catálogo completo (uma requisição, reaproveitada por busca/gêneros)
        // ------------------------------------------------------------------
        private readonly object _dbGate = new object();
        private JsonElement _db;
        private DateTime _dbAt = DateTime.MinValue;
        private Task<JsonElement> _dbTask;

        private async Task<JsonElement> AllAsync()
        {
            Task<JsonElement> task;
            lock (_dbGate)
            {
                if (_db.ValueKind == JsonValueKind.Object && DateTime.UtcNow - _dbAt < DbTtl)
                    return _db;
                task = _dbTask;
                if (task == null)
                {
                    task = LoadAllAsync();
                    _dbTask = task;
                }
            }

            try
            {
                return await task.ConfigureAwait(false);
            }
            finally
            {
                lock (_dbGate)
                {
                    if (ReferenceEquals(_dbTask, task)) _dbTask = null;
                }
            }
        }

        private async Task<JsonElement> LoadAllAsync()
        {
            var root = await GetJsonAsync(Base + "/api/mangas").ConfigureAwait(false);
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("mangas", out var arr) ||
                arr.ValueKind != JsonValueKind.Array)
                throw new Exception("Suki Mangás: catálogo em formato inesperado.");

            lock (_dbGate)
            {
                _db = root;
                _dbAt = DateTime.UtcNow;
            }
            return root;
        }

        // ------------------------------------------------------------------
        // Detalhe (também traz as páginas de todos os capítulos)
        // ------------------------------------------------------------------
        private sealed class DetailEntry
        {
            public DateTime At;
            public JsonElement Manga;
        }

        private readonly object _detailGate = new object();
        private readonly Dictionary<string, DetailEntry> _detailCache =
            new Dictionary<string, DetailEntry>(StringComparer.Ordinal);
        private readonly Dictionary<string, Task<JsonElement>> _detailFlight =
            new Dictionary<string, Task<JsonElement>>(StringComparer.Ordinal);

        private async Task<JsonElement> DetailAsync(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new Exception("Obra inválida no Suki Mangás.");
            Task<JsonElement> task;
            lock (_detailGate)
            {
                if (_detailCache.TryGetValue(id, out var hit) && DateTime.UtcNow - hit.At < DetailTtl)
                    return hit.Manga;
                if (!_detailFlight.TryGetValue(id, out task))
                {
                    task = LoadDetailAsync(id);
                    _detailFlight[id] = task;
                }
            }

            try
            {
                return await task.ConfigureAwait(false);
            }
            finally
            {
                lock (_detailGate) _detailFlight.Remove(id);
            }
        }

        private async Task<JsonElement> LoadDetailAsync(string id)
        {
            var root = await GetJsonAsync(Base + "/api/mangas/" + Uri.EscapeDataString(id)).ConfigureAwait(false);
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("manga", out var manga) ||
                manga.ValueKind != JsonValueKind.Object)
                throw new Exception("Obra não encontrada no Suki Mangás.");

            lock (_detailGate)
            {
                _detailCache[id] = new DetailEntry { At = DateTime.UtcNow, Manga = manga };
                if (_detailCache.Count > 60)
                {
                    var oldest = _detailCache.OrderBy(kv => kv.Value.At).First().Key;
                    if (oldest != id) _detailCache.Remove(oldest);
                }
            }
            return manga;
        }

        // ------------------------------------------------------------------
        // Conversão do JSON para os modelos do app
        // ------------------------------------------------------------------
        private static string Str(JsonElement e, string name)
        {
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v)) return string.Empty;
            return v.ValueKind == JsonValueKind.String ? (v.GetString() ?? string.Empty) : string.Empty;
        }

        private static int Int(JsonElement e, string name)
        {
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v)) return 0;
            if (v.ValueKind == JsonValueKind.Number) return v.TryGetInt32(out var n) ? n : 0;
            if (v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var p)) return p;
            return 0;
        }

        private static double Double(JsonElement e, string name)
        {
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v)) return 0;
            if (v.ValueKind == JsonValueKind.Number) return v.TryGetDouble(out var n) ? n : 0;
            if (v.ValueKind == JsonValueKind.String)
                return double.TryParse(v.GetString()?.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var p) ? p : 0;
            return 0;
        }

        private static List<string> GenresOf(JsonElement e)
        {
            var list = new List<string>();
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty("genres", out var arr) ||
                arr.ValueKind != JsonValueKind.Array) return list;
            foreach (var g in arr.EnumerateArray())
            {
                if (g.ValueKind != JsonValueKind.String) continue;
                var name = (g.GetString() ?? string.Empty).Trim();
                if (name.Length > 0 && !list.Contains(name, StringComparer.OrdinalIgnoreCase)) list.Add(name);
            }
            return list;
        }

        private static string Norm(string v)
        {
            if (string.IsNullOrEmpty(v)) return string.Empty;
            var s = v.Normalize(System.Text.NormalizationForm.FormD);
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (var c in s)
                if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) !=
                    System.Globalization.UnicodeCategory.NonSpacingMark) sb.Append(c);
            return Regex.Replace(sb.ToString(), "\\s+", " ").Trim().ToLowerInvariant();
        }

        private static bool LooksAdult(string title, string description, IEnumerable<string> genres)
        {
            foreach (var g in genres ?? Enumerable.Empty<string>())
                if (AdultGenres.Contains(Norm(g))) return true;
            var text = Norm((title ?? string.Empty) + " " + (description ?? string.Empty));
            return Regex.IsMatch(text, "\\bhentai\\b|\\bsmut\\b|\\bnsfw\\b|\\+18\\b|\\b18\\+\\b");
        }

        private static Manga ToManga(JsonElement m)
        {
            var id = Str(m, "id");
            var title = Str(m, "title");
            var titleAlt = Str(m, "titleOriginal");
            var desc = Str(m, "synopsis");
            var genres = GenresOf(m);
            var cover = Str(m, "cover");
            if (cover.Length == 0) cover = Str(m, "coverThumb");
            var chapters = 0;
            var latest = string.Empty;
            if (m.TryGetProperty("chapters", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                chapters = arr.GetArrayLength();
                if (chapters > 0)
                {
                    var last = arr[chapters - 1];
                    latest = Str(last, "title");
                }
            }

            return new Manga
            {
                Source = "suki",
                Id = id,
                Title = string.IsNullOrWhiteSpace(title) ? "Sem título" : title,
                TitleAlt = titleAlt,
                Cover = cover,
                Description = desc,
                Status = Str(m, "status"),
                Url = Base + "/manga?id=" + Uri.EscapeDataString(id),
                Latest = latest,
                Adult = LooksAdult(title, desc, genres),
                Chapters = chapters,
                Year = Int(m, "year"),
                Genres = genres,
            };
        }

        private static List<Chapter> ChaptersOf(JsonElement manga, string mangaId)
        {
            var list = new List<Chapter>();
            if (manga.ValueKind != JsonValueKind.Object ||
                !manga.TryGetProperty("chapters", out var arr) ||
                arr.ValueKind != JsonValueKind.Array) return list;

            foreach (var c in arr.EnumerateArray())
            {
                var chapterId = Str(c, "id");
                if (string.IsNullOrWhiteSpace(chapterId)) continue;
                var number = Double(c, "number");
                var title = Str(c, "title");
                if (string.IsNullOrWhiteSpace(title)) title = "Capítulo " + number.ToString("0.##", CultureInfo.InvariantCulture);
                list.Add(new Chapter
                {
                    // A API de páginas precisa da obra + capítulo; o token composto
                    // continua sendo um id estável para histórico e favoritos.
                    Id = mangaId + "|" + chapterId,
                    Title = title.Trim(),
                    Number = number,
                    Date = Str(c, "releaseDate"),
                });
            }
            return ChapterOrder.Ascending(list);
        }

        private static Manga Copy(Manga m) => new Manga
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

        // ------------------------------------------------------------------
        // ISource
        // ------------------------------------------------------------------
        public async Task<CatalogResult> CatalogAsync(SourceQuery query)
        {
            var page = new CatalogResult { Page = Math.Max(1, query.Page) };
            var root = await AllAsync().ConfigureAwait(false);
            if (!root.TryGetProperty("mangas", out var arr) || arr.ValueKind != JsonValueKind.Array)
                return page;

            var q = (query.Q ?? string.Empty).Trim();
            var genre = (query.Genre ?? string.Empty).Trim();
            var wantGenre = genre.Length > 0 && Norm(genre) != "tudo" && Norm(genre) != "todos";
            var items = new List<(Manga Manga, int Score, long Views)>();

            foreach (var raw in arr.EnumerateArray())
            {
                var manga = ToManga(raw);
                if (!query.Adult && manga.Adult) continue;
                if (wantGenre && !manga.Genres.Any(g => Norm(g) == Norm(genre))) continue;

                var score = 4;
                if (q.Length > 0)
                {
                    score = Math.Min(TitleTools.Score(manga.Title, q),
                        string.IsNullOrWhiteSpace(manga.TitleAlt) ? 4 : TitleTools.Score(manga.TitleAlt, q));
                    if (score >= 4) continue;
                }

                long views = 0;
                if (raw.TryGetProperty("views", out var v) && v.ValueKind == JsonValueKind.Number) v.TryGetInt64(out views);
                items.Add((manga, score, views));
            }

            var ordered = q.Length > 0
                ? items.OrderBy(x => x.Score).ThenByDescending(x => x.Views).ThenBy(x => x.Manga.Title, StringComparer.CurrentCultureIgnoreCase)
                : items.OrderByDescending(x => x.Views).ThenBy(x => x.Manga.Title, StringComparer.CurrentCultureIgnoreCase);

            var limit = query.Limit <= 0 ? 28 : Math.Min(60, query.Limit);
            var skip = (page.Page - 1) * limit;
            page.Total = items.Count;
            page.Items = ordered.Skip(skip).Take(limit).Select(x => Copy(x.Manga)).ToList();
            page.HasMore = skip + page.Items.Count < page.Total;
            return page;
        }

        public async Task<Manga> MangaAsync(string id, bool adult)
        {
            var manga = await DetailAsync(id).ConfigureAwait(false);
            return Copy(ToManga(manga));
        }

        public async Task<List<Chapter>> ChaptersAsync(string id)
        {
            var manga = await DetailAsync(id).ConfigureAwait(false);
            return ChaptersOf(manga, id);
        }

        public async Task<PageSet> PagesAsync(string token, bool adult)
        {
            var raw = (token ?? string.Empty).Trim();
            var sep = raw.IndexOf('|');
            string mangaId;
            string chapterId;

            if (sep > 0)
            {
                mangaId = raw.Substring(0, sep);
                chapterId = raw.Substring(sep + 1);
            }
            else
            {
                (mangaId, chapterId) = await ResolveChapterAsync(raw).ConfigureAwait(false);
            }

            var manga = await DetailAsync(mangaId).ConfigureAwait(false);
            if (!manga.TryGetProperty("chapters", out var arr) || arr.ValueKind != JsonValueKind.Array)
                throw new Exception("Capítulo não encontrado no Suki Mangás.");

            foreach (var c in arr.EnumerateArray())
            {
                if (!string.Equals(Str(c, "id"), chapterId, StringComparison.Ordinal)) continue;

                var set = new PageSet();
                ReadPages(c, "pages", set.Images);
                if (set.Images.Count == 0) ReadPages(c, "pagesLeves", set.Images);
                if (set.Images.Count == 0 && c.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                    set.Text = text.GetString() ?? string.Empty;
                if (set.Images.Count == 0 && string.IsNullOrWhiteSpace(set.Text))
                    throw new Exception("Capítulo sem páginas disponíveis.");
                return set;
            }

            throw new Exception("Capítulo não encontrado no Suki Mangás.");
        }

        private static void ReadPages(JsonElement chapter, string property, List<string> images)
        {
            if (chapter.ValueKind != JsonValueKind.Object ||
                !chapter.TryGetProperty(property, out var arr) ||
                arr.ValueKind != JsonValueKind.Array) return;

            foreach (var p in arr.EnumerateArray())
            {
                string url = null;
                if (p.ValueKind == JsonValueKind.String) url = p.GetString();
                else if (p.ValueKind == JsonValueKind.Object)
                {
                    url = Str(p, "url");
                    if (string.IsNullOrWhiteSpace(url)) url = Str(p, "src");
                    if (string.IsNullOrWhiteSpace(url)) url = Str(p, "image");
                }
                if (string.IsNullOrWhiteSpace(url)) continue;
                url = url.Trim();
                if (!images.Contains(url, StringComparer.Ordinal)) images.Add(url);
            }
        }

        /// <summary>Fallback para tokens sem obra: procura o capítulo no catálogo completo.</summary>
        private async Task<(string MangaId, string ChapterId)> ResolveChapterAsync(string chapterId)
        {
            if (string.IsNullOrWhiteSpace(chapterId))
                throw new Exception("Capítulo inválido no Suki Mangás.");

            var root = await AllAsync().ConfigureAwait(false);
            if (root.TryGetProperty("mangas", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var manga in arr.EnumerateArray())
                {
                    if (!manga.TryGetProperty("chapters", out var chapters) || chapters.ValueKind != JsonValueKind.Array) continue;
                    foreach (var c in chapters.EnumerateArray())
                    {
                        if (string.Equals(Str(c, "id"), chapterId, StringComparison.Ordinal))
                            return (Str(manga, "id"), chapterId);
                    }
                }
            }
            throw new Exception("Capítulo não encontrado no Suki Mangás.");
        }

        public async Task<List<string>> GenresAsync()
        {
            var root = await AllAsync().ConfigureAwait(false);
            var set = new SortedSet<string>(StringComparer.CurrentCultureIgnoreCase);
            if (root.TryGetProperty("mangas", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var manga in arr.EnumerateArray())
                    foreach (var g in GenresOf(manga))
                        if (!string.IsNullOrWhiteSpace(g)) set.Add(g);
            }
            return set.ToList();
        }
    }
}
