using System;
using System.IO;
using System.Text.Json;

namespace SoloMangas.Shared
{
    /// <summary>
    /// token JWT + dados do usuario. Persistido em %APPDATA%\MangeekPC\session.json,
    /// importando a sessao da versao anterior (Electron) quando existir.
    /// </summary>
    public sealed class SessionStore
    {
        public static SessionStore Instance { get; } = new SessionStore();

        private readonly object _gate = new object();

        public string Token { get; private set; }
        public JsonElement? User { get; private set; }

        public void Load()
        {
            lock (_gate)
            {
                if (TryLoadFile(AppPaths.SessionFile)) return;
                if (TryLoadFile(AppPaths.LegacySessionFile))
                {
                    Save();
                }
            }
        }

        private bool TryLoadFile(string file)
        {
            try
            {
                if (!File.Exists(file)) return false;
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object) return false;
                if (!root.TryGetProperty("token", out var tok) || tok.ValueKind != JsonValueKind.String) return false;

                Token = tok.GetString();
                if (root.TryGetProperty("user", out var u) && u.ValueKind == JsonValueKind.Object)
                    User = u.Clone();
                else
                    User = null;
                return !string.IsNullOrWhiteSpace(Token);
            }
            catch
            {
                return false;
            }
        }

        public void Set(string token, JsonElement? user)
        {
            lock (_gate)
            {
                Token = token;
                User = user;
                Save();
            }
        }

        public void Clear()
        {
            lock (_gate)
            {
                Token = null;
                User = null;
                Save();
            }
        }

        public void Save()
        {
            try
            {
                AppPaths.EnsureDataDir();
                var payload = new { token = Token, user = User };
                var json = JsonSerializer.Serialize(payload);
                var tmp = AppPaths.SessionFile + ".tmp";
                File.WriteAllText(tmp, json);
                File.Move(tmp, AppPaths.SessionFile, true);
            }
            catch
            {
                // sem permissao de escrita: a sessao continua apenas em memoria
            }
        }
    }
}
