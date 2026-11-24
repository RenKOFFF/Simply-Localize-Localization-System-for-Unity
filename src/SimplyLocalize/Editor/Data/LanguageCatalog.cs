using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace SimplyLocalize.Editor.Data
{
    internal sealed class LanguageDefinition
    {
        internal string Code;
        internal string EnglishName;
        internal string RussianName;
        internal string NativeName;
        internal string Iso3;
        internal string SearchText;
        internal SystemLanguage SystemLanguage;
        internal string Label => EnglishName + " [" + Code + "]";
    }

    internal static class LanguageCatalog
    {
        private static readonly Regex CustomTag = new Regex(@"^(?:[a-z]{2,3}(?:-[a-z]{4})?(?:-(?:[a-z]{2}|[0-9]{3}))?(?:-(?:[a-z0-9]{5,8}|[0-9][a-z0-9]{3}))*|x(?:-[a-z0-9]{1,8})+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Dictionary<string, LanguageDefinition> Entries = Build();

        internal static LanguageDefinition Find(string code) => Entries.TryGetValue(code, out var entry) ? entry : null;

        internal static List<string> Search(string query)
        {
            string term = Normalize(query.Trim());
            bool Prefix(LanguageDefinition entry) => entry.SearchText.StartsWith(term, StringComparison.Ordinal) || entry.SearchText.Contains(" " + term);
            return Entries.Values.Where(e => term.Length == 0 || Prefix(e) || (term.Length > 2 && e.SearchText.Contains(term)))
                .OrderBy(e => string.Equals(e.Code, term, StringComparison.OrdinalIgnoreCase) || e.Iso3 == term ? 0 :
                    e.Code.StartsWith(term, StringComparison.OrdinalIgnoreCase) ? 1 : Prefix(e) ? 2 : 3)
                .ThenBy(e => e.Code.Contains("-") ? 1 : 0)
                .ThenBy(e => e.EnglishName, StringComparer.OrdinalIgnoreCase)
                .Select(e => e.Code).ToList();
        }

        internal static LanguageDefinition Custom(string code, string displayName)
        {
            string normalized = CanonicalCode(code);
            var known = Find(normalized);
            if (known != null) return known;
            if (string.IsNullOrWhiteSpace(displayName)) throw new ArgumentException("Enter a display name for the custom language.");
            var entry = new LanguageDefinition { Code = normalized, EnglishName = displayName.Trim(), NativeName = displayName.Trim(), RussianName = "", SystemLanguage = SystemLanguage.Unknown };
            Index(entry);
            return entry;
        }

        internal static string CanonicalCode(string input) => LocalizationLanguageCode.Normalize(input);

        private static Dictionary<string, LanguageDefinition> Build()
        {
            var result = new Dictionary<string, LanguageDefinition>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in LanguageCatalogData.Names.Split('\n'))
            {
                var columns = line.TrimEnd('\r').Split('|');
                result.Add(columns[0], new LanguageDefinition { Code = columns[0], EnglishName = columns[1], RussianName = columns[2], NativeName = columns[1], SystemLanguage = SystemLanguage.Unknown });
            }
            foreach (var culture in CultureInfo.GetCultures(CultureTypes.NeutralCultures | CultureTypes.SpecificCultures))
            {
                if (culture.Name.Length == 0 || !CustomTag.IsMatch(culture.Name) || culture.Name == "zh-CHS" || culture.Name == "zh-CHT") continue;
                if (!result.TryGetValue(culture.Name, out var entry))
                {
                    result.TryGetValue(culture.TwoLetterISOLanguageName, out var parent);
                    entry = new LanguageDefinition { Code = culture.Name, EnglishName = culture.EnglishName, RussianName = parent?.RussianName ?? "", SystemLanguage = SystemLanguage.Unknown };
                    result.Add(culture.Name, entry);
                }
                entry.NativeName = culture.NativeName;
                entry.Iso3 = culture.ThreeLetterISOLanguageName;
            }
            foreach (var entry in result.Values)
            {
                string language = entry.Code.Split('-')[0];
                string english = result.TryGetValue(language, out var parent) ? parent.EnglishName : entry.EnglishName;
                if (Enum.TryParse(english, out SystemLanguage system) && system != SystemLanguage.Unknown) entry.SystemLanguage = system;
                if (language == "nb" || language == "nn") entry.SystemLanguage = SystemLanguage.Norwegian;
                if (language == "sh") entry.SystemLanguage = SystemLanguage.SerboCroatian;
                if (language == "zh") entry.SystemLanguage = entry.Code.StartsWith("zh-Hant") || entry.Code == "zh-TW" || entry.Code == "zh-HK" || entry.Code == "zh-MO" ? SystemLanguage.ChineseTraditional : entry.Code == "zh" ? SystemLanguage.Chinese : SystemLanguage.ChineseSimplified;
                Index(entry);
            }
            return result;
        }

        private static void Index(LanguageDefinition entry)
        {
            entry.SearchText = Normalize(string.Join(" ", entry.Code, entry.EnglishName, entry.RussianName, entry.NativeName, entry.Iso3, Transliterate(entry.RussianName)));
        }

        private static string Normalize(string text)
        {
            var result = new StringBuilder();
            foreach (char c in (text ?? "").ToLowerInvariant().Normalize(NormalizationForm.FormD))
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) result.Append(c);
            return result.ToString();
        }

        private static string Transliterate(string text)
        {
            const string letters = "абвгдеёжзийклмнопрстуфхцчшщъыьэюя";
            string[] latin = { "a", "b", "v", "g", "d", "e", "e", "zh", "z", "i", "y", "k", "l", "m", "n", "o", "p", "r", "s", "t", "u", "f", "kh", "ts", "ch", "sh", "shch", "", "y", "", "e", "yu", "ya" };
            var result = new StringBuilder();
            foreach (char c in text.ToLowerInvariant())
            {
                int index = letters.IndexOf(c);
                result.Append(index < 0 ? c.ToString() : latin[index]);
            }
            return result.ToString();
        }
    }
}
