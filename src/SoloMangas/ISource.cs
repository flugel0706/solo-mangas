using System.Collections.Generic;
using System.Threading.Tasks;

namespace SoloMangasNative
{
    /// <summary>Contrato comum das fontes. Todas devolvem o mesmo modelo normalizado.</summary>
    public interface ISource
    {
        string Key { get; }
        string Name { get; }
        bool SupportsLogin { get; }

        /// <summary>Fonte exclusiva de obras +18: só aparece com o modo adulto ligado.</summary>
        bool AdultOnly { get; }

        Task<CatalogResult> CatalogAsync(SourceQuery query);
        Task<Manga> MangaAsync(string id, bool adult);
        Task<List<Chapter>> ChaptersAsync(string id);
        Task<PageSet> PagesAsync(string token, bool adult);
        Task<List<string>> GenresAsync();
    }
}
