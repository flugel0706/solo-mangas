using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SoloMangas.Shared
{
    public sealed class ApiResult
    {
        public int Status;
        public JsonElement? Data;
        public string Raw = string.Empty;
        public string Error;

        public static ApiResult FromHttp(HttpResult res)
        {
            var r = new ApiResult { Status = res.Status, Raw = res.Raw ?? string.Empty, Error = res.Error };
            if (!string.IsNullOrEmpty(res.Raw))
            {
                try
                {
                    using var doc = JsonDocument.Parse(res.Raw);
                    r.Data = doc.RootElement.Clone();
                }
                catch
                {
                    r.Data = null;
                }
            }
            return r;
        }
    }

    /// <summary>
    /// API do Mangeek (engenharia reversa do app Android 1.7.5).
    /// Assinatura: nonce crescente em hex maiusculo + md5("M&lt;" + seed + "#MANG33K&gt;D").
    /// </summary>
    public sealed class MangeekApi
    {
        public const string ApiV2 = "http://geekstations.com.br/api/v2/pt";
        public const string ApiV1 = "http://geekstations.com.br/api/v1";

        private static readonly object NonceGate = new object();
        private static readonly object CacheGate = new object();
        private static long _lastNonceMs;

        private sealed class TagCacheEntry
        {
            public DateTime At;
            public JsonElement Value;
        }

        private readonly Dictionary<string, TagCacheEntry> _tagCache = new Dictionary<string, TagCacheEntry>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _tagCacheOrder = new List<string>();
        private readonly SessionStore _session;

        public MangeekApi(SessionStore session)
        {
            _session = session;
        }

        // ------------------------------------------------------------------
        // Assinatura
        // ------------------------------------------------------------------
        private static long NextNonceMs()
        {
            lock (NonceGate)
            {
                var t = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                if (t <= _lastNonceMs) t = _lastNonceMs + 1;
                _lastNonceMs = t;
                return t;
            }
        }

        private static string Nonce() => NextNonceMs().ToString("X", CultureInfo.InvariantCulture);

        public static string KeyGen(string seed)
        {
            using var md5 = MD5.Create();
            var bytes = md5.ComputeHash(Encoding.UTF8.GetBytes("M<" + seed + "#MANG33K>D"));
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        private static string BuildUrl(string route, string nonce, string key) =>
            ApiV2 + route + "/" + nonce + "/" + key;

        private Dictionary<string, string> AuthHeaders()
        {
            var h = new Dictionary<string, string>();
            if (!string.IsNullOrEmpty(_session.Token))
                h["Authorization"] = "Bearer " + _session.Token;
            return h;
        }

        // ------------------------------------------------------------------
        // Rotas
        // ------------------------------------------------------------------
        public async Task<ApiResult> SimpleAsync(string route, string method = "GET", object body = null, int timeoutMs = 30000)
        {
            var n = Nonce();
            var url = BuildUrl(route, n, KeyGen(n));
            string payload = null;
            if (body != null) payload = JsonSerializer.Serialize(body);
            var res = await HttpUtil.RequestAsync(url, method, payload, AuthHeaders(), timeoutMs).ConfigureAwait(false);
            return ApiResult.FromHttp(res);
        }

        public async Task<ApiResult> IdAsync(string route, object id, string method = "GET", object body = null)
        {
            var n = Nonce();
            var sid = Convert.ToString(id, CultureInfo.InvariantCulture);
            var url = ApiV2 + route + "/" + n + "/" + Uri.EscapeDataString(sid) + "/" + KeyGen(sid);
            string payload = null;
            if (body != null) payload = JsonSerializer.Serialize(body);
            var res = await HttpUtil.RequestAsync(url, method, payload, AuthHeaders(), 30000).ConfigureAwait(false);
            return ApiResult.FromHttp(res);
        }

        public async Task<ApiResult> SearchAsync(string query, IEnumerable<string> tags)
        {
            var q = (query ?? string.Empty).ToLowerInvariant();
            var n = Nonce();
            var url = ApiV2 + "/search/" + n + "/" + Uri.EscapeDataString(q) + "/" + KeyGen(q);
            var payload = JsonSerializer.Serialize(new { tags = (tags ?? Enumerable.Empty<string>()).ToArray() });
            var res = await HttpUtil.RequestAsync(url, "POST", payload, AuthHeaders(), 30000).ConfigureAwait(false);
            return ApiResult.FromHttp(res);
        }

        public async Task<ApiResult> DiscoverAsync(IEnumerable<string> tags, IEnumerable<string> ignore)
        {
            var n = Nonce();
            var url = BuildUrl("/discover", n, KeyGen(n));
            var payload = JsonSerializer.Serialize(new
            {
                tags = (tags ?? Enumerable.Empty<string>()).ToArray(),
                ignore = (ignore ?? Enumerable.Empty<string>()).ToArray(),
            });
            var res = await HttpUtil.RequestAsync(url, "POST", payload, AuthHeaders(), 30000).ConfigureAwait(false);
            return ApiResult.FromHttp(res);
        }

        /// <summary>
        /// Lista COMPLETA de obras de uma tag. /discover devolve no maximo 25 itens e
        /// nao pagina; /search com query " " + a tag devolve o catalogo inteiro da tag.
        /// </summary>
        public async Task<ApiResult> TagAllAsync(string tag)
        {
            var t = (tag ?? string.Empty).Trim();
            if (t.Length == 0)
                return new ApiResult { Status = 400, Raw = "{\"message\":\"Tag invalida.\"}" };

            lock (CacheGate)
            {
                if (_tagCache.TryGetValue(t, out var hit) && DateTime.UtcNow - hit.At < TimeSpan.FromMinutes(10))
                    return new ApiResult { Status = 200, Data = hit.Value };
            }

            var q = " ";
            var n = Nonce();
            var url = ApiV2 + "/search/" + n + "/" + Uri.EscapeDataString(q) + "/" + KeyGen(q);
            var payload = JsonSerializer.Serialize(new { tags = new[] { t } });
            var res = await HttpUtil.RequestAsync(url, "POST", payload, AuthHeaders(), 60000).ConfigureAwait(false);
            if (res.Status < 200 || res.Status >= 300)
                return new ApiResult { Status = res.Status == 0 ? 0 : res.Status, Raw = res.Raw, Error = res.Error };

            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(res.Raw ?? string.Empty);
            }
            catch
            {
                return new ApiResult { Status = res.Status == 0 ? 500 : res.Status, Raw = res.Raw };
            }

            using (doc)
            {
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Array)
                    return new ApiResult { Status = res.Status == 0 ? 500 : res.Status, Data = root.Clone() };

                var tags = new HashSet<string>(StringComparer.Ordinal);
                var items = new List<JsonElement>(root.GetArrayLength());
                foreach (var m in root.EnumerateArray())
                {
                    items.Add(m.Clone());
                    if (m.ValueKind == JsonValueKind.Object &&
                        m.TryGetProperty("tags", out var tl) && tl.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var x in tl.EnumerateArray())
                            if (x.ValueKind == JsonValueKind.String) tags.Add(x.GetString());
                    }
                }

                var sorted = tags.ToList();
                sorted.Sort(StringComparer.Create(new CultureInfo("pt-BR"), true));

                var valueObj = new Dictionary<string, object>
                {
                    ["items"] = items,
                    ["tags"] = sorted,
                };
                var json = JsonSerializer.Serialize(valueObj);
                using var built = JsonDocument.Parse(json);
                var value = built.RootElement.Clone();

                lock (CacheGate)
                {
                    if (!_tagCache.ContainsKey(t)) _tagCacheOrder.Add(t);
                    _tagCache[t] = new TagCacheEntry { At = DateTime.UtcNow, Value = value };
                    while (_tagCacheOrder.Count > 40)
                    {
                        var oldest = _tagCacheOrder[0];
                        _tagCacheOrder.RemoveAt(0);
                        _tagCache.Remove(oldest);
                    }
                }

                return new ApiResult { Status = 200, Data = value };
            }
        }

        public async Task<ApiResult> TagsUniverseAsync()
        {
            var r = await TagAllAsync("Ação").ConfigureAwait(false);
            if (r.Status != 200 || !r.Data.HasValue)
                return new ApiResult { Status = r.Status == 0 ? 500 : r.Status, Raw = r.Raw, Error = r.Error };

            var d = r.Data.Value;
            if (d.TryGetProperty("tags", out var tags))
                return new ApiResult { Status = 200, Data = tags.Clone() };
            return new ApiResult { Status = 200, Data = JsonDocument.Parse("[]").RootElement.Clone() };
        }

        // ------------------------------------------------------------------
        // Login / OAuth
        // ------------------------------------------------------------------
        private static readonly Regex MangeekScheme =
            new Regex(@"mangeek://oauth/google/([^\s&""']+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex JwtRe =
            new Regex(@"(eyJ[A-Za-z0-9_-]+\.eyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+)", RegexOptions.Compiled);

        private static readonly Regex GoogleCodeRe =
            new Regex(@"^4/[A-Za-z0-9_\-]{20,}", RegexOptions.Compiled);

        public static string ExtractJwt(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var t = text.Trim();
            var m = MangeekScheme.Match(t);
            if (m.Success) return Uri.UnescapeDataString(m.Groups[1].Value);
            var j = JwtRe.Match(t);
            if (j.Success) return j.Groups[1].Value;
            return null;
        }

        public static long JwtExp(string token)
        {
            try
            {
                if (string.IsNullOrEmpty(token)) return 0;
                var parts = token.Split('.');
                if (parts.Length < 2) return 0;
                var p = parts[1].Replace('-', '+').Replace('_', '/');
                switch (p.Length % 4)
                {
                    case 2: p += "=="; break;
                    case 3: p += "="; break;
                }
                var json = Encoding.UTF8.GetString(Convert.FromBase64String(p));
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("exp", out var exp) && exp.ValueKind == JsonValueKind.Number)
                    return exp.GetInt64() * 1000;
            }
            catch { }
            return 0;
        }

        public async Task<string> ConsentUrlAsync()
        {
            var n = Nonce();
            var url = BuildUrl("/oauth/google", n, KeyGen(n));
            var res = await HttpUtil.RequestAsync(url, headers: AuthHeaders(), timeoutMs: 30000).ConfigureAwait(false);
            var text = (res.Raw ?? string.Empty).Trim().Trim('"');
            if (!text.StartsWith("https://accounts.google.com/", StringComparison.OrdinalIgnoreCase))
                throw new Exception("Não foi possível obter a URL do Google (HTTP " + res.Status + ").");
            return text;
        }

        /// <summary>
        /// Troca o `code` do Google pelo JWT. O host nao responde em 443, mas responde
        /// em HTTP porta 80 devolvendo Location: mangeek://oauth/google/&lt;jwt&gt;.
        /// </summary>
        public async Task<string> ExchangeCodeAsync(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) throw new Exception("Código do Google vazio.");
            var url = ApiV1 + "/oauth/google/callback?code=" + Uri.EscapeDataString(code);
            var res = await HttpUtil.RequestAsync(url, timeoutMs: 30000, userAgent: HttpUtil.ChromeUa).ConfigureAwait(false);

            if (!string.IsNullOrEmpty(res.Location))
            {
                var jwt = ExtractJwt(res.Location);
                if (!string.IsNullOrEmpty(jwt)) return jwt;
            }
            var body = res.Raw ?? string.Empty;
            var fromBody = ExtractJwt(body);
            if (!string.IsNullOrEmpty(fromBody)) return fromBody;

            var detail = body.Length > 120 ? body.Substring(0, 120) : body;
            throw new Exception("O servidor não devolveu a sessão (HTTP " + res.Status + "). " + detail);
        }

        public async Task<Dictionary<string, object>> LoginWithTokenAsync(string token)
        {
            if (string.IsNullOrWhiteSpace(token)) throw new Exception("Token vazio.");
            var exp = JwtExp(token);
            if (exp > 0 && exp < DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 60000)
                throw new Exception("Esse token já expirou. Entre de novo com o Google.");

            _session.Set(token, null);
            var me = await SimpleAsync("/me").ConfigureAwait(false);

            if (me.Status == 200 && me.Data.HasValue && me.Data.Value.ValueKind == JsonValueKind.Object)
            {
                var d = me.Data.Value;
                var hasId = d.TryGetProperty("id", out var idEl) && idEl.ValueKind != JsonValueKind.Null;
                if (hasId)
                {
                    var fresh = token;
                    if (d.TryGetProperty("token", out var t) && t.ValueKind == JsonValueKind.String)
                        fresh = t.GetString();
                    _session.Set(fresh, d);
                    return new Dictionary<string, object> { ["ok"] = true, ["user"] = d };
                }
            }

            return new Dictionary<string, object>
            {
                ["ok"] = true,
                ["user"] = null,
                ["warning"] = "Login salvo, mas não foi possível validar agora (HTTP " + me.Status + ").",
            };
        }

        public async Task<Dictionary<string, object>> LoginWithCodeAsync(string code)
        {
            var jwt = await ExchangeCodeAsync(code).ConfigureAwait(false);
            return await LoginWithTokenAsync(jwt).ConfigureAwait(false);
        }

        public async Task<Dictionary<string, object>> LoginSmartAsync(string text)
        {
            var t = (text ?? string.Empty).Trim();
            if (t.Length == 0) throw new Exception("Cole o token, a URL de retorno ou o código do Google.");

            var jwt = ExtractJwt(t);
            if (!string.IsNullOrEmpty(jwt))
                return await LoginWithTokenAsync(jwt).ConfigureAwait(false);

            string code = null;
            try
            {
                if (Uri.TryCreate(t, UriKind.Absolute, out var u))
                {
                    var q = u.Query.TrimStart('?').Split('&');
                    foreach (var pair in q)
                    {
                        var idx = pair.IndexOf('=');
                        if (idx <= 0) continue;
                        var k = Uri.UnescapeDataString(pair.Substring(0, idx));
                        if (k == "code")
                        {
                            code = Uri.UnescapeDataString(pair.Substring(idx + 1));
                            break;
                        }
                    }
                }
            }
            catch { }

            if (string.IsNullOrEmpty(code) && GoogleCodeRe.IsMatch(t)) code = t;
            if (!string.IsNullOrEmpty(code))
                return await LoginWithCodeAsync(code).ConfigureAwait(false);

            throw new Exception("Não encontrei um token nem um código na colagem.");
        }

        // ------------------------------------------------------------------
        // Envelope de resposta usado pelo renderer
        // ------------------------------------------------------------------
        public static Dictionary<string, object> Ok(JsonElement? data) =>
            new Dictionary<string, object> { ["ok"] = true, ["data"] = data };

        public static Dictionary<string, object> Wrap(ApiResult r, SessionStore session)
        {
            if (r.Status >= 200 && r.Status < 300)
            {
                var data = r.Data;
                if (data.HasValue && data.Value.ValueKind == JsonValueKind.Object)
                {
                    var d = data.Value;
                    if (d.TryGetProperty("token", out var t) && t.ValueKind == JsonValueKind.String &&
                        d.TryGetProperty("id", out var id) && id.ValueKind != JsonValueKind.Null &&
                        id.ValueKind != JsonValueKind.Undefined)
                    {
                        session.Set(t.GetString(), d);
                    }
                }
                return Ok(data);
            }

            if (r.Status == 401) session.Clear();
            return Fail(r);
        }

        public static Dictionary<string, object> Fail(ApiResult r)
        {
            var msg = "Falha na requisição";

            if (r.Data.HasValue && r.Data.Value.ValueKind == JsonValueKind.Object)
            {
                var d = r.Data.Value;
                if (d.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String)
                    msg = m.GetString();
                else if (d.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String)
                    msg = e.GetString();
            }
            else if (!string.IsNullOrEmpty(r.Raw))
            {
                msg = r.Raw.Length > 200 ? r.Raw.Substring(0, 200) : r.Raw;
            }

            if (r.Status == 401) msg = "Sessão expirada. Entre novamente.";
            if (r.Status == 0) msg = "Sem conexão com o servidor.";

            return new Dictionary<string, object>
            {
                ["ok"] = false,
                ["status"] = r.Status,
                ["error"] = msg,
                ["data"] = r.Data,
            };
        }
    }
}
