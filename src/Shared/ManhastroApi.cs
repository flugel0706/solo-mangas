using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace SoloMangas.Shared
{
    /// <summary>
    /// Fonte secundaria: api.manhastro.net.
    /// A API limita requisicoes (429) e as vezes responde 503/52x. Tudo passa por
    /// uma fila serial com intervalo minimo, retry progressivo e cache com fallback.
    /// </summary>
    public sealed class ManhastroApi
    {
        private const string BaseUrl = "https://api.manhastro.net";
        private const int MinGapMs = 900;

        private static readonly HashSet<int> Retryable = new HashSet<int>
        {
            429, 500, 502, 503, 504, 520, 521, 522, 523, 524,
        };

        private static readonly Regex ManhastroHostRe = new Regex(
            @"//([^/]*?)manhastro\.com(?=[/?#]|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private sealed class CacheEntry
        {
            public DateTime At;
            public object Value;
        }

        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        private readonly object _cacheGate = new object();
        private readonly Dictionary<string, CacheEntry> _cache = new Dictionary<string, CacheEntry>(StringComparer.Ordinal);
        private readonly List<string> _cacheOrder = new List<string>();
        private readonly HttpClient _client;
        private DateTime _lastAt = DateTime.MinValue;

        public ManhastroApi()
        {
            var handler = new SocketsHttpHandler
            {
                AllowAutoRedirect = true,
                AutomaticDecompression = DecompressionMethods.All,
                UseCookies = false,
                ConnectTimeout = TimeSpan.FromSeconds(15),
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                MaxConnectionsPerServer = 4,
            };
            _client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        }

        // ------------------------------------------------------------------
        // HTTP com fila + retry
        // ------------------------------------------------------------------
        private async Task<JsonElement> GetJsonAsync(string pathAndQuery, int tries = 4)
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                var lastStatus = 0;
                for (var attempt = 0; attempt < tries; attempt++)
                {
                    var wait = MinGapMs - (int)(DateTime.UtcNow - _lastAt).TotalMilliseconds;
                    if (wait > 0) await Task.Delay(wait).ConfigureAwait(false);
                    _lastAt = DateTime.UtcNow;

                    HttpResult r;
                    try
                    {
                        using var req = new HttpRequestMessage(HttpMethod.Get, BaseUrl + pathAndQuery);
                        req.Headers.TryAddWithoutValidation("Accept", "application/json");
                        req.Headers.TryAddWithoutValidation("User-Agent", HttpUtil.ChromeUa);
                        req.Headers.TryAddWithoutValidation("Origin", "https://manhastro.net");
                        req.Headers.TryAddWithoutValidation("Referer", "https://manhastro.net/");

                        using var cts = new CancellationTokenSource(20000);
                        using var res = await _client.SendAsync(req, HttpCompletionOption.ResponseContentRead, cts.Token)
                            .ConfigureAwait(false);
                        var body = await res.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
                        r = new HttpResult { Status = (int)res.StatusCode, Raw = body };
                    }
                    catch (Exception e)
                    {
                        r = new HttpResult { Status = 0, Error = e.Message };
                    }

                    lastStatus = r.Status;

                    if (Retryable.Contains(r.Status) || r.Status == 0)
                    {
                        var backoff = r.Status == 429 ? 4000 * (attempt + 1) : 1500 * (attempt + 1) + 500;
                        _lastAt = DateTime.UtcNow.AddMilliseconds(backoff);
                        if (attempt < tries - 1)
                        {
                            await Task.Delay(backoff).ConfigureAwait(false);
                            continue;
                        }
                        if (r.Status == 0)
                            throw new Exception("Sem conexão com o Manhastro.");
                        throw new Exception(
                            "Manhastro indisponível no momento (" + r.Status + "). Tente de novo em alguns segundos.");
                    }

                    JsonDocument doc;
                    try
                    {
                        doc = JsonDocument.Parse(r.Raw ?? string.Empty);
                    }
                    catch
                    {
                        throw new Exception("Resposta inválida do Manhastro.");
                    }

                    using (doc)
                    {
                        var root = doc.RootElement;
                        if (r.Status >= 400 ||
                            (root.ValueKind == JsonValueKind.Object &&
                             root.TryGetProperty("success", out var s) && s.ValueKind == JsonValueKind.False))
                        {
                            var msg = "Falha no Manhastro (" + r.Status + ").";
                            if (root.ValueKind == JsonValueKind.Object)
                            {
                                if (root.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String) msg = e.GetString();
                                else if (root.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String) msg = m.GetString();
                            }
                            throw new Exception(msg);
                        }
                        return root.Clone();
                    }
                }

                throw new Exception("Manhastro indisponível no momento (" +
                                    (lastStatus == 0 ? "sem resposta" : lastStatus.ToString()) +
                                    "). Tente de novo em alguns segundos.");
            }
            finally
            {
                _gate.Release();
            }
        }

        private async Task<T> CachedAsync<T>(string key, TimeSpan ttl, Func<Task<T>> fn)
        {
            lock (_cacheGate)
            {
                if (_cache.TryGetValue(key, out var hit) && DateTime.UtcNow - hit.At < ttl)
                    return (T)hit.Value;
            }

            try
            {
                var value = await fn().ConfigureAwait(false);
                lock (_cacheGate)
                {
                    if (!_cache.ContainsKey(key)) _cacheOrder.Add(key);
                    _cache[key] = new CacheEntry { At = DateTime.UtcNow, Value = value };
                    while (_cacheOrder.Count > 220)
                    {
                        var oldest = _cacheOrder[0];
                        _cacheOrder.RemoveAt(0);
                        _cache.Remove(oldest);
                    }
                }
                return value;
            }
            catch
            {
                // se ja temos uma copia (mesmo vencida), usa em vez de falhar
                lock (_cacheGate)
                {
                    if (_cache.TryGetValue(key, out var stale)) return (T)stale.Value;
                }
                throw;
            }
        }

        // ------------------------------------------------------------------
        // Normalizacao
        // ------------------------------------------------------------------
        private static string JStr(JsonElement o, string name)
        {
            if (o.ValueKind != JsonValueKind.Object) return null;
            if (!o.TryGetProperty(name, out var v)) return null;
            switch (v.ValueKind)
            {
                case JsonValueKind.String: return v.GetString();
                case JsonValueKind.Number: return v.ToString();
                case JsonValueKind.True: return "true";
                case JsonValueKind.False: return "false";
                default: return null;
            }
        }

        private static string FirstNonEmpty(params string[] values) =>
            values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

        private static long JLong(JsonElement o, string name, long def = 0)
        {
            var s = JStr(o, name);
            if (s != null && long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)) return n;
            return def;
        }

        private static bool JBool(JsonElement o, string name)
        {
            if (o.ValueKind != JsonValueKind.Object || !o.TryGetProperty(name, out var v)) return false;
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

        private static string Cover(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return string.Empty;
            var u = url.Trim();
            if (u.StartsWith("//")) u = "https:" + u;
            else if (!Regex.IsMatch(u, "^https?://", RegexOptions.IgnoreCase))
                u = "https://" + u.TrimStart('/');
            u = ManhastroHostRe.Replace(u, "//$1manhastro.net");
            return u;
        }

        private static Dictionary<string, object> Item(JsonElement raw)
        {
            if (raw.ValueKind != JsonValueKind.Object || !raw.TryGetProperty("manga_id", out _)) return null;

            var title = FirstNonEmpty(JStr(raw, "titulo"), JStr(raw, "titulo_brasil"), "Sem titulo");
            var titlePt = FirstNonEmpty(JStr(raw, "titulo_brasil"), JStr(raw, "titulo"));
            var titleOrig = JStr(raw, "titulo") ?? string.Empty;

            var genres = new List<string>();
            if (raw.TryGetProperty("generos", out var g) && g.ValueKind == JsonValueKind.Array)
            {
                foreach (var x in g.EnumerateArray())
                    if (x.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(x.GetString()))
                        genres.Add(x.GetString());
            }

            long? views = null;
            if (raw.TryGetProperty("views_mes", out var vw) && vw.ValueKind != JsonValueKind.Null)
            {
                if (vw.ValueKind == JsonValueKind.Number && vw.TryGetInt64(out var vn)) views = vn;
                else if (vw.ValueKind == JsonValueKind.String && long.TryParse(vw.GetString(), out var vs)) views = vs;
            }

            return new Dictionary<string, object>
            {
                ["id"] = JLong(raw, "manga_id"),
                ["title"] = title.Trim(),
                ["title_pt"] = (titlePt ?? string.Empty).Trim(),
                ["title_orig"] = titleOrig.Trim(),
                ["description"] = FirstNonEmpty(JStr(raw, "descricao_brasil"), JStr(raw, "descricao"), string.Empty),
                ["description_pt"] = (JStr(raw, "descricao_brasil") ?? string.Empty).Trim(),
                ["cover"] = Cover(FirstNonEmpty(JStr(raw, "imagem"), JStr(raw, "cover_pro"), JStr(raw, "cover_desktop"), "")),
                ["adult"] = JBool(raw, "adulta"),
                ["chapters"] = JLong(raw, "qnt_capitulo"),
                ["latest"] = JStr(raw, "ultimo_capitulo") ?? string.Empty,
                ["status"] = JStr(raw, "status") ?? string.Empty,
                ["genres"] = genres,
                ["views"] = views.HasValue ? (object)views.Value : null,
            };
        }

        // ------------------------------------------------------------------
        // Endpoints
        // ------------------------------------------------------------------
        public Task<Dictionary<string, object>> CatalogAsync(
            int page = 1, int limit = 24, string q = "", bool adult = false,
            string sort = "ultimo_capitulo", string order = "desc", string genre = "")
        {
            page = Math.Max(1, page);
            limit = Math.Min(60, Math.Max(1, limit));

            var qs = "page=" + page.ToString(CultureInfo.InvariantCulture) +
                     "&limit=" + limit.ToString(CultureInfo.InvariantCulture);
            if (!string.IsNullOrWhiteSpace(q)) qs += "&q=" + Uri.EscapeDataString(q);
            if (!string.IsNullOrWhiteSpace(genre)) qs += "&genero=" + Uri.EscapeDataString(genre.Trim());
            if (!string.IsNullOrWhiteSpace(sort)) qs += "&sort=" + Uri.EscapeDataString(sort);
            if (!string.IsNullOrWhiteSpace(order)) qs += "&order=" + Uri.EscapeDataString(order);

            var url = (adult ? "/adulta" : string.Empty) + "/dados?" + qs;
            var key = "cat:" + url;

            return CachedAsync(key, TimeSpan.FromMinutes(4), async () =>
            {
                var j = await GetJsonAsync(url).ConfigureAwait(false);
                var items = new List<Dictionary<string, object>>();

                if (j.ValueKind == JsonValueKind.Object &&
                    j.TryGetProperty("data", out var arr) && arr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var x in arr.EnumerateArray())
                    {
                        var it = Item(x);
                        if (it != null) items.Add(it);
                    }
                }

                long currentPage = page, lastPage = 1, total = 0;
                var hasMore = false;

                if (j.ValueKind == JsonValueKind.Object && j.TryGetProperty("meta", out var meta) &&
                    meta.ValueKind == JsonValueKind.Object)
                {
                    currentPage = JLong(meta, "current_page", page);
                    lastPage = JLong(meta, "last_page", 1);
                    total = JLong(meta, "total", 0);
                    hasMore = JBool(meta, "has_more") || currentPage < lastPage;
                }

                return new Dictionary<string, object>
                {
                    ["items"] = items,
                    ["page"] = currentPage,
                    ["hasMore"] = hasMore,
                    ["total"] = total,
                };
            });
        }

        public Task<Dictionary<string, object>> MangaAsync(long id, bool adult = false)
        {
            if (id <= 0) throw new Exception("Obra inválida.");
            return CachedAsync("manga:" + id, TimeSpan.FromMinutes(10), async () =>
            {
                // Obras +18 só aparecem no escopo /adulta; as demais no escopo raiz.
                // Tenta o escopo pedido e cai no outro antes de desistir, para que
                // favoritos/histórico abram a obra independente do filtro atual.
                var scopes = adult ? new[] { "/adulta", string.Empty }
                                   : new[] { string.Empty, "/adulta" };

                foreach (var scope in scopes)
                {
                    var j = await GetJsonAsync(scope + "/dados?manga_id=" +
                                               id.ToString(CultureInfo.InvariantCulture) +
                                               "&page=1&limit=1").ConfigureAwait(false);

                    if (j.ValueKind != JsonValueKind.Object ||
                        !j.TryGetProperty("data", out var arr) ||
                        arr.ValueKind != JsonValueKind.Array) continue;

                    JsonElement? chosen = null;
                    foreach (var x in arr.EnumerateArray())
                    {
                        if (chosen == null) chosen = x;
                        if (x.ValueKind == JsonValueKind.Object && JLong(x, "manga_id") == id)
                        {
                            chosen = x;
                            break;
                        }
                    }
                    if (chosen.HasValue)
                    {
                        var it = Item(chosen.Value);
                        if (it != null) return it;
                    }
                }
                throw new Exception("Obra não encontrada.");
            });
        }

        public Task<List<Dictionary<string, object>>> ChaptersAsync(long id)
        {
            if (id <= 0) throw new Exception("Obra inválida.");
            return CachedAsync("ch:" + id, TimeSpan.FromMinutes(5), async () =>
            {
                var j = await GetJsonAsync("/dados/" + id.ToString(CultureInfo.InvariantCulture)).ConfigureAwait(false);
                var list = new List<Dictionary<string, object>>();

                if (j.ValueKind == JsonValueKind.Object &&
                    j.TryGetProperty("data", out var arr) && arr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var c in arr.EnumerateArray())
                    {
                        if (c.ValueKind != JsonValueKind.Object) continue;
                        if (!c.TryGetProperty("capitulo_id", out _)) continue;
                        list.Add(new Dictionary<string, object>
                        {
                            ["id"] = JLong(c, "capitulo_id"),
                            ["title"] = (JStr(c, "capitulo_nome") ?? "Capitulo").Trim(),
                            ["date"] = JStr(c, "capitulo_data") ?? string.Empty,
                        });
                    }
                }
                return list;
            });
        }

        public Task<Dictionary<string, object>> PagesAsync(long chapterId, bool adult = false)
        {
            if (chapterId <= 0) throw new Exception("Capítulo inválido.");
            return CachedAsync("pg:" + chapterId, TimeSpan.FromMinutes(15), async () =>
            {
                // Capítulos +18 só existem em /adulta/paginas/{id}. Começa pelo
                // escopo indicado e usa o outro como fallback.
                JsonElement j;
                var scopes = adult ? new[] { "/adulta", string.Empty }
                                   : new[] { string.Empty, "/adulta" };
                Exception last = null;
                var ok = false;
                j = default;
                foreach (var scope in scopes)
                {
                    try
                    {
                        j = await GetJsonAsync(scope + "/paginas/" +
                                               chapterId.ToString(CultureInfo.InvariantCulture))
                            .ConfigureAwait(false);
                        ok = true;
                        break;
                    }
                    catch (Exception ex)
                    {
                        last = ex;
                    }
                }
                if (!ok)
                    throw last ?? new Exception("Capítulo indisponível no momento.");
                var data = j.ValueKind == JsonValueKind.Object && j.TryGetProperty("data", out var d)
                    ? d
                    : default;

                var images = new List<string>();
                var text = string.Empty;

                var isText = data.ValueKind == JsonValueKind.Object &&
                             data.TryGetProperty("text", out var tx) && tx.ValueKind == JsonValueKind.True;

                var ch = data.ValueKind == JsonValueKind.Object && data.TryGetProperty("chapter", out var c)
                    ? c
                    : default;

                if (isText)
                {
                    if (ch.ValueKind == JsonValueKind.Object)
                    {
                        if (ch.TryGetProperty("text", out var tv) && tv.ValueKind == JsonValueKind.String)
                        {
                            text = tv.GetString();
                        }
                        else if (ch.TryGetProperty("data", out var dv) && dv.ValueKind == JsonValueKind.Array)
                        {
                            var parts = new List<string>();
                            foreach (var p in dv.EnumerateArray())
                                if (p.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(p.GetString()))
                                    parts.Add(p.GetString());
                            text = string.Join("\n\n", parts);
                        }
                    }
                    return new Dictionary<string, object> { ["text"] = text ?? string.Empty, ["images"] = images };
                }

                var baseUrl = ch.ValueKind == JsonValueKind.Object ? (JStr(ch, "baseUrl") ?? string.Empty) : string.Empty;
                var hash = ch.ValueKind == JsonValueKind.Object ? (JStr(ch, "hash") ?? string.Empty) : string.Empty;

                if (ch.ValueKind == JsonValueKind.Object && ch.TryGetProperty("data", out var files) &&
                    files.ValueKind == JsonValueKind.Array)
                {
                    foreach (var f in files.EnumerateArray())
                    {
                        if (f.ValueKind != JsonValueKind.String) continue;
                        var name = f.GetString();
                        if (string.IsNullOrWhiteSpace(name)) continue;
                        if (Regex.IsMatch(name, "^https?://", RegexOptions.IgnoreCase)) images.Add(name);
                        else images.Add(baseUrl + "/" + hash + "/" + name.TrimStart('/'));
                    }
                }

                if (images.Count == 0) throw new Exception("Capítulo sem páginas.");
                return new Dictionary<string, object> { ["text"] = string.Empty, ["images"] = images };
            });
        }
    }
}
