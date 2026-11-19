using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace SoloMangasNative
{
    public sealed class FavEntry
    {
        public string Source { get; set; } = string.Empty;
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Cover { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public bool Adult { get; set; }
        public DateTime Added { get; set; } = DateTime.Now;

        [JsonIgnore] public string Key => Source + ":" + Id;
    }

    public sealed class HistoryEntry
    {
        public string Source { get; set; } = string.Empty;
        public string MangaId { get; set; } = string.Empty;
        public string MangaTitle { get; set; } = string.Empty;
        public string Cover { get; set; } = string.Empty;
        public string ChapterId { get; set; } = string.Empty;
        public string ChapterTitle { get; set; } = string.Empty;
        public bool Adult { get; set; }
        public double Position { get; set; }
        public DateTime Updated { get; set; } = DateTime.Now;

        [JsonIgnore] public string Key => Source + ":" + MangaId;
    }

    /// <summary>Pesquisa recente de uma fonte (chips abaixo da barra de busca).</summary>
    public sealed class SearchEntry
    {
        public string Source { get; set; } = string.Empty;
        public string Query { get; set; } = string.Empty;
        public DateTime Used { get; set; } = DateTime.Now;

        [JsonIgnore] public string Key => Source + "\u0001" + Query.ToLowerInvariant();
    }

    public sealed class AppSettings
    {
        public bool Adult { get; set; }
        public string TitlePreference { get; set; } = "pt";
        public string LastSource { get; set; } = "mangeek";
        public string LastGenre { get; set; } = string.Empty;
        /// <summary>Busca unificada: consulta todas as fontes de uma vez e agrupa títulos repetidos.</summary>
        public bool GlobalSearch { get; set; } = true;
        /// <summary>Leitor continuo: emenda o proximo capitulo no fim do atual.</summary>
        public bool ContinuousRead { get; set; } = true;
        /// <summary>Largura das paginas: 0 automatica, 1 compacta, 2 confortavel, 3 ampla.</summary>
        public int ReaderWidth { get; set; }
        /// <summary>Modo do leitor: "webtoon" (rolagem continua) ou "page" (pagina a pagina).</summary>
        public string ReaderMode { get; set; } = "webtoon";
        /// <summary>Ordem inicial dos capitulos na pagina da obra: true = ultimo primeiro.</summary>
        public bool ChapterOrderDesc { get; set; } = true;
        // Ultima geometria da janela (0 = usar o padrao).
        public double WindowW { get; set; }
        public double WindowH { get; set; }
        public bool WindowMaximized { get; set; }
        /// <summary>
        /// Origem das atualizacoes: URL do update.json, pasta local/rede, caminho
        /// de um .json ou vazio para usar o update.json ao lado do executavel.
        /// </summary>
        public string UpdateSource { get; set; } = string.Empty;
        /// <summary>Verifica se ha versao nova ao abrir o app, sem atrapalhar a abertura.</summary>
        public bool CheckUpdatesOnStartup { get; set; } = true;
        /// <summary>Tema visual: "cod" (Call of Duty, padrao da 2.8) ou "sangue" (visual classico).</summary>
        public string Theme { get; set; } = "cod";
    }

    /// <summary>Resumo do que foi importado de um backup.</summary>
    public sealed class ImportResult
    {
        public int Favorites { get; set; }
        public int History { get; set; }
        public int Read { get; set; }
        public int Positions { get; set; }
        public int Searches { get; set; }
        public int Skipped { get; set; }
        public int Total => Favorites + History + Read + Positions + Searches;
    }

    /// <summary>Estado local: favoritos, historico, lidos, posicoes e configuracoes.</summary>
    public sealed class LocalStore
    {
        public static LocalStore Instance { get; } = new LocalStore();

        private sealed class State
        {
            public AppSettings Settings { get; set; } = new AppSettings();
            public List<FavEntry> Favorites { get; set; } = new List<FavEntry>();
            public List<HistoryEntry> History { get; set; } = new List<HistoryEntry>();
            public List<SearchEntry> Searches { get; set; } = new List<SearchEntry>();
            public List<string> Read { get; set; } = new List<string>();
            public Dictionary<string, double> Positions { get; set; } = new Dictionary<string, double>();
        }

        private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        private readonly object _gate = new object();
        private State _state = new State();
        private HashSet<string> _readSet = new HashSet<string>(StringComparer.Ordinal);
        private int _savePending;

        public string DataDir { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SoloMangas");

        public string StoreFile => Path.Combine(DataDir, "store.json");

        public AppSettings Settings => _state.Settings;

        public void Load()
        {
            lock (_gate)
            {
                try
                {
                    if (!File.Exists(StoreFile)) return;
                    var json = File.ReadAllText(StoreFile);
                    var loaded = JsonSerializer.Deserialize<State>(json, JsonOpts);
                    if (loaded != null) _state = loaded;
                    _state.Settings ??= new AppSettings();
                    _state.Favorites ??= new List<FavEntry>();
                    _state.History ??= new List<HistoryEntry>();
                    _state.Searches ??= new List<SearchEntry>();
                    _state.Read ??= new List<string>();
                    _state.Positions ??= new Dictionary<string, double>();
                    _readSet = new HashSet<string>(_state.Read, StringComparer.Ordinal);
                }
                catch
                {
                    // estado corrompido: segue com o padrao, sem derrubar o app
                }
            }
        }

        public void Save()
        {
            lock (_gate)
            {
                try
                {
                    Directory.CreateDirectory(DataDir);
                    var json = JsonSerializer.Serialize(_state, JsonOpts);
                    var tmp = StoreFile + ".tmp";
                    File.WriteAllText(tmp, json);
                    File.Copy(tmp, StoreFile, true);
                    try { File.Delete(tmp); } catch { }
                }
                catch
                {
                }
            }
        }

        /// <summary>
        /// Gravação agendada: junta várias mudanças (posição de leitura, histórico)
        /// em uma única escrita no disco, fora da thread da interface.
        /// </summary>
        public void SaveSoon()
        {
            if (Interlocked.Exchange(ref _savePending, 1) == 1) return;
            _ = Task.Run(async () =>
            {
                await Task.Delay(700).ConfigureAwait(false);
                Interlocked.Exchange(ref _savePending, 0);
                Save();
            });
        }

        /// <summary>Garante a gravação ao fechar o app.</summary>
        public void Flush()
        {
            Interlocked.Exchange(ref _savePending, 0);
            Save();
        }

        // ---------------- Favoritos ----------------
        public IReadOnlyList<FavEntry> Favorites
        {
            get { lock (_gate) return _state.Favorites.OrderByDescending(f => f.Added).ToList(); }
        }

        public bool IsFavorite(string source, string id)
        {
            lock (_gate)
                return _state.Favorites.Any(f =>
                    f.Source.Equals(source, StringComparison.OrdinalIgnoreCase) && f.Id == id);
        }

        public bool ToggleFavorite(Manga m)
        {
            lock (_gate)
            {
                var hit = _state.Favorites.FirstOrDefault(f =>
                    f.Source.Equals(m.Source, StringComparison.OrdinalIgnoreCase) && f.Id == m.Id);
                if (hit != null)
                {
                    _state.Favorites.Remove(hit);
                    SaveSoon();
                    return false;
                }
                _state.Favorites.Add(new FavEntry
                {
                    Source = m.Source,
                    Id = m.Id,
                    Title = m.Title,
                    Cover = m.Cover,
                    Url = m.Url,
                    Adult = m.Adult,
                    Added = DateTime.Now,
                });
                SaveSoon();
                return true;
            }
        }

        public void RemoveFavorite(string source, string id)
        {
            lock (_gate)
            {
                _state.Favorites.RemoveAll(f =>
                    f.Source.Equals(source, StringComparison.OrdinalIgnoreCase) && f.Id == id);
                SaveSoon();
            }
        }

        // ---------------- Lidos ----------------
        public bool IsRead(string source, string chapterId)
        {
            lock (_gate) return _readSet.Contains(source + ":" + chapterId);
        }

        public void MarkRead(string source, string chapterId)
        {
            lock (_gate)
            {
                var key = source + ":" + chapterId;
                if (_readSet.Contains(key)) return;
                _readSet.Add(key);
                _state.Read.Add(key);
                if (_state.Read.Count > 5000)
                {
                    var drop = _state.Read.Take(1000).ToList();
                    _state.Read.RemoveRange(0, 1000);
                    foreach (var d in drop) _readSet.Remove(d);
                }
                SaveSoon();
            }
        }

        public int ReadCount(string source, IEnumerable<Chapter> chapters)
        {
            lock (_gate)
                return chapters.Count(c => _readSet.Contains(source + ":" + c.Id));
        }

        // ---------------- Historico ----------------
        public IReadOnlyList<HistoryEntry> History
        {
            get { lock (_gate) return _state.History.OrderByDescending(h => h.Updated).ToList(); }
        }

        public HistoryEntry GetHistory(string source, string mangaId)
        {
            lock (_gate)
                return _state.History.FirstOrDefault(h =>
                    h.Source.Equals(source, StringComparison.OrdinalIgnoreCase) && h.MangaId == mangaId);
        }

        public void UpsertHistory(HistoryEntry entry)
        {
            lock (_gate)
            {
                var hit = _state.History.FirstOrDefault(h =>
                    h.Source.Equals(entry.Source, StringComparison.OrdinalIgnoreCase) && h.MangaId == entry.MangaId);
                if (hit == null)
                {
                    _state.History.Add(entry);
                }
                else
                {
                    hit.ChapterId = entry.ChapterId;
                    hit.ChapterTitle = entry.ChapterTitle;
                    hit.Position = entry.Position;
                    hit.Adult = entry.Adult;
                    hit.Updated = DateTime.Now;
                    if (!string.IsNullOrWhiteSpace(entry.MangaTitle)) hit.MangaTitle = entry.MangaTitle;
                    if (!string.IsNullOrWhiteSpace(entry.Cover)) hit.Cover = entry.Cover;
                }
                if (_state.History.Count > 500)
                {
                    var keep = _state.History.OrderByDescending(h => h.Updated).Take(400).ToList();
                    _state.History = keep;
                }
                SaveSoon();
            }
        }

        public void RemoveHistory(string source, string mangaId)
        {
            lock (_gate)
            {
                _state.History.RemoveAll(h =>
                    h.Source.Equals(source, StringComparison.OrdinalIgnoreCase) && h.MangaId == mangaId);
                SaveSoon();
            }
        }

        public void ClearHistory()
        {
            lock (_gate)
            {
                _state.History.Clear();
                SaveSoon();
            }
        }

        // ---------------- Pesquisas recentes ----------------
        public IReadOnlyList<SearchEntry> RecentSearches(string source, int max = 10)
        {
            lock (_gate)
            {
                return _state.Searches
                    .Where(s => s.Source.Equals(source, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(s => s.Used)
                    .Take(max)
                    .ToList();
            }
        }

        public void AddSearch(string source, string query)
        {
            query = (query ?? string.Empty).Trim();
            if (query.Length < 2) return;
            lock (_gate)
            {
                _state.Searches.RemoveAll(s =>
                    s.Source.Equals(source, StringComparison.OrdinalIgnoreCase) &&
                    s.Query.Equals(query, StringComparison.OrdinalIgnoreCase));
                _state.Searches.Add(new SearchEntry
                {
                    Source = source,
                    Query = query,
                    Used = DateTime.Now,
                });
                if (_state.Searches.Count > 200)
                    _state.Searches = _state.Searches.OrderByDescending(s => s.Used).Take(150).ToList();
                SaveSoon();
            }
        }

        public void ClearSearches(string source)
        {
            lock (_gate)
            {
                _state.Searches.RemoveAll(s => s.Source.Equals(source, StringComparison.OrdinalIgnoreCase));
                SaveSoon();
            }
        }

        // ---------------- Backup ----------------
        /// <summary>
        /// Restaura favoritos, historico, lidos, posicoes e pesquisas de um
        /// store.json exportado. Faz mescla: nada do estado atual e apagado e
        /// entradas repetidas sao mantidas uma unica vez.
        /// </summary>
        public ImportResult ImportBackup(string path)
        {
            var json = File.ReadAllText(path);
            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(json, new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip,
                });
            }
            catch (JsonException)
            {
                throw new Exception("O arquivo selecionado nao e um JSON valido.");
            }

            using (doc)
            {
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                    throw new Exception("O arquivo selecionado nao parece ser um backup do Solo Mangas.");

                var result = new ImportResult();
                lock (_gate)
                {
                    // -------- favoritos --------
                    if (root.TryGetProperty("Favorites", out var favs) && favs.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var el in favs.EnumerateArray())
                        {
                            var source = Text(el, "Source");
                            var id = Text(el, "Id");
                            if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(id))
                            {
                                result.Skipped++;
                                continue;
                            }
                            var exists = _state.Favorites.Any(f =>
                                f.Source.Equals(source, StringComparison.OrdinalIgnoreCase) && f.Id == id);
                            if (exists)
                            {
                                result.Skipped++;
                                continue;
                            }
                            _state.Favorites.Add(new FavEntry
                            {
                                Source = source,
                                Id = id,
                                Title = Text(el, "Title"),
                                Cover = Text(el, "Cover"),
                                Url = Text(el, "Url"),
                                Adult = Bool(el, "Adult"),
                                Added = Date(el, "Added") ?? DateTime.Now,
                            });
                            result.Favorites++;
                        }
                    }

                    // -------- historico --------
                    if (root.TryGetProperty("History", out var hist) && hist.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var el in hist.EnumerateArray())
                        {
                            var source = Text(el, "Source");
                            var mangaId = Text(el, "MangaId");
                            if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(mangaId))
                            {
                                result.Skipped++;
                                continue;
                            }

                            var when = Date(el, "Updated") ?? DateTime.Now;
                            var existing = _state.History.FirstOrDefault(h =>
                                h.Source.Equals(source, StringComparison.OrdinalIgnoreCase) && h.MangaId == mangaId);
                            if (existing == null)
                            {
                                _state.History.Add(new HistoryEntry
                                {
                                    Source = source,
                                    MangaId = mangaId,
                                    MangaTitle = Text(el, "MangaTitle"),
                                    Cover = Text(el, "Cover"),
                                    ChapterId = Text(el, "ChapterId"),
                                    ChapterTitle = Text(el, "ChapterTitle"),
                                    Adult = Bool(el, "Adult"),
                                    Position = Clamp01(Number(el, "Position")),
                                    Updated = when,
                                });
                                result.History++;
                                continue;
                            }

                            // Mantem o capitulo mais recente entre o atual e o backup.
                            if (when <= existing.Updated)
                            {
                                result.Skipped++;
                                continue;
                            }
                            existing.ChapterId = Text(el, "ChapterId");
                            existing.ChapterTitle = Text(el, "ChapterTitle");
                            existing.Position = Clamp01(Number(el, "Position"));
                            existing.Updated = when;
                            if (!string.IsNullOrWhiteSpace(Text(el, "MangaTitle"))) existing.MangaTitle = Text(el, "MangaTitle");
                            if (!string.IsNullOrWhiteSpace(Text(el, "Cover"))) existing.Cover = Text(el, "Cover");
                            result.History++;
                        }
                    }

                    // -------- capitulos lidos --------
                    if (root.TryGetProperty("Read", out var read) && read.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var el in read.EnumerateArray())
                        {
                            if (el.ValueKind != JsonValueKind.String) continue;
                            var key = el.GetString();
                            if (string.IsNullOrWhiteSpace(key) || !key.Contains(':')) continue;
                            if (!_readSet.Add(key))
                            {
                                result.Skipped++;
                                continue;
                            }
                            _state.Read.Add(key);
                            result.Read++;
                        }
                        if (_state.Read.Count > 5000)
                        {
                            var drop = _state.Read.Take(1000).ToList();
                            _state.Read.RemoveRange(0, 1000);
                            foreach (var d in drop) _readSet.Remove(d);
                        }
                    }

                    // -------- posicoes --------
                    if (root.TryGetProperty("Positions", out var pos) && pos.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var p in pos.EnumerateObject())
                        {
                            if (!p.Value.TryGetDouble(out var v)) continue;
                            _state.Positions[p.Name] = Clamp01(v);
                            result.Positions++;
                        }
                    }

                    // -------- pesquisas recentes --------
                    if (root.TryGetProperty("Searches", out var searches) && searches.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var el in searches.EnumerateArray())
                        {
                            var source = Text(el, "Source");
                            var query = Text(el, "Query");
                            if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(query))
                            {
                                result.Skipped++;
                                continue;
                            }
                            var exists = _state.Searches.Any(s =>
                                s.Source.Equals(source, StringComparison.OrdinalIgnoreCase) &&
                                s.Query.Equals(query, StringComparison.OrdinalIgnoreCase));
                            if (exists)
                            {
                                result.Skipped++;
                                continue;
                            }
                            _state.Searches.Add(new SearchEntry
                            {
                                Source = source,
                                Query = query,
                                Used = Date(el, "Used") ?? DateTime.Now,
                            });
                            result.Searches++;
                        }
                        if (_state.Searches.Count > 200)
                            _state.Searches = _state.Searches.OrderByDescending(s => s.Used).Take(150).ToList();
                    }

                    Save();
                }
                return result;
            }
        }

        private static string Text(JsonElement el, string name) =>
            el.ValueKind == JsonValueKind.Object && el.TryGetProperty(name, out var v) &&
            v.ValueKind == JsonValueKind.String ? v.GetString() : string.Empty;

        private static bool Bool(JsonElement el, string name) =>
            el.ValueKind == JsonValueKind.Object && el.TryGetProperty(name, out var v) &&
            (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False) && v.GetBoolean();

        private static double Number(JsonElement el, string name) =>
            el.ValueKind == JsonValueKind.Object && el.TryGetProperty(name, out var v) &&
            v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d) ? d : 0;

        private static DateTime? Date(JsonElement el, string name)
        {
            var text = Text(el, name);
            return DateTime.TryParse(text, out var d) ? d : null;
        }

        private static double Clamp01(double value) => Math.Max(0, Math.Min(1, value));

        // ---------------- Posicoes ----------------
        public double GetPosition(string source, string chapterId)
        {
            lock (_gate)
                return _state.Positions.TryGetValue(source + ":" + chapterId, out var v) ? v : 0;
        }

        public void SetPosition(string source, string chapterId, double ratio)
        {
            lock (_gate)
            {
                _state.Positions[source + ":" + chapterId] = Math.Max(0, Math.Min(1, ratio));
            }
            SaveSoon();
        }
    }
}

