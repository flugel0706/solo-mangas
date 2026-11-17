using System;
using System.Collections.Generic;
using System.Linq;

namespace SoloMangasNative
{
    /// <summary>Obra normalizada, independente da fonte.</summary>
    public sealed class Manga
    {
        public string Source = string.Empty;
        public string Id = string.Empty;
        public string Title = "Sem titulo";
        public string TitleAlt = string.Empty;
        public string Cover = string.Empty;
        public string Description = string.Empty;
        public string Status = string.Empty;
        public string Url = string.Empty;
        public string Latest = string.Empty;
        public bool Adult;
        public int Chapters;
        public int Year;
        public List<string> Genres = new List<string>();

        public string Key => Source + ":" + Id;
    }

    public sealed class Chapter
    {
        public string Id = string.Empty;
        public string Title = "Capitulo";
        public string Date = string.Empty;
        public double Number;
        public bool Locked;
        public string PreviousId;
        public string NextId;
    }

    public sealed class PageSet
    {
        public List<string> Images = new List<string>();
        public string Text = string.Empty;

        public bool IsText => string.IsNullOrWhiteSpace(Text) == false && Images.Count == 0;
    }

    public sealed class CatalogResult
    {
        public List<Manga> Items = new List<Manga>();
        public int Page = 1;
        public bool HasMore;
        public long Total;
    }

    public sealed class SourceQuery
    {
        public int Page = 1;
        public int Limit = 28;
        public string Q = string.Empty;
        public string Genre = string.Empty;
        public bool Adult;
    }

    public sealed class ChapterResult
    {
        public Chapter Chapter = new Chapter();
        public Manga Manga = new Manga();
        public PageSet Pages = new PageSet();
    }

    /// <summary>Página que consegue se atualizar quando volta ao topo da pilha.</summary>
    public interface IRefreshable
    {
        void Refresh();
    }

    /// <summary>Página avisada quando o modo +18 global muda.</summary>
    public interface IAdultAware
    {
        void AdultChanged();
    }

    /// <summary>Página ligada a uma fonte (usado para realinhar a barra lateral ao voltar).</summary>
    public interface IPageSource
    {
        string PageSource { get; }
    }

    public static class ChapterOrder
    {
        /// <summary>
        /// Devolve os capítulos em ordem crescente. A maioria das fontes numera os
        /// capítulos, mas algumas listam do mais novo para o mais antigo; quando não
        /// há numeração confiável, preserva a ordem original da fonte.
        /// </summary>
        public static List<Chapter> Ascending(IEnumerable<Chapter> chapters)
        {
            var list = (chapters ?? Enumerable.Empty<Chapter>()).ToList();
            if (list.Count < 2) return list;
            var numbered = list.Count(c => c.Number > 0);
            if (numbered >= Math.Max(2, (int)Math.Ceiling(list.Count * 0.6)))
            {
                list.Sort((a, b) =>
                {
                    var c = a.Number.CompareTo(b.Number);
                    if (c != 0) return c;
                    return string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase);
                });
            }
            return list;
        }

        public static int IndexOf(List<Chapter> list, string chapterId)
        {
            if (list == null || string.IsNullOrEmpty(chapterId)) return -1;
            for (var i = 0; i < list.Count; i++)
                if (string.Equals(list[i].Id, chapterId, StringComparison.Ordinal)) return i;
            return -1;
        }
    }

    public static class TitleTools
    {
        /// <summary>Minúsculas, sem acentos e sem pontuação, para comparar títulos entre fontes.</summary>
        public static string Fold(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            var s = value.Normalize(System.Text.NormalizationForm.FormD);
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (var c in s)
            {
                if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) ==
                    System.Globalization.UnicodeCategory.NonSpacingMark) continue;
                if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
                else sb.Append(' ');
            }
            return System.Text.RegularExpressions.Regex.Replace(sb.ToString(), "\\s+", " ").Trim();
        }

        /// <summary>0 = igual, 1 = começa com, 2 = contém, 3 = todas as palavras, 4 = resto.</summary>
        public static int Score(string title, string query)
        {
            var t = Fold(title);
            var q = Fold(query);
            if (q.Length == 0) return 4;
            if (t == q) return 0;
            if (t.StartsWith(q, StringComparison.Ordinal)) return 1;
            if (t.Contains(q)) return 2;
            var words = q.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length > 0 && words.All(w => t.Contains(w))) return 3;
            return 4;
        }
    }
}
