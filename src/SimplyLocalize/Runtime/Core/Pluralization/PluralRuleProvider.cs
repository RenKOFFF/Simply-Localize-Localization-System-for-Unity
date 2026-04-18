using System.Collections.Generic;

namespace SimplyLocalize.Pluralization
{
    /// <summary>
    /// Maps language codes to their plural rules based on CLDR.
    /// Caches rule instances — each rule is created once and reused.
    /// </summary>
    public static class PluralRuleProvider
    {
        private static readonly Dictionary<string, IPluralRule> Cache = new();

        private static readonly GermanicPluralRule Germanic = new();
        private static readonly SlavicPluralRule Slavic = new();
        private static readonly RomancePluralRule Romance = new();
        private static readonly EastAsianPluralRule EastAsian = new();
        private static readonly ArabicPluralRule Arabic = new();

        /// <summary>
        /// Returns the plural rule for the given language code.
        /// Supports both short codes ("en") and regional codes ("en-US", "pt-BR").
        /// Falls back to Germanic (English-like) if the language is not recognized.
        /// </summary>
        public static IPluralRule GetRule(string languageCode)
        {
            if (string.IsNullOrEmpty(languageCode))
                return Germanic;

            if (Cache.TryGetValue(languageCode, out var cached))
                return cached;

            string baseCode = GetBaseCode(languageCode);
            IPluralRule rule = ResolveRule(baseCode);
            Cache[languageCode] = rule;
            return rule;
        }

        /// <summary>
        /// Registers a custom plural rule for a language code.
        /// Use this to override built-in rules or add support for new languages.
        /// </summary>
        public static void RegisterRule(string languageCode, IPluralRule rule)
        {
            Cache[languageCode] = rule;
        }

        private static IPluralRule ResolveRule(string baseCode)
        {
            return baseCode switch
            {
                // Germanic
                "en" or "de" or "nl" or "sv" or "da" or "no" or "nb" or "nn"
                    or "is" or "fo" or "af" or "fy" => Germanic,

                // Slavic
                "ru" or "uk" or "be" or "pl" or "hr" or "sr" or "bs"
                    or "cs" or "sk" or "sl" => Slavic,

                // Romance
                "fr" or "es" or "it" or "pt" or "ca" or "gl" or "ro"
                    or "oc" => Romance,

                // East Asian and no-plural languages
                "ja" or "zh" or "ko" or "vi" or "th" or "id" or "ms"
                    or "my" or "lo" or "km" or "ka" or "tr" or "hu"
                    or "fi" or "et" => EastAsian,

                // Arabic
                "ar" => Arabic,

                // Default fallback
                _ => Germanic
            };
        }

        private static string GetBaseCode(string languageCode)
        {
            int dashIndex = languageCode.IndexOf('-');
            if (dashIndex > 0)
                return languageCode.Substring(0, dashIndex).ToLowerInvariant();

            int underscoreIndex = languageCode.IndexOf('_');
            if (underscoreIndex > 0)
                return languageCode.Substring(0, underscoreIndex).ToLowerInvariant();

            return languageCode.ToLowerInvariant();
        }
    }
}
