using System.Collections.Generic;
using System.Linq;

namespace SimplyLocalize.Editor.Utilities
{
    public struct LanguageCoverage
    {
        public string LanguageCode;
        public string DisplayName;
        public int TranslatedCount;
        public int TotalCount;
        public float Percentage;
        public List<string> MissingKeys;
    }

    public struct CoverageWarning
    {
        public enum Severity { Info, Warning, Error }

        public Severity Level;
        public string Message;
        public string Key;
        public string LanguageCode;
    }

    /// <summary>
    /// Analyzes translation coverage across all languages and produces warnings.
    /// </summary>
    public static class CoverageAnalyzer
    {
        public static List<LanguageCoverage> ComputeCoverage(
            Data.EditorLocalizationData data,
            LocalizationConfig config)
        {
            var result = new List<LanguageCoverage>();
            int totalKeys = data.KeyCount;

            foreach (var profile in config.languages)
            {
                if (profile == null) continue;

                string langCode = profile.Code;
                var missing = new List<string>();
                int translated = 0;

                foreach (var key in data.AllKeys)
                {
                    string value = data.GetTranslation(key, langCode);

                    if (!string.IsNullOrEmpty(value))
                        translated++;
                    else
                        missing.Add(key);
                }

                result.Add(new LanguageCoverage
                {
                    LanguageCode = langCode,
                    DisplayName = profile.displayName,
                    TranslatedCount = translated,
                    TotalCount = totalKeys,
                    Percentage = totalKeys > 0 ? (float)translated / totalKeys : 1f,
                    MissingKeys = missing
                });
            }

            return result;
        }

        public static List<CoverageWarning> ComputeWarnings(
            Data.EditorLocalizationData data,
            LocalizationConfig config)
        {
            var warnings = new List<CoverageWarning>();

            string refLang = config.DefaultLanguageCode
                ?? (config.languages.Count > 0 ? config.languages[0].Code : null);

            if (string.IsNullOrEmpty(refLang))
                return warnings;

            foreach (var key in data.AllKeys)
            {
                string refValue = data.GetTranslation(key, refLang);
                var refParams = ExtractParams(refValue);

                foreach (var profile in config.languages)
                {
                    if (profile == null || profile.Code == refLang) continue;

                    string value = data.GetTranslation(key, profile.Code);

                    // Missing translation
                    if (string.IsNullOrEmpty(value))
                    {
                        warnings.Add(new CoverageWarning
                        {
                            Level = CoverageWarning.Severity.Warning,
                            Message = $"Missing translation for '{key}' in {profile.displayName}",
                            Key = key,
                            LanguageCode = profile.Code
                        });
                        continue;
                    }

                    // Parameter mismatch
                    var langParams = ExtractParams(value);

                    if (refParams.Count > 0 && !refParams.SetEquals(langParams))
                    {
                        warnings.Add(new CoverageWarning
                        {
                            Level = CoverageWarning.Severity.Error,
                            Message = $"Parameter mismatch in '{key}': " +
                                      $"{refLang} has {{{string.Join(", ", refParams)}}} " +
                                      $"but {profile.Code} has {{{string.Join(", ", langParams)}}}",
                            Key = key,
                            LanguageCode = profile.Code
                        });
                    }
                }
            }

            return warnings;
        }

        /// <summary>
        /// Extracts parameter identifiers from a translation string.
        /// E.g. "{0} {playerName} {0|coin|coins}" → {"0", "playerName"}
        /// </summary>
        private static HashSet<string> ExtractParams(string text)
        {
            var result = new HashSet<string>();
            if (string.IsNullOrEmpty(text)) return result;

            int pos = 0;
            while (pos < text.Length)
            {
                if (text[pos] == '{' && pos + 1 < text.Length && text[pos + 1] != '{')
                {
                    int close = text.IndexOf('}', pos + 1);
                    if (close > pos)
                    {
                        string content = text.Substring(pos + 1, close - pos - 1);
                        int pipe = content.IndexOf('|');
                        string id = pipe >= 0 ? content.Substring(0, pipe).Trim() : content.Trim();

                        if (id.Length > 0)
                            result.Add(id);

                        pos = close + 1;
                        continue;
                    }
                }

                pos++;
            }

            return result;
        }
    }
}
