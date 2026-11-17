using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SoloMangas.Shared
{
    public sealed class HttpResult
    {
        public int Status;
        public string Raw = string.Empty;
        public string Location;
        public string Error;
    }

    /// <summary>Cliente HTTP compartilhado (sem redirect automatico: o OAuth usa o Location).</summary>
    public static class HttpUtil
    {
        public const string ChromeUa =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";

        public const string ApiUa = "okhttp/4.12.0";

        private static readonly HttpClient Client = CreateClient();

        private static HttpClient CreateClient()
        {
            var handler = new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.All,
                UseCookies = false,
                ConnectTimeout = TimeSpan.FromSeconds(15),
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                Expect100ContinueTimeout = TimeSpan.Zero,
            };
            return new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        }

        public static async Task<HttpResult> RequestAsync(
            string url,
            string method = "GET",
            string body = null,
            Dictionary<string, string> headers = null,
            int timeoutMs = 30000,
            string userAgent = ApiUa)
        {
            try
            {
                using var req = new HttpRequestMessage(new HttpMethod(method), url);
                req.Headers.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
                req.Headers.TryAddWithoutValidation("User-Agent", userAgent ?? ApiUa);

                if (headers != null)
                {
                    foreach (var kv in headers)
                    {
                        if (string.IsNullOrEmpty(kv.Key)) continue;
                        req.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
                    }
                }

                if (body != null)
                {
                    var content = new ByteArrayContent(Encoding.UTF8.GetBytes(body));
                    content.Headers.TryAddWithoutValidation("Content-Type", "application/json");
                    req.Content = content;
                }

                using var cts = new CancellationTokenSource(Math.Max(1000, timeoutMs));
                using var res = await Client
                    .SendAsync(req, HttpCompletionOption.ResponseContentRead, cts.Token)
                    .ConfigureAwait(false);

                var raw = await res.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);

                var location = res.Headers.Location?.ToString();
                if (location == null && res.Headers.TryGetValues("Location", out var locs))
                    foreach (var l in locs) { location = l; break; }

                return new HttpResult
                {
                    Status = (int)res.StatusCode,
                    Raw = raw ?? string.Empty,
                    Location = location,
                };
            }
            catch (OperationCanceledException)
            {
                return new HttpResult { Status = 0, Error = "Tempo esgotado." };
            }
            catch (Exception e)
            {
                return new HttpResult { Status = 0, Error = e.Message };
            }
        }
    }
}
