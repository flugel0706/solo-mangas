using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using SoloMangas.Shared;

namespace SoloMangasNative
{
    internal static class DictUtil
    {
        public static string Str(this Dictionary<string, object> d, string key)
        {
            if (d == null || !d.TryGetValue(key, out var v) || v == null) return string.Empty;
            return Convert.ToString(v, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        public static int Int(this Dictionary<string, object> d, string key)
        {
            if (d == null || !d.TryGetValue(key, out var v) || v == null) return 0;
            if (v is int i) return i;
            if (v is long l) return (int)l;
            return int.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), out var n) ? n : 0;
        }

        public static bool Bool(this Dictionary<string, object> d, string key)
        {
            if (d == null || !d.TryGetValue(key, out var v) || v == null) return false;
            if (v is bool b) return b;
            return Convert.ToString(v, CultureInfo.InvariantCulture) == "1";
        }

        public static List<string> List(this Dictionary<string, object> d, string key)
        {
            if (d == null || !d.TryGetValue(key, out var v) || v == null) return new List<string>();
            if (v is IEnumerable<string> e) return e.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
            return new List<string>();
        }

        public static Manga ToManga(this Dictionary<string, object> d, string source, string idOverride = null)
        {
            if (d == null) return null;
            var id = idOverride ?? d.Str("id");
            if (string.IsNullOrWhiteSpace(id)) return null;
            return new Manga
            {
                Source = source,
                Id = id,
                Title = d.Str("title") is var t && !string.IsNullOrWhiteSpace(t) ? t : "Sem titulo",
                TitleAlt = d.Str("title_alt"),
                Cover = d.Str("cover"),
                Description = d.Str("description"),
                Status = d.Str("status"),
                Url = d.Str("url"),
                Latest = d.Str("latest"),
                Adult = d.Bool("adult"),
                Chapters = d.Int("chapters"),
                Genres = d.List("genres"),
            };
        }
    }

    /// <summary>Fonte Manhastro (API JSON).</summary>
    public sealed class ManhastroSource : ISource
    {
        public static readonly string[] UiGenres =
        {
            "Ação", "Aventura", "Comédia", "Drama", "Fantasia", "Romance", "Slice of Life",
            "Suspense", "Mistério", "Ficção Científica", "Artes Marciais", "Escolar",
            "Histórico", "Psicológico", "Sobrenatural", "Reencarnação", "Sistema", "Isekai",
            "Culinária", "Música", "Ecchi", "Adulto", "Yaoi", "Yuri", "Shounen Ai",
            "Shoujo Ai", "Harem", "Vampiros", "Magia", "Super Poderes", "Policial", "Militar",
            "Samurai", "Webtoon", "Manhwa", "Manhua", "Manga", "Josei", "Seinen", "Shounen",
            "Shoujo", "Gender Bender", "Novel", "Tragédia", "Adaptação", "Sobrevivência",
        };

        private readonly ManhastroApi _api;

        public ManhastroSource(ManhastroApi api)
        {
            _api = api;
        }

        public string Key => "manhastro";
        public string Name => "Manhastro";
        public bool SupportsLogin => false;
        public bool AdultOnly => false;

        public async Task<CatalogResult> CatalogAsync(SourceQuery query)
        {
            var r = await _api.CatalogAsync(query.Page, query.Limit, query.Q, query.Adult,
                "ultimo_capitulo", "desc", query.Genre).ConfigureAwait(false);
            var page = new CatalogResult
            {
                Page = r.Int("page") is var p && p > 0 ? p : query.Page,
                Total = r.Int("total"),
                HasMore = r.Bool("hasMore"),
            };
            if (r.TryGetValue("items", out var raw) && raw is IEnumerable<Dictionary<string, object>> list)
            {
                foreach (var it in list)
                {
                    var m = it.ToManga("manhastro");
                    if (m != null) page.Items.Add(m);
                }
            }
            return page;
        }

        public async Task<Manga> MangaAsync(string id, bool adult)
        {
            if (!long.TryParse(id, out var nid) || nid <= 0) throw new Exception("Obra invalida.");
            var d = await _api.MangaAsync(nid, adult).ConfigureAwait(false);
            var m = d.ToManga("manhastro");
            if (m != null) m.Url = "https://manhastro.net/manga/" + m.Id;
            return m;
        }

        public async Task<List<Chapter>> ChaptersAsync(string id)
        {
            if (!long.TryParse(id, out var nid) || nid <= 0) throw new Exception("Obra invalida.");
            var list = await _api.ChaptersAsync(nid).ConfigureAwait(false);
            var outList = new List<Chapter>();
            foreach (var c in list)
            {
                var ch = new Chapter
                {
                    Id = c.Str("id"),
                    Title = c.Str("title") is var t && !string.IsNullOrWhiteSpace(t) ? t : "Capitulo",
                    Date = c.Str("date"),
                };
                ch.Number = NumberFrom(ch.Title);
                if (!string.IsNullOrEmpty(ch.Id)) outList.Add(ch);
            }
            return outList;
        }

        public async Task<PageSet> PagesAsync(string token, bool adult)
        {
            if (!long.TryParse(token, out var cid) || cid <= 0) throw new Exception("Capitulo invalido.");
            var d = await _api.PagesAsync(cid, adult).ConfigureAwait(false);
            var set = new PageSet { Text = d.Str("text") };
            if (d.TryGetValue("images", out var raw) && raw is IEnumerable<string> imgs)
                set.Images.AddRange(imgs.Where(x => !string.IsNullOrWhiteSpace(x)));
            return set;
        }

        public Task<List<string>> GenresAsync() => Task.FromResult(UiGenres.ToList());

        internal static double NumberFrom(string title)
        {
            var m = System.Text.RegularExpressions.Regex.Match(title ?? string.Empty, @"(\d+(?:[.,]\d+)?)");
            if (!m.Success) return 0;
            return double.TryParse(m.Groups[1].Value.Replace(',', '.'),
                NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : 0;
        }
    }
}
