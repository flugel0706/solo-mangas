using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SoloMangasNative
{
    /// <summary>
    /// Cache da lista de páginas de um capítulo (memória + disco).
    ///
    /// Antes da 2.9.2 cada abertura de capítulo refazia a requisição que devolve
    /// as URLs das páginas, mesmo quando o usuário voltava ao mesmo capítulo ou
    /// emendava o próximo. Aqui a lista fica em memória por 1 hora e em disco
    /// por 7 dias; as imagens em si continuam no cache do ImageLoader.
    ///
    /// A MangaDex fica de fora: as URLs do at-home server expiram em poucos
    /// minutos e uma lista velha só produziria imagens quebradas.
    /// </summary>
    internal static class PageSetCache
    {
        private sealed class Entry
        {
            public DateTime At;
            public PageSet Value;
        }

        private sealed class DiskEntry
        {
            public DateTime At { get; set; }
            public List<string> Images { get; set; }
            public string Text { get; set; }
        }

        private const int MemoryLimit = 220;
        private static readonly TimeSpan MemoryTtl = TimeSpan.FromMinutes(60);
        private static readonly TimeSpan DiskTtl = TimeSpan.FromDays(7);

        private static readonly object Gate = new object();
        private static readonly Dictionary<string, Entry> Mem =
            new Dictionary<string, Entry>(StringComparer.Ordinal);
        private static readonly Dictionary<string, Task<PageSet>> Flight =
            new Dictionary<string, Task<PageSet>>(StringComparer.Ordinal);

        private static int _trimStarted;

        public static string CacheDir { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SoloMangas", "cache", "pages");

        /// <summary>Fontes cujas URLs de página são estáveis e podem ser guardadas.</summary>
        public static bool IsCacheable(ISource source)
        {
            if (source == null) return false;
            return !string.Equals(source.Key, "mangadex", StringComparison.OrdinalIgnoreCase) &&
                   !string.Equals(source.Key, "mangadex18", StringComparison.OrdinalIgnoreCase);
        }

        private static string KeyOf(ISource source, string chapterId, bool adult)
        {
            return source.Key + "|" + (adult ? "1" : "0") + "|" + (chapterId ?? string.Empty);
        }

        /// <summary>Busca a lista de páginas, usando memória, disco e uma única requisição por capítulo.</summary>
        public static async Task<PageSet> GetAsync(ISource source, string chapterId, bool adult)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (string.IsNullOrWhiteSpace(chapterId)) throw new Exception("Capítulo inválido.");

            var key = KeyOf(source, chapterId, adult);
            lock (Gate)
            {
                if (Mem.TryGetValue(key, out var hit) && DateTime.UtcNow - hit.At < MemoryTtl)
                    return hit.Value;
            }

            Task<PageSet> task;
            lock (Gate)
            {
                if (!Flight.TryGetValue(key, out task))
                {
                    task = LoadAsync(source, chapterId, adult, key);
                    Flight[key] = task;
                }
            }

            try
            {
                return await task.ConfigureAwait(false);
            }
            finally
            {
                lock (Gate) Flight.Remove(key);
            }
        }

        /// <summary>Prepara a lista do próximo capítulo sem bloquear a leitura atual.</summary>
        public static void Prefetch(ISource source, string chapterId, bool adult)
        {
            if (source == null || string.IsNullOrWhiteSpace(chapterId)) return;
            if (!IsCacheable(source)) return;
            var key = KeyOf(source, chapterId, adult);
            lock (Gate)
            {
                if (Mem.TryGetValue(key, out var hit) && DateTime.UtcNow - hit.At < MemoryTtl) return;
                if (Flight.ContainsKey(key)) return;
            }
            _ = Task.Run(async () =>
            {
                try { await GetAsync(source, chapterId, adult).ConfigureAwait(false); }
                catch { }
            });
        }

        private static async Task<PageSet> LoadAsync(ISource source, string chapterId, bool adult, string key)
        {
            if (IsCacheable(source))
            {
                var disk = await TryReadDiskAsync(key).ConfigureAwait(false);
                if (disk != null)
                {
                    PutMemory(key, disk);
                    return disk;
                }
            }

            var pages = await source.PagesAsync(chapterId, adult).ConfigureAwait(false);
            if (pages == null) throw new Exception("Capítulo sem páginas disponíveis.");
            PutMemory(key, pages);

            if (IsCacheable(source) && (pages.Images.Count > 0 || !string.IsNullOrWhiteSpace(pages.Text)))
                _ = WriteDiskAsync(key, pages);
            return pages;
        }

        private static void PutMemory(string key, PageSet pages)
        {
            lock (Gate)
            {
                Mem[key] = new Entry { At = DateTime.UtcNow, Value = pages };
                if (Mem.Count <= MemoryLimit) return;
                foreach (var old in Mem.OrderBy(kv => kv.Value.At).Take(Mem.Count - MemoryLimit).ToList())
                    Mem.Remove(old.Key);
            }
        }

        // ------------------------------------------------------------------
        // Disco
        // ------------------------------------------------------------------
        private static string FileFor(string key)
        {
            using var sha = SHA1.Create();
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(key));
            return Path.Combine(CacheDir, BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant() + ".json");
        }

        private static async Task<PageSet> TryReadDiskAsync(string key)
        {
            try
            {
                var file = FileFor(key);
                if (!File.Exists(file)) return null;
                if (DateTime.UtcNow - File.GetLastWriteTimeUtc(file) > DiskTtl) return null;

                var raw = await File.ReadAllTextAsync(file, Encoding.UTF8).ConfigureAwait(false);
                var entry = JsonSerializer.Deserialize<DiskEntry>(raw);
                if (entry == null || DateTime.UtcNow - entry.At.ToUniversalTime() > DiskTtl) return null;

                var set = new PageSet { Text = entry.Text ?? string.Empty };
                if (entry.Images != null)
                    foreach (var url in entry.Images)
                        if (!string.IsNullOrWhiteSpace(url) && !set.Images.Contains(url)) set.Images.Add(url);
                if (set.Images.Count == 0 && string.IsNullOrWhiteSpace(set.Text)) return null;
                return set;
            }
            catch
            {
                return null;
            }
        }

        private static async Task WriteDiskAsync(string key, PageSet pages)
        {
            try
            {
                Directory.CreateDirectory(CacheDir);
                var entry = new DiskEntry
                {
                    At = DateTime.UtcNow,
                    Images = new List<string>(pages.Images),
                    Text = pages.Text,
                };
                var json = JsonSerializer.Serialize(entry);
                var file = FileFor(key);
                var tmp = file + ".tmp";
                await File.WriteAllTextAsync(tmp, json, Encoding.UTF8).ConfigureAwait(false);
                if (File.Exists(file)) File.Delete(file);
                File.Move(tmp, file);
                TrimDiskOnce();
            }
            catch
            {
                // Cache é opcional: falha silenciosa não afeta a leitura.
            }
        }

        /// <summary>Mantém no máximo 700 listas em disco, apagando as mais antigas.</summary>
        private static void TrimDiskOnce()
        {
            if (Interlocked.Exchange(ref _trimStarted, 1) == 1) return;
            _ = Task.Run(() =>
            {
                try
                {
                    if (!Directory.Exists(CacheDir)) return;
                    var files = new DirectoryInfo(CacheDir).GetFiles("*.json")
                        .OrderBy(f => f.LastWriteTimeUtc).ToList();
                    var remove = files.Count - 700;
                    if (remove <= 0) return;
                    foreach (var f in files.Take(remove))
                    {
                        try { f.Delete(); } catch { }
                    }
                }
                catch { }
            });
        }
    }
}
