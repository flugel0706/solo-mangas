using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SoloMangasNative
{
    /// <summary>Manifesto de atualizacao ja resolvido (versao, arquivo, hash e notas).</summary>
    public sealed class UpdateInfo
    {
        public string Version { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string Sha256 { get; set; } = string.Empty;
        public long Size { get; set; }
        public string Notes { get; set; } = string.Empty;
        public string Manifest { get; set; } = string.Empty;
        public Version Parsed { get; set; } = new Version(0, 0, 0);
    }

    /// <summary>
    /// Atualizacao portatil: le um update.json (URL, pasta local ou arquivo ao
    /// lado do exe), baixa o novo executavel, confere o SHA-256 e troca o exe
    /// em uso por meio de uma segunda instancia do proprio app (--apply-update).
    /// </summary>
    public static class UpdateService
    {
        private const string ManifestName = "update.json";
        private static readonly HttpClient Http = CreateHttp();

        // ------------------------------------------------------------------
        // Versao / caminhos
        // ------------------------------------------------------------------

        public static Version CurrentVersion
        {
            get
            {
                var v = typeof(UpdateService).Assembly.GetName().Version ?? new Version(2, 6, 0, 0);
                return Normalize(v);
            }
        }

        public static string CurrentLabel =>
            CurrentVersion.Major + "." + CurrentVersion.Minor + "." + CurrentVersion.Build;

        public static string TargetExe =>
            Environment.ProcessPath ??
            Process.GetCurrentProcess().MainModule?.FileName ??
            string.Empty;

        public static string UpdateDir =>
            Path.Combine(Path.GetTempPath(), "SoloMangas-update");

        // ------------------------------------------------------------------
        // Verificacao
        // ------------------------------------------------------------------

        /// <summary>
        /// Busca e valida o manifesto. O parametro <paramref name="source"/> aceita
        /// uma URL http(s), uma pasta local/rede, o caminho de um .json ou vazio
        /// para usar o update.json que fica ao lado do executavel.
        /// </summary>
        public static async Task<UpdateInfo> CheckAsync(string source, CancellationToken ct)
        {
            var manifest = ResolveManifest(source);
            string json;

            if (IsHttp(manifest))
            {
                using var resp = await Http.GetAsync(AddCacheBuster(manifest), HttpCompletionOption.ResponseContentRead, ct)
                    .ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                    throw new Exception("O servidor respondeu HTTP " + (int)resp.StatusCode +
                        " ao buscar o manifesto de atualizacao.\n\nConfira a URL em Configuracoes > Atualizacoes.");
                json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            }
            else
            {
                if (!File.Exists(manifest))
                    throw new Exception("Manifesto de atualizacao nao encontrado em:\n" + manifest +
                        "\n\nColoque o update.json ao lado do executavel ou informe a URL do manifesto em Configuracoes > Atualizacoes.");
                json = await File.ReadAllTextAsync(manifest, ct).ConfigureAwait(false);
            }

            var info = ParseManifest(json, manifest);

            if (string.IsNullOrWhiteSpace(info.Url))
                throw new Exception("O manifesto nao informa o arquivo da nova versao (campo \"url\").");

            info.Url = ResolveUrl(info.Url, manifest);
            return info;
        }

        private static UpdateInfo ParseManifest(string json, string manifest)
        {
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
                throw new Exception("O manifesto de atualizacao esta em formato JSON invalido.");
            }

            using (doc)
            {
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                    throw new Exception("O manifesto de atualizacao precisa ser um objeto JSON.");

                var map = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
                foreach (var p in root.EnumerateObject()) map[p.Name] = p.Value;

                string Text(string name)
                {
                    if (!map.TryGetValue(name, out var el)) return string.Empty;
                    return el.ValueKind switch
                    {
                        JsonValueKind.String => el.GetString() ?? string.Empty,
                        JsonValueKind.Number => el.GetRawText(),
                        _ => string.Empty,
                    };
                }

                long Number(string name)
                {
                    if (!map.TryGetValue(name, out var el)) return 0;
                    if (el.ValueKind == JsonValueKind.Number && el.TryGetInt64(out var n)) return n;
                    if (el.ValueKind == JsonValueKind.String && long.TryParse(el.GetString(), out var s)) return s;
                    return 0;
                }

                var versionText = Text("version").Trim();
                var parsed = ParseVersion(versionText, out var valid);
                if (!valid)
                    throw new Exception("O manifesto nao informa uma versao valida (campo \"version\", ex.: \"2.5.0\").");

                return new UpdateInfo
                {
                    Version = versionText,
                    Url = Text("url").Trim(),
                    Sha256 = Text("sha256").Trim().Replace(" ", string.Empty),
                    Size = Number("size"),
                    Notes = Text("notes").Trim(),
                    Manifest = manifest,
                    Parsed = parsed,
                };
            }
        }

        // ------------------------------------------------------------------
        // Download + verificacao de hash
        // ------------------------------------------------------------------

        /// <summary>
        /// Baixa (ou copia) o executavel da nova versao para a pasta temporaria,
        /// confere o SHA-256 do manifesto e devolve o caminho pronto para aplicar.
        /// </summary>
        public static async Task<string> DownloadAsync(UpdateInfo info, IProgress<double> progress, CancellationToken ct)
        {
            var dir = UpdateDir;
            Directory.CreateDirectory(dir);
            CleanupOldFiles(dir);

            var name = "Solo-Mangas-" + info.Parsed.ToString(3) + ".exe";
            var dest = Path.Combine(dir, name);
            var tmp = dest + ".part";

            if (File.Exists(dest) && HashMatches(dest, info.Sha256))
            {
                progress?.Report(1);
                return dest;
            }

            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            try { if (File.Exists(dest)) File.Delete(dest); } catch { }

            if (IsHttp(info.Url))
            {
                using var resp = await Http.GetAsync(info.Url, HttpCompletionOption.ResponseHeadersRead, ct)
                    .ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                    throw new Exception("O download da versao " + info.Version + " falhou (HTTP " +
                        (int)resp.StatusCode + ").");

                var total = resp.Content.Headers.ContentLength ?? info.Size;
                await using var input = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                await using var output = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

                var buffer = new byte[81920];
                long done = 0;
                int read;
                while ((read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    done += read;
                    if (total > 0) progress?.Report(Math.Min(1.0, (double)done / total));
                }
            }
            else
            {
                if (!File.Exists(info.Url))
                    throw new Exception("O arquivo da versao " + info.Version + " nao foi encontrado em:\n" + info.Url);
                progress?.Report(0.05);
                await using var input = File.OpenRead(info.Url);
                await using var output = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
                await input.CopyToAsync(output, 81920, ct).ConfigureAwait(false);
                progress?.Report(1);
            }

            if (!string.IsNullOrEmpty(info.Sha256))
            {
                var hash = Sha256File(tmp);
                if (!hash.Equals(info.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    try { File.Delete(tmp); } catch { }
                    throw new Exception("O arquivo baixado nao confere com o SHA-256 do manifesto.\n\n" +
                        "Esperado: " + info.Sha256 + "\nObtido: " + hash +
                        "\n\nA atualizacao foi cancelada.");
                }
            }

            File.Move(tmp, dest);
            progress?.Report(1);
            return dest;
        }

        // ------------------------------------------------------------------
        // Troca do executavel em uso
        // ------------------------------------------------------------------

        /// <summary>
        /// Abre uma segunda instancia do novo exe com "--apply-update &lt;alvo&gt; &lt;pid&gt;".
        /// Ela espera o app atual fechar, substitui o arquivo e reabre o programa.
        /// </summary>
        public static bool LaunchApplier(string newExe, out string error)
        {
            error = null;
            var target = TargetExe;
            if (string.IsNullOrEmpty(target) || !File.Exists(target))
            {
                error = "Nao foi possivel localizar o executavel atual do Solo Mangas.";
                return false;
            }

            if (!CanWriteToFolder(Path.GetDirectoryName(target)))
            {
                error = "A pasta do executavel nao permite escrita:\n" + Path.GetDirectoryName(target) +
                    "\n\nMova o Solo Mangas para Documentos, Area de Trabalho ou outra pasta sua e tente de novo.";
                return false;
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = newExe,
                    Arguments = "--apply-update \"" + target + "\" " + Environment.ProcessId,
                    UseShellExecute = false,
                    WorkingDirectory = Path.GetDirectoryName(newExe) ?? UpdateDir,
                };
                Process.Start(psi);
                return true;
            }
            catch (Exception ex)
            {
                error = "Nao foi possivel iniciar o aplicador da atualizacao: " + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Modo aplicador (roda na nova versao, a partir da pasta temporaria):
        /// espera o processo antigo sair, copia a si mesmo sobre o alvo e reabre.
        /// </summary>
        public static void ApplyUpdateAndExit(string target, int parentPid)
        {
            try
            {
                var self = TargetExe;

                if (!string.IsNullOrEmpty(self) &&
                    !string.Equals(self, target, StringComparison.OrdinalIgnoreCase))
                {
                    if (parentPid > 0 && parentPid != Environment.ProcessId)
                    {
                        var deadline = DateTime.UtcNow.AddSeconds(90);
                        while (DateTime.UtcNow < deadline && IsAlive(parentPid))
                            Thread.Sleep(300);
                    }

                    Exception last = null;
                    var copied = false;
                    for (var i = 0; i < 120 && !copied; i++)
                    {
                        try
                        {
                            File.Copy(self, target, true);
                            copied = true;
                        }
                        catch (Exception ex)
                        {
                            last = ex;
                            Thread.Sleep(400);
                        }
                    }

                    if (!copied)
                    {
                        Directory.CreateDirectory(UpdateDir);
                        File.WriteAllText(Path.Combine(UpdateDir, "falha-ao-aplicar.txt"),
                            DateTime.Now.ToString("s") + " - nao foi possivel substituir " + target +
                            ": " + last?.Message, new UTF8Encoding(false));
                        return;
                    }
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = target,
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(target),
                });
            }
            catch
            {
                // O aplicador roda sem interface: falhas ficam registradas ou sao silenciosas.
            }
            finally
            {
                Environment.Exit(0);
            }
        }

        // ------------------------------------------------------------------
        // Utilitarios
        // ------------------------------------------------------------------

        private static HttpClient CreateHttp()
        {
            var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "SoloMangas-Updater/2.6");
            return http;
        }

        private static Version Normalize(Version v) =>
            new Version(v.Major, Math.Max(0, v.Minor), Math.Max(0, v.Build), Math.Max(0, v.Revision));

        private static Version ParseVersion(string text, out bool ok)
        {
            ok = false;
            if (string.IsNullOrWhiteSpace(text)) return new Version(0, 0, 0);
            var clean = text.Trim();
            if (clean.StartsWith("v", StringComparison.OrdinalIgnoreCase)) clean = clean.Substring(1);
            if (clean.IndexOf('.') < 0) return new Version(0, 0, 0);
            if (!Version.TryParse(clean, out var v)) return new Version(0, 0, 0);
            ok = true;
            return Normalize(v);
        }

        private static bool IsHttp(string s) =>
            s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            s.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

        private static string AddCacheBuster(string url) =>
            url + (url.Contains("?") ? "&" : "?") + "t=" + DateTime.UtcNow.Ticks;

        private static string ResolveManifest(string source)
        {
            source = (source ?? string.Empty).Trim();

            if (source.Length == 0)
            {
                var exeDir = Path.GetDirectoryName(TargetExe);
                if (string.IsNullOrEmpty(exeDir)) exeDir = AppContext.BaseDirectory;
                return Path.Combine(exeDir, ManifestName);
            }

            if (IsHttp(source))
            {
                if (source.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return source;
                return source.TrimEnd('/') + "/" + ManifestName;
            }

            if (source.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                return Path.GetFullPath(source);
            return Path.Combine(Path.GetFullPath(source), ManifestName);
        }

        private static string ResolveUrl(string url, string manifest)
        {
            url = (url ?? string.Empty).Trim();
            if (url.Length == 0) return string.Empty;

            if (IsHttp(url)) return url;

            if (IsHttp(manifest))
            {
                if (Uri.TryCreate(new Uri(manifest, UriKind.Absolute), url, out var abs))
                    return abs.ToString();
                return url;
            }

            if (Path.IsPathRooted(url)) return url;

            var dir = Path.GetDirectoryName(manifest) ?? ".";
            return Path.GetFullPath(Path.Combine(dir, url));
        }

        private static bool HashMatches(string file, string expected)
        {
            if (string.IsNullOrEmpty(expected)) return false;
            try { return Sha256File(file).Equals(expected, StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }

        private static string Sha256File(string path)
        {
            using var sha = SHA256.Create();
            using var fs = File.OpenRead(path);
            return Convert.ToHexString(sha.ComputeHash(fs));
        }

        private static bool CanWriteToFolder(string dir)
        {
            try
            {
                if (string.IsNullOrEmpty(dir)) return false;
                var probe = Path.Combine(dir, ".solo-update-test");
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsAlive(int pid)
        {
            try
            {
                var p = Process.GetProcessById(pid);
                return !p.HasExited;
            }
            catch
            {
                return false;
            }
        }

        private static void CleanupOldFiles(string dir)
        {
            try
            {
                foreach (var f in Directory.GetFiles(dir, "Solo-Mangas-*.exe"))
                    if (File.GetLastWriteTimeUtc(f) < DateTime.UtcNow.AddDays(-7))
                        File.Delete(f);
            }
            catch
            {
            }
        }
    }
}
