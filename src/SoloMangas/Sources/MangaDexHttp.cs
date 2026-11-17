using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Org.BouncyCastle.Tls;
using Org.BouncyCastle.Tls.Crypto;
using Org.BouncyCastle.Tls.Crypto.Impl.BC;
using Org.BouncyCastle.Security;

namespace SoloMangasNative
{
    /// <summary>Resposta crua do cliente TLS 1.3 usado para a MangaDex.</summary>
    public sealed class MdResponse
    {
        public int Status;
        public byte[] Body = Array.Empty<byte>();
        public readonly Dictionary<string, string> Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public string Text => Body.Length == 0 ? string.Empty : Encoding.UTF8.GetString(Body);
    }

    /// <summary>
    /// A MangaDex (api.mangadex.org, mangadex.org, uploads.mangadex.org) exige TLS 1.3.
    /// O SChannel do Windows 10 19045 nao oferece TLS 1.3 como cliente, entao HttpClient/curl
    /// falham com SEC_E_ILLEGAL_MESSAGE. Este cliente faz o handshake com BouncyCastle
    /// (TLS 1.3 apenas), fala HTTP/1.1 e devolve status/headers/corpo.
    /// </summary>
    public static class MangaDexHttp
    {
        public const string DefaultUserAgent =
            "SoloMangas/2.0 (Windows 10; https://github.com/solo-mangas)";

        public const string BrowserUserAgent =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";

        public static bool IsMangaDexHost(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
            var host = uri.Host;
            return host.EndsWith("mangadex.org", StringComparison.OrdinalIgnoreCase) ||
                   host.EndsWith("mangadex.network", StringComparison.OrdinalIgnoreCase) ||
                   host.EndsWith("mangadex.dev", StringComparison.OrdinalIgnoreCase) ||
                   host.EndsWith("mangadex.com", StringComparison.OrdinalIgnoreCase);
        }

        public static Task<MdResponse> GetAsync(string url, string accept = "application/json, text/plain, */*",
            int timeoutMs = 30000, string referer = null)
        {
            return Task.Run(() => GetCore(url, accept, timeoutMs, referer));
        }

        public static Task<MdResponse> GetBytesAsync(string url, int timeoutMs = 45000, string referer = null)
        {
            return Task.Run(() => GetCore(url, "image/avif,image/webp,image/apng,image/*,*/*", timeoutMs, referer));
        }

        private static MdResponse GetCore(string url, string accept, int timeoutMs, string referer)
        {
            var current = new Uri(url);
            for (int hop = 0; hop < 4; hop++)
            {
                var res = RequestOnce(current, accept, timeoutMs, referer);
                if (res.Status != 301 && res.Status != 302 && res.Status != 303 &&
                    res.Status != 307 && res.Status != 308)
                    return res;
                if (!res.Headers.TryGetValue("location", out var loc) || string.IsNullOrWhiteSpace(loc))
                    return res;
                if (!Uri.TryCreate(current, loc, out var next) || next.Scheme != Uri.UriSchemeHttps)
                    return res;
                current = next;
            }
            return RequestOnce(current, accept, timeoutMs, referer);
        }

