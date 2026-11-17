using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using SoloMangas.Shared;

namespace SoloMangasNative
{
    /// <summary>Fonte Ler Manga Online (scraping + REST do tema).</summary>
    public sealed class LerMangaSource : ISource
    {
        private readonly LerMangaApi _api;

        public LerMangaSource(LerMangaApi api)
        {
            _api = api;
        }

        public string Key => "lermanga";
        public string Name => "Ler Mangá";
        public bool SupportsLogin => false;
        public bool AdultOnly => false;

        public async Task<CatalogResult> CatalogAsync(SourceQuery query)
        {
            var r = await _api.CatalogAsync(query.Page, query.Limit, query.Q, query.Adult, query.Genre)
                .ConfigureAwait(false);
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
                    var m = it.ToManga("lermanga");
                    if (m != null) page.Items.Add(m);
                }
            }
            return page;
        }

        public async Task<Manga> MangaAsync(string id, bool adult)
        {
            var d = await _api.MangaAsync(id, adult).ConfigureAwait(false);
            return d.ToManga("lermanga", id);
        }

        public async Task<List<Chapter>> ChaptersAsync(string id)
        {
            var list = await _api.ChaptersAsync(id).ConfigureAwait(false);
            var outList = new List<Chapter>();
            foreach (var c in list)
            {
                var ch = new Chapter
                {
                    Id = c.Str("id"),
                    Title = c.Str("title") is var t && !string.IsNullOrWhiteSpace(t) ? t : "Capitulo",
                    Date = c.Str("date"),
                    Number = c.Int("number"),
                };
                if (!string.IsNullOrEmpty(ch.Id)) outList.Add(ch);
            }
            return outList;
        }

        public async Task<PageSet> PagesAsync(string token, bool adult)
        {
            var d = await _api.PagesAsync(token, adult).ConfigureAwait(false);
            var set = new PageSet { Text = d.Str("text") };
            if (d.TryGetValue("images", out var raw) && raw is IEnumerable<string> imgs)
            {
                foreach (var i in imgs)
                    if (!string.IsNullOrWhiteSpace(i)) set.Images.Add(i);
            }
            return set;
        }

        public Task<List<string>> GenresAsync() => Task.FromResult(_api.Genres());
    }
}
