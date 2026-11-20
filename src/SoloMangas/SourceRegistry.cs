using System;
using System.Collections.Generic;
using System.Linq;
using SoloMangas.Shared;

namespace SoloMangasNative
{
    /// <summary>Registro das fontes disponiveis no app.</summary>
    public sealed class SourceRegistry
    {
        public static SourceRegistry Instance { get; } = new SourceRegistry();

        private readonly List<ISource> _sources = new List<ISource>();

        public MangeekApi Mangeek { get; }
        public SessionStore Session => SessionStore.Instance;

        private SourceRegistry()
        {
            SessionStore.Instance.Load();
            Mangeek = new MangeekApi(SessionStore.Instance);

            _sources.Add(new MangeekSource(Mangeek));
            _sources.Add(new ManhastroSource(new ManhastroApi()));
            _sources.Add(new LerMangaSource(new LerMangaApi()));
            _sources.Add(new MangaDexSource());
            _sources.Add(new MangaFlixSource());
            _sources.Add(new MangaLivreSource());
            _sources.Add(new SukiSource());
            _sources.Add(new GoMangasSource());

            // Fontes exclusivamente +18 (em portugues). Ficam ocultas ate o modo +18 ligar.
            _sources.Add(new HanamiSource());
            _sources.Add(new HQBrasaSource());
            _sources.Add(new MelhorHentaiSource());
            _sources.Add(new MangaDexSource("mangadex18", "MangaDex +18", true));
        }

        public IReadOnlyList<ISource> Sources => _sources;

        /// <summary>Fontes visiveis para o modo atual (as +18 so entram com adult=true).</summary>
        public IReadOnlyList<ISource> Enabled(bool adult)
        {
            return _sources.Where(s => adult || !s.AdultOnly).ToList();
        }

        public string NameOf(string key)
        {
            var hit = _sources.FirstOrDefault(s => string.Equals(s.Key, key, StringComparison.OrdinalIgnoreCase));
            return hit == null ? key : hit.Name;
        }

        public ISource Get(string key)
        {
            var hit = _sources.FirstOrDefault(s => string.Equals(s.Key, key, StringComparison.OrdinalIgnoreCase));
            if (hit == null) throw new Exception("Fonte desconhecida: " + key);
            return hit;
        }

        /// <summary>Verdadeiro quando a fonte ainda existe no app.</summary>
        public bool Has(string key)
        {
            return !string.IsNullOrEmpty(key) &&
                   _sources.Any(s => string.Equals(s.Key, key, StringComparison.OrdinalIgnoreCase));
        }
    }
}
