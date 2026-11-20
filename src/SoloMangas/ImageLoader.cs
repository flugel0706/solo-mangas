using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace SoloMangasNative
{
    /// <summary>
    /// Download, decodificação e cache (memória + disco) de capas e páginas.
    ///
    /// 2.7:
    /// - Os bytes originais são baixados UMA vez por URL e reaproveitados por
    ///   todos os tamanhos (hero 760, card 380, continuação 160...). Antes, a
    ///   mesma capa era baixada duas vezes porque a chave incluía a largura.
    /// - Um bitmap já decodificado em tamanho maior serve para exibir menor
    ///   (o WPF reduz no desenho), evitando decodificações redundantes.
    /// - Disco guarda os bytes originais, com teto de 1,2 GB e limpeza dos
    ///   arquivos mais antigos em segundo plano.
    /// - Download e decodificação têm limites separados: a rede não espera a
    ///   CPU e vice-versa.
    /// </summary>
    public static class ImageLoader
    {
        private static readonly HttpClient Client = Create();
        private static readonly SemaphoreSlim NetLimit = new SemaphoreSlim(12, 12);
        private static readonly SemaphoreSlim DecodeLimit = new SemaphoreSlim(6, 6);
        private static readonly SemaphoreSlim ConvertGate = new SemaphoreSlim(2, 2);

        // ---------------- Bitmaps decodificados (por URL + largura) ----------------
        private static readonly ConcurrentDictionary<string, BitmapImage> Mem =
            new ConcurrentDictionary<string, BitmapImage>(StringComparer.Ordinal);
        private static readonly ConcurrentDictionary<string, Task<BitmapImage>> InFlight =
            new ConcurrentDictionary<string, Task<BitmapImage>>(StringComparer.Ordinal);
        private static readonly LinkedList<string> Lru = new LinkedList<string>();
        private static readonly object LruGate = new object();
        private static readonly Dictionary<string, long> MemPixels = new Dictionary<string, long>(StringComparer.Ordinal);
        private static long _pixelTotal;
        private const int MemLimit = 360;
        private const long PixelBudget = 120_000_000L; // ~480 MB de bitmaps

        // ---------------- Bytes originais (por URL, sem largura) ----------------
        private static readonly ConcurrentDictionary<string, Task<byte[]>> ByteFlight =
            new ConcurrentDictionary<string, Task<byte[]>>(StringComparer.Ordinal);
        private static readonly Dictionary<string, byte[]> ByteCache = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        private static readonly LinkedList<string> ByteLru = new LinkedList<string>();
        private static readonly object ByteGate = new object();
        private static long _byteTotal;
        private const long ByteBudget = 96L * 1024 * 1024;
        private const int ByteLimit = 400;

        // ---------------- Conversões AVIF/HEIF (caras, ficam em memória) ----------------
        private static readonly Dictionary<string, byte[]> HeifCache = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        private static readonly LinkedList<string> HeifLru = new LinkedList<string>();
        private static readonly object HeifGate = new object();
        private static long _heifTotal;
        private const long HeifBudget = 20L * 1024 * 1024;

        private static readonly ConcurrentDictionary<string, string> HashCache =
            new ConcurrentDictionary<string, string>(StringComparer.Ordinal);

        private static int _diskTrimStarted;

        public static string CacheDir { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SoloMangas", "cache", "img");

        private static HttpClient Create()
        {
            var handler = new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.All,
                ConnectTimeout = TimeSpan.FromSeconds(12),
                PooledConnectionLifetime = TimeSpan.FromMinutes(10),
                MaxConnectionsPerServer = 16,
                AllowAutoRedirect = true,
            };
            var c = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
            c.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
            c.DefaultRequestHeaders.TryAddWithoutValidation("Accept",
                "image/webp,image/jpeg,image/png,image/apng,image/*;q=0.8,*/*;q=0.5");
            c.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "pt-BR,pt;q=0.9,en;q=0.8");
            return c;
        }

        /// <summary>
        /// Cópia já decodificada em memória. Se não houver no tamanho exato,
        /// aceita uma versão maior: o WPF reduz na hora e o card aparece sem
        /// esperar uma nova decodificação.
        /// </summary>
        public static BitmapImage GetCached(string url, int width)
        {
            if (string.IsNullOrEmpty(url)) return null;
            var key = url + "#" + width;
            if (Mem.TryGetValue(key, out var bmp))
            {
                TouchSafe(key);
                return bmp;
            }

            foreach (var w in new[] { 1400, 900, 760, 520, 380, 160, 140 })
            {
                if (w <= width) continue;
                var bigger = url + "#" + w;
                if (Mem.TryGetValue(bigger, out bmp))
                {
                    TouchSafe(bigger);
                    return bmp;
                }
            }
            return null;
        }

        public static void Preload(string url, int decodeWidth = 0, string referer = null)
        {
            if (string.IsNullOrWhiteSpace(url)) return;
            _ = LoadAsync(url, decodeWidth, referer);
        }

        public static Task<BitmapImage> LoadAsync(string url, int decodeWidth = 0, string referer = null)
        {
            if (string.IsNullOrWhiteSpace(url)) return Task.FromResult<BitmapImage>(null);

            var key = url + "#" + decodeWidth;
            if (Mem.TryGetValue(key, out var hit))
            {
                TouchSafe(key);
                return Task.FromResult(hit);
            }

            // Uma única tarefa por URL+tamanho. O download em si é único por URL.
            return InFlight.GetOrAdd(key, _ => LoadCoreAsync(url, decodeWidth, referer, key));
        }

        private static async Task<BitmapImage> LoadCoreAsync(string url, int decodeWidth, string referer, string key)
        {
            try
            {
                var bytes = await GetBytesAsync(url, referer).ConfigureAwait(false);
                if (bytes == null || bytes.Length < 32)
                {
                    Log.Write("img: sem bytes " + url);
                    return null;
                }

                var bmp = await DecodeAsync(url, bytes, decodeWidth).ConfigureAwait(false);
                if (bmp == null)
                {
                    Log.Write("img: decode falhou (" + bytes.Length + " bytes) " + url);
                    return null;
                }

                Store(key, bmp);
                return bmp;
            }
            catch (Exception ex)
            {
                Log.Write("img: erro " + ex.GetType().Name + " " + ex.Message + " " + url);
                return null;
            }
            finally
            {
                InFlight.TryRemove(key, out _);
            }
        }

        // ------------------------------------------------------------------
        // Bytes originais: um download por URL, compartilhado entre tamanhos
        // ------------------------------------------------------------------
        private static Task<byte[]> GetBytesAsync(string url, string referer)
        {
            lock (ByteGate)
            {
                if (ByteCache.TryGetValue(url, out var cached))
                {
                    TouchByte(url);
                    return Task.FromResult(cached);
                }
            }
            return ByteFlight.GetOrAdd(url, _ => FetchAndCacheBytesAsync(url, referer));
        }

        private static async Task<byte[]> FetchAndCacheBytesAsync(string url, string referer)
        {
            try
            {
                // 1) Disco: bytes originais (aceita cache antigo por largura).
                var disk = await TryReadDiskAsync(url).ConfigureAwait(false);
                if (disk != null && disk.Length >= 32)
                {
                    PutBytes(url, disk);
                    return disk;
                }

                // 2) Rede: duas tentativas normais e uma terceira com cabeçalhos
                //    alternativos (alguns CDNs recusam o primeiro UA/Accept).
                for (var attempt = 0; attempt < 3; attempt++)
                {
                    var res = await FetchAsync(url, referer, attempt).ConfigureAwait(false);
                    if (res == null) continue;
                    if (res.Value.Bytes == null || res.Value.Bytes.Length < 32) continue;

                    PutBytes(url, res.Value.Bytes);
                    _ = WriteDiskAsync(url, res.Value.Bytes);
                    return res.Value.Bytes;
                }

                Log.Write("img: falha final " + url);
                return null;
            }
            catch (Exception ex)
            {
                Log.Write("img: bytes erro " + ex.GetType().Name + " " + ex.Message + " " + url);
                return null;
            }
            finally
            {
                ByteFlight.TryRemove(url, out _);
            }
        }

        // ------------------------------------------------------------------
        // Rede
        // ------------------------------------------------------------------
        private static async Task<(byte[] Bytes, string Type)?> FetchAsync(string url, string referer, int attempt)
        {
            if (MangaDexHttp.IsMangaDexHost(url))
            {
                try
                {
                    var dex = await MangaDexHttp.GetBytesAsync(url, 35000, referer).ConfigureAwait(false);
                    if (dex.Status < 200 || dex.Status >= 300 || dex.Body.Length == 0) return null;
                    return (dex.Body, "image/*");
                }
                catch
                {
                    return null;
                }
            }

            await NetLimit.WaitAsync().ConfigureAwait(false);
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                if (!string.IsNullOrEmpty(referer)) req.Headers.TryAddWithoutValidation("Referer", referer);
                if (attempt >= 1)
                {
                    req.Headers.TryAddWithoutValidation("User-Agent",
                        "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:128.0) Gecko/20100101 Firefox/128.0");
                    req.Headers.TryAddWithoutValidation("Accept", "image/jpeg,image/png,image/*;q=0.8,*/*;q=0.5");
                }
                if (attempt >= 2)
                {
                    req.Headers.TryAddWithoutValidation("Cache-Control", "no-cache");
                    await Task.Delay(200).ConfigureAwait(false);
                }

                using var res = await Client.SendAsync(req, HttpCompletionOption.ResponseContentRead).ConfigureAwait(false);
                if ((int)res.StatusCode == 429)
                {
                    Log.Write("img: http 429 " + url);
                    await Task.Delay(350).ConfigureAwait(false);
                    return null;
                }
                if (!res.IsSuccessStatusCode)
                {
                    Log.Write("img: http " + (int)res.StatusCode + " " + url);
                    return null;
                }

                var type = res.Content.Headers.ContentType?.MediaType ?? string.Empty;
                if (type.StartsWith("text/", StringComparison.OrdinalIgnoreCase))
                {
                    Log.Write("img: tipo " + type + " " + url);
                    return null;
                }

                var bytes = await res.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                if (bytes.Length == 0)
                {
                    Log.Write("img: vazio " + url);
                    return null;
                }
                return (bytes, type);
            }
            catch (Exception ex)
            {
                Log.Write("img: erro " + ex.GetType().Name + " " + ex.Message + " " + url);
                return null;
            }
            finally
            {
                NetLimit.Release();
            }
        }

        // ------------------------------------------------------------------
        // Decode / cache
        // ------------------------------------------------------------------
        private static async Task<BitmapImage> DecodeAsync(string url, byte[] bytes, int width)
        {
            await DecodeLimit.WaitAsync().ConfigureAwait(false);
            try
            {
                return TryDecode(url, bytes, width);
            }
            finally
            {
                DecodeLimit.Release();
            }
        }

        /// <summary>
        /// Decodifica a imagem. AVIF/HEIF não é suportado pelo WIC sem a extensão
        /// AV1 do Windows, então é convertido (via ImageMagick/libheif) antes.
        /// </summary>
        private static BitmapImage TryDecode(string url, byte[] original, int width)
        {
            try
            {
                if (original == null || original.Length < 32) return null;

                var bytes = original;
                var converted = false;
                if (IsHeifContainer(original))
                {
                    bytes = GetHeifBytes(url, original, width);
                    if (bytes == null) return null;
                    converted = true;
                }
                else if (!LooksLikeImage(original))
                {
                    return null;
                }

                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                // AVIF/HEIF já saiu redimensionado do ImageMagick; pedir DecodePixelWidth
                // aqui só faria o WIC reamostrar (e gastar memória à toa).
                if (width > 0 && !converted) bmp.DecodePixelWidth = width;
                bmp.StreamSource = new MemoryStream(bytes, false);
                bmp.EndInit();
                bmp.Freeze();
                if (bmp.PixelWidth <= 1 || bmp.PixelHeight <= 1) return null;
                return bmp;
            }
            catch
            {
                return null;
            }
        }

        private static bool IsHeifContainer(byte[] b)
        {
            if (b.Length < 16) return false;
            // ISO BMFF: caixa 'ftyp' no offset 4 e marca no offset 8.
            if (!(b[4] == 0x66 && b[5] == 0x74 && b[6] == 0x79 && b[7] == 0x70)) return false;
            var brand = Encoding.ASCII.GetString(b, 8, 4);
            return brand == "avif" || brand == "avis" || brand == "mif1" || brand == "msf1" ||
                   brand == "heic" || brand == "heix" || brand == "hevc" || brand == "hevx";
        }

        private static byte[] GetHeifBytes(string url, byte[] bytes, int width)
        {
            var key = url + "#hf" + width;
            lock (HeifGate)
            {
                if (HeifCache.TryGetValue(key, out var cached))
                {
                    TouchHeif(key);
                    return cached;
                }
            }

            var converted = ConvertHeif(bytes, width);
            if (converted == null) return null;
            PutHeif(key, converted);
            return converted;
        }

        /// <summary>Converte AVIF/HEIC em JPEG (já no tamanho de exibição).</summary>
        private static byte[] ConvertHeif(byte[] bytes, int width)
        {
            // libheif decodifica a página inteira antes de reduzir: converter várias
            // tiras gigantes ao mesmo tempo estoura a memória. Duas por vez.
            ConvertGate.Wait();
            try
            {
                using var img = new ImageMagick.MagickImage(bytes);
                if (width > 0 && img.Width > width)
                    img.Resize(new ImageMagick.MagickGeometry((uint)width, 0u));
                img.Quality = 88;
                img.Strip();
                return img.ToByteArray(ImageMagick.MagickFormat.Jpeg);
            }
            catch (Exception ex)
            {
                Log.Write("img: avif falhou " + ex.GetType().Name + " " + ex.Message);
                return null;
            }
            finally
            {
                ConvertGate.Release();
            }
        }

        private static bool LooksLikeImage(byte[] b)
        {
            bool jpg = b[0] == 0xFF && b[1] == 0xD8;
            bool png = b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47;
            bool gif = b[0] == 0x47 && b[1] == 0x49 && b[2] == 0x46;
            bool bmp = b[0] == 0x42 && b[1] == 0x4D;
            bool webp = b.Length > 12 && b[0] == 0x52 && b[1] == 0x49 && b[2] == 0x46 && b[3] == 0x46 &&
                        b[8] == 0x57 && b[9] == 0x45 && b[10] == 0x42 && b[11] == 0x50;
            return jpg || png || gif || bmp || webp;
        }

        // ------------------------------------------------------------------
        // Disco
        // ------------------------------------------------------------------
        private static string HashName(string url)
        {
            return HashCache.GetOrAdd(url, u =>
            {
                using var sha = SHA1.Create();
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(u));
                return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
            });
        }

        private static string DiskPath(string url) => Path.Combine(CacheDir, HashName(url) + ".bin");

        private static async Task<byte[]> TryReadDiskAsync(string url)
        {
            try
            {
                if (!Directory.Exists(CacheDir)) return null;
                var name = HashName(url);
                // Ordem: arquivo novo (sem largura) e caches antigos por largura.
                var candidates = new[]
                {
                    name + ".bin",
                    name + "_900.bin",
                    name + "_760.bin",
                    name + "_520.bin",
                    name + "_380.bin",
                    name + "_160.bin",
                    name + "_140.bin",
                };
                foreach (var candidate in candidates)
                {
                    var bytes = await TryReadAsync(Path.Combine(CacheDir, candidate)).ConfigureAwait(false);
                    if (bytes != null && bytes.Length >= 32) return bytes;
                }
                return null;
            }
            catch
            {
                return null;
            }
        }

        private static async Task<byte[]> TryReadAsync(string file)
        {
            if (string.IsNullOrEmpty(file) || !File.Exists(file)) return null;
            try
            {
                await using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read,
                    81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
                var buf = new byte[fs.Length];
                var read = 0;
                while (read < buf.Length)
                {
                    var n = await fs.ReadAsync(buf, read, buf.Length - read).ConfigureAwait(false);
                    if (n <= 0) break;
                    read += n;
                }
                return read == buf.Length ? buf : null;
            }
            catch
            {
                return null;
            }
        }

        private static async Task WriteDiskAsync(string url, byte[] bytes)
        {
            if (string.IsNullOrEmpty(url) || bytes == null || bytes.Length == 0) return;
            try
            {
                Directory.CreateDirectory(CacheDir);
                var file = DiskPath(url);
                var tmp = file + ".tmp";
                await using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None,
                    81920, FileOptions.Asynchronous | FileOptions.SequentialScan))
                    await fs.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
                if (File.Exists(file)) File.Delete(file);
                File.Move(tmp, file);
            }
            catch
            {
                // cache é opcional; falha silenciosa não afeta a leitura
            }
        }

        private static void TryDelete(string file)
        {
            try { if (!string.IsNullOrEmpty(file) && File.Exists(file)) File.Delete(file); } catch { }
        }

        /// <summary>
        /// Remove os arquivos mais antigos quando o cache passa de 1,2 GB.
        /// Roda uma única vez por sessão, em segundo plano, sem tocar na UI.
        /// </summary>
        public static void TrimDiskCacheAsync()
        {
            if (Interlocked.Exchange(ref _diskTrimStarted, 1) == 1) return;
            _ = Task.Run(() =>
            {
                try
                {
                    if (!Directory.Exists(CacheDir)) return;
                    const long cap = 1_200L * 1024 * 1024;
                    var files = new DirectoryInfo(CacheDir)
                        .GetFiles("*.bin")
                        .OrderBy(f => f.LastWriteTimeUtc)
                        .ToList();
                    var total = files.Sum(f => f.Length);
                    if (total <= cap) return;

                    var target = (long)(cap * 0.7);
                    var removed = 0;
                    foreach (var f in files)
                    {
                        if (total <= target) break;
                        try
                        {
                            total -= f.Length;
                            f.Delete();
                            removed++;
                        }
                        catch { }
                    }
                    if (removed > 0)
                        Log.Write("img: cache limpo (" + removed + " arquivos, restam " + (total / (1024 * 1024)) + " MB)");
                }
                catch (Exception ex)
                {
                    Log.Write("img: limpeza falhou " + ex.Message);
                }
            });
        }

        // ------------------------------------------------------------------
        // LRU de bitmaps
        // ------------------------------------------------------------------
        private static void Store(string key, BitmapImage bmp)
        {
            Mem[key] = bmp;
            lock (LruGate)
            {
                if (MemPixels.TryGetValue(key, out var old)) _pixelTotal -= old;
                var px = (long)bmp.PixelWidth * bmp.PixelHeight;
                MemPixels[key] = px;
                _pixelTotal += px;

                Touch(key);
                while (Lru.Count > 1 && (Lru.Count > MemLimit || _pixelTotal > PixelBudget))
                {
                    var oldest = Lru.First;
                    if (oldest == null) break;
                    Lru.RemoveFirst();
                    if (Mem.TryRemove(oldest.Value, out _) && MemPixels.TryGetValue(oldest.Value, out var freed))
                    {
                        _pixelTotal -= freed;
                        MemPixels.Remove(oldest.Value);
                    }
                }
            }
        }

        private static void TouchSafe(string key)
        {
            lock (LruGate)
            {
                if (Mem.ContainsKey(key)) Touch(key);
            }
        }

        private static void Touch(string key)
        {
            var node = Lru.Find(key);
            if (node != null) Lru.Remove(node);
            Lru.AddLast(key);
        }

        private static void PutBytes(string url, byte[] bytes)
        {
            lock (ByteGate)
            {
                if (ByteCache.TryGetValue(url, out var old)) _byteTotal -= old.Length;
                ByteCache[url] = bytes;
                _byteTotal += bytes.Length;
                TouchByte(url);
                while (ByteLru.Count > 1 && (ByteCache.Count > ByteLimit || _byteTotal > ByteBudget))
                {
                    var first = ByteLru.First;
                    if (first == null) break;
                    ByteLru.RemoveFirst();
                    if (ByteCache.Remove(first.Value, out var removed)) _byteTotal -= removed.Length;
                }
            }
        }

        private static void TouchByte(string url)
        {
            var node = ByteLru.Find(url);
            if (node != null) ByteLru.Remove(node);
            ByteLru.AddLast(url);
        }

        private static void PutHeif(string key, byte[] bytes)
        {
            lock (HeifGate)
            {
                if (HeifCache.TryGetValue(key, out var old)) _heifTotal -= old.Length;
                HeifCache[key] = bytes;
                _heifTotal += bytes.Length;
                TouchHeif(key);
                while (HeifLru.Count > 1 && _heifTotal > HeifBudget)
                {
                    var first = HeifLru.First;
                    if (first == null) break;
                    HeifLru.RemoveFirst();
                    if (HeifCache.Remove(first.Value, out var removed)) _heifTotal -= removed.Length;
                }
            }
        }

        private static void TouchHeif(string key)
        {
            var node = HeifLru.Find(key);
            if (node != null) HeifLru.Remove(node);
            HeifLru.AddLast(key);
        }

        /// <summary>Apaga capas/páginas do disco (Configurações).</summary>
        public static int ClearDiskCache()
        {
            var count = 0;
            try
            {
                if (Directory.Exists(CacheDir))
                {
                    foreach (var f in Directory.EnumerateFiles(CacheDir))
                    {
                        try { File.Delete(f); count++; } catch { }
                    }
                }
            }
            catch { }

            Mem.Clear();
            lock (LruGate)
            {
                Lru.Clear();
                MemPixels.Clear();
                _pixelTotal = 0;
            }
            lock (ByteGate)
            {
                ByteCache.Clear();
                ByteLru.Clear();
                _byteTotal = 0;
            }
            lock (HeifGate)
            {
                HeifCache.Clear();
                HeifLru.Clear();
                _heifTotal = 0;
            }
            return count;
        }

        /// <summary>Tamanho aproximado do cache em MB.</summary>
        public static long DiskCacheSize()
        {
            try
            {
                if (!Directory.Exists(CacheDir)) return 0;
                return Directory.EnumerateFiles(CacheDir).Sum(f =>
                {
                    try { return new FileInfo(f).Length; } catch { return 0L; }
                }) / (1024 * 1024);
            }
            catch
            {
                return 0;
            }
        }
    }
}