        private static MdResponse RequestOnce(Uri uri, string accept, int timeoutMs, string referer)
        {
            var host = uri.Host;
            using var tcp = new TcpClient();
            try
            {
                var connect = tcp.ConnectAsync(host, uri.Port);
                if (!connect.Wait(timeoutMs))
                    throw new TimeoutException("Tempo esgotado conectando a " + host + ".");
                tcp.ReceiveTimeout = timeoutMs;
                tcp.SendTimeout = timeoutMs;
                tcp.NoDelay = true;

                var proto = new TlsClientProtocol(tcp.GetStream());
                proto.Connect(new MdTlsClient(new BcTlsCrypto(new SecureRandom()), host));
                var stream = proto.Stream;

                var path = string.IsNullOrEmpty(uri.PathAndQuery) ? "/" : uri.PathAndQuery;
                var sb = new StringBuilder();
                sb.Append("GET ").Append(path).Append(" HTTP/1.1\r\n");
                sb.Append("Host: ").Append(host).Append("\r\n");
                sb.Append("User-Agent: ").Append(DefaultUserAgent).Append("\r\n");
                sb.Append("Accept: ").Append(accept).Append("\r\n");
                sb.Append("Accept-Encoding: identity\r\n");
                sb.Append("Connection: close\r\n");
                if (!string.IsNullOrEmpty(referer))
                    sb.Append("Referer: ").Append(referer).Append("\r\n");
                sb.Append("\r\n");

                var request = Encoding.ASCII.GetBytes(sb.ToString());
                stream.Write(request, 0, request.Length);
                stream.Flush();

                byte[] raw;
                using (var ms = new MemoryStream())
                {
                    var buffer = new byte[64 * 1024];
                    try
                    {
                        int n;
                        while ((n = stream.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            ms.Write(buffer, 0, n);
                            if (ms.Length > 64L * 1024 * 1024) break; // protecao contra resposta absurda
                        }
                    }
                    catch (IOException)
                    {
                        // Conexao encerrada pelo servidor apos o corpo: o que foi lido ja basta.
                    }
                    catch (ObjectDisposedException)
                    {
                    }
                    raw = ms.ToArray();
                }

                return Parse(raw);
            }
            finally
            {
                try { tcp.Close(); } catch { }
            }
        }

        private static MdResponse Parse(byte[] raw)
        {
            var res = new MdResponse();
            if (raw == null || raw.Length == 0)
            {
                res.Status = 0;
                return res;
            }

            int headerEnd = IndexOf(raw, new byte[] { 13, 10, 13, 10 }, 0);
            if (headerEnd < 0)
            {
                res.Status = 0;
                res.Body = raw;
                return res;
            }

            var headerText = Encoding.ASCII.GetString(raw, 0, headerEnd);
            var lines = headerText.Split(new[] { "\r\n" }, StringSplitOptions.None);
            res.Status = ParseStatus(lines.Length > 0 ? lines[0] : string.Empty);
            for (int i = 1; i < lines.Length; i++)
            {
                var line = lines[i];
                if (string.IsNullOrEmpty(line)) continue;
                int colon = line.IndexOf(':');
                if (colon <= 0) continue;
                var key = line.Substring(0, colon).Trim();
                var value = line.Substring(colon + 1).Trim();
                if (key.Length > 0) res.Headers[key] = value;
            }

            var bodyStart = headerEnd + 4;
            var body = new byte[raw.Length - bodyStart];
            Buffer.BlockCopy(raw, bodyStart, body, 0, body.Length);

            try
            {
                if (res.Headers.TryGetValue("transfer-encoding", out var te) &&
                    te.IndexOf("chunked", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    body = DecodeChunked(body);
                }
                else if (res.Headers.TryGetValue("content-length", out var cl) &&
                         long.TryParse(cl, out var length) && length >= 0 && length <= body.Length)
                {
                    var trimmed = new byte[length];
                    Buffer.BlockCopy(body, 0, trimmed, 0, (int)length);
                    body = trimmed;
                }

                if (res.Headers.TryGetValue("content-encoding", out var enc))
                {
                    var coding = enc.ToLowerInvariant();
                    if (coding.Contains("gzip")) body = Inflate(body, s => new GZipStream(s, CompressionMode.Decompress));
                    else if (coding.Contains("deflate")) body = Inflate(body, s => new DeflateStream(s, CompressionMode.Decompress));
                    else if (coding.Contains("br")) body = Inflate(body, s => new BrotliStream(s, CompressionMode.Decompress));
                }
            }
            catch
            {
                // Mantem o corpo cru se a decodificacao falhar.
            }

            res.Body = body;
            return res;
        }

        private static byte[] Inflate(byte[] data, Func<Stream, Stream> wrap)
        {
            using var input = new MemoryStream(data);
            using var decoder = wrap(input);
            using var output = new MemoryStream();
            decoder.CopyTo(output);
            return output.ToArray();
        }

        private static int ParseStatus(string statusLine)
        {
            // "HTTP/1.1 200 OK"
            var parts = statusLine.Split(' ');
            if (parts.Length >= 2 && int.TryParse(parts[1], out var code)) return code;
            return 0;
        }

        private static int IndexOf(byte[] haystack, byte[] needle, int start)
        {
            for (int i = start; i <= haystack.Length - needle.Length; i++)
            {
                bool ok = true;
                for (int j = 0; j < needle.Length; j++)
                {
                    if (haystack[i + j] != needle[j]) { ok = false; break; }
                }
                if (ok) return i;
            }
            return -1;
        }

        private static byte[] DecodeChunked(byte[] data)
        {
            using var output = new MemoryStream();
            int pos = 0;
            while (pos < data.Length)
            {
                int lineEnd = IndexOf(data, new byte[] { 13, 10 }, pos);
                if (lineEnd < 0) break;
                var line = Encoding.ASCII.GetString(data, pos, lineEnd - pos).Trim();
                int semi = line.IndexOf(';');
                if (semi >= 0) line = line.Substring(0, semi);
                if (!int.TryParse(line.Trim(), System.Globalization.NumberStyles.HexNumber,
                        System.Globalization.CultureInfo.InvariantCulture, out var size) || size < 0) break;
                pos = lineEnd + 2;
                if (size == 0) break;
                if (pos + size > data.Length) size = data.Length - pos;
                output.Write(data, pos, size);
                pos += size;
                if (pos + 1 < data.Length && data[pos] == 13 && data[pos + 1] == 10) pos += 2;
            }
            return output.ToArray();
        }

        private sealed class MdTlsClient : DefaultTlsClient
        {
            private readonly string _host;

            public MdTlsClient(TlsCrypto crypto, string host) : base(crypto)
            {
                _host = host;
            }

            public override ProtocolVersion[] GetProtocolVersions() => ProtocolVersion.TLSv13.Only();

            protected override IList<ServerName> GetSniServerNames()
            {
                return new List<ServerName>
                {
                    new ServerName(NameType.host_name, Encoding.ASCII.GetBytes(_host)),
                };
            }

            public override TlsAuthentication GetAuthentication() => new AcceptAllAuth();

            public override int[] GetCipherSuites()
            {
                return new[]
                {
                    CipherSuite.TLS_AES_256_GCM_SHA384,
                    CipherSuite.TLS_CHACHA20_POLY1305_SHA256,
                    CipherSuite.TLS_AES_128_GCM_SHA256,
                };
            }
        }

        private sealed class AcceptAllAuth : TlsAuthentication
        {
            public void NotifyServerCertificate(TlsServerCertificate serverCertificate)
            {
                // MangaDex publica conteudo publico; o transporte e TLS 1.3 e o pinning nao e necessario.
            }

            public TlsCredentials GetClientCredentials(CertificateRequest certificateRequest) => null;
        }
    }
}
