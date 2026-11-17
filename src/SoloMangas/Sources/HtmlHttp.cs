using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using SoloMangas.Shared;

namespace SoloMangasNative
{
    /// <summary>
    /// Cliente simples para scraping HTML das fontes +18. Usa UA de navegador,
    /// segue redirecionamentos e aceita gzip/deflate/br. Sem cookies: as fontes
    /// escolhidas nao exigem login.
    /// </summary>
    internal static class HtmlHttp
    {
        private static readonly HttpClient Client = Create();

        private static HttpClient Create()
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
            var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", HttpUtil.ChromeUa);
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "pt-BR,pt;q=0.9,en;q=0.7");
            return client;
        }

        public static async Task<string> GetAsync(string url, string referer = null, int timeoutMs = 30000)
        {
            Exception last = null;
            for (var attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    using var cts = new CancellationTokenSource(Math.Max(5000, timeoutMs));
                    using var req = new HttpRequestMessage(HttpMethod.Get, url);
                    req.Headers.TryAddWithoutValidation("Accept",
                        "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8");
                    if (!string.IsNullOrWhiteSpace(referer))
                        req.Headers.TryAddWithoutValidation("Referer", referer);

                    using var res = await Client
                        .SendAsync(req, HttpCompletionOption.ResponseContentRead, cts.Token)
                        .ConfigureAwait(false);
                    var html = await res.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
                    if (res.IsSuccessStatusCode) return html ?? string.Empty;

                    last = new Exception("HTTP " + (int)res.StatusCode + " em " + url);
                    if ((int)res.StatusCode < 500) break;
                }
                catch (OperationCanceledException)
                {
                    last = new Exception("Tempo esgotado ao abrir " + url);
                }
                catch (Exception e)
                {
                    last = e;
                }

                if (attempt == 0) await Task.Delay(350).ConfigureAwait(false);
            }

            throw new Exception(last?.Message ?? ("Falha ao abrir " + url));
        }
    }
}
