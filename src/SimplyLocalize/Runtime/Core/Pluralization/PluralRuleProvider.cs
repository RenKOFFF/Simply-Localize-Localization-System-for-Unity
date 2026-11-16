using System;
using System.Collections.Generic;

namespace SimplyLocalize.Pluralization
{
    /// <summary>
    /// Maps language codes to their plural rules based on CLDR.
    /// Caches rule instances — each rule is created once and reused.
    /// </summary>
    public static class PluralRuleProvider
    {
        private static readonly Dictionary<string, PluralRuleInfo> Cache = new();
        private static readonly Dictionary<string, IPluralRule> Overrides = new();
        private static readonly object Sync = new();

        private static readonly GermanicPluralRule Germanic = new();
        private static readonly SlavicPluralRule Slavic = new();
        private static readonly RomancePluralRule Romance = new();
        private static readonly EastAsianPluralRule EastAsian = new();
        private static readonly ArabicPluralRule Arabic = new();
        private static readonly IPluralRule ExactlyOne = new CardinalPluralRule(CardinalKind.ExactlyOne);
        private static readonly IPluralRule Danish = new CardinalPluralRule(CardinalKind.Danish);
        private static readonly IPluralRule Icelandic = new CardinalPluralRule(CardinalKind.Icelandic);
        private static readonly IPluralRule Belarusian = new CardinalPluralRule(CardinalKind.Belarusian);
        private static readonly IPluralRule Polish = new CardinalPluralRule(CardinalKind.Polish);
        private static readonly IPluralRule Czech = new CardinalPluralRule(CardinalKind.Czech);
        private static readonly IPluralRule Slovenian = new CardinalPluralRule(CardinalKind.Slovenian);
        private static readonly IPluralRule Serbian = new CardinalPluralRule(CardinalKind.Serbian);
        private static readonly IPluralRule Romanian = new CardinalPluralRule(CardinalKind.Romanian);
        private static readonly IPluralRule Italian = new CardinalPluralRule(CardinalKind.Italian);
        private static readonly IPluralRule Spanish = new CardinalPluralRule(CardinalKind.Spanish);
        private static readonly IPluralRule Occitan = new LegacyOccitanRule();

        /// <summary>
        /// Returns the plural rule for the given language code.
        /// Supports both short codes ("en") and regional codes ("en-US", "pt-BR").
        /// Falls back to Germanic (English-like) if the language is not recognized.
        /// </summary>
        public static IPluralRule GetRule(string languageCode) => GetRuleInfo(languageCode).Rule;

        public static PluralRuleInfo GetRuleInfo(string languageCode)
        {
            if (string.IsNullOrWhiteSpace(languageCode))
                return new PluralRuleInfo(Germanic, PluralRuleSupport.Default, "en");

            string code = Normalize(languageCode);
            lock (Sync)
            {
                if (Cache.TryGetValue(code, out var cached)) return cached;
                // Exact override first, then parent tags, then the built-in rule.
                string parent = code;
                while (true)
                {
                    if (Overrides.TryGetValue(parent, out var custom)) return Cache[code] = new PluralRuleInfo(custom, PluralRuleSupport.Custom, parent);
                    int separator = parent.LastIndexOf('-');
                    if (separator < 0) break;
                    parent = parent.Substring(0, separator);
                }
                var rule = ResolveRule(parent, code);
                return Cache[code] = rule != null
                    ? new PluralRuleInfo(rule, PluralRuleSupport.BuiltIn, parent)
                    : new PluralRuleInfo(Germanic, PluralRuleSupport.Default, "en");
            }
        }

        /// <summary>
        /// Registers a custom plural rule for a language code.
        /// Use this to override built-in rules or add support for new languages.
        /// Codes are case-insensitive; '-' and '_' are equivalent. Parent overrides apply
        /// to regional codes unless a more specific override exists. Invalidates cached lookups.
        /// </summary>
        public static void RegisterRule(string languageCode, IPluralRule rule)
        {
            if (string.IsNullOrWhiteSpace(languageCode)) throw new ArgumentException("A language code is required.", nameof(languageCode));
            if (rule == null) throw new ArgumentNullException(nameof(rule));
            lock (Sync)
            {
                Overrides[Normalize(languageCode)] = rule;
                Cache.Clear();
            }
        }

        private static IPluralRule ResolveRule(string baseCode, string code)
        {
            return baseCode switch
            {
                "en" or "de" or "nl" or "sv" or "fy" or "fi" or "et" or "gl" => Germanic,
                "no" or "nb" or "nn" or "fo" or "af" or "ka" or "tr" or "hu" => ExactlyOne,
                "da" => Danish,
                "is" => Icelandic,
                "ru" or "uk" => Slavic,
                "be" => Belarusian,
                "pl" => Polish,
                "cs" or "sk" => Czech,
                "sl" => Slovenian,
                "hr" or "sr" or "bs" => Serbian,
                "ro" => Romanian,
                "fr" => Romance,
                "pt" => UsesEuropeanPortuguese(code) ? Italian : Romance,
                "it" or "ca" => Italian,
                "es" => Spanish,
                // Preserve the previous Occitan rule; it has no entry in CLDR 48 plurals.xml.
                "oc" => Occitan,
                "ja" or "zh" or "ko" or "vi" or "th" or "id" or "ms"
                    or "my" or "lo" or "km" => EastAsian,
                "ar" => Arabic,
                _ => null
            };
        }

        private static bool UsesEuropeanPortuguese(string code)
        {
            var parts = code.Split('-');
            for (int i = 1; i < parts.Length; i++)
            {
                if (parts[i].Length == 1) break; // Stop before BCP-47 extensions/private use.
                if (parts[i].Length == 2)
                    return parts[i] is "pt" or "ao" or "ch" or "cv" or "fr" or "gq" or "gw"
                        or "lu" or "mo" or "mz" or "st" or "tl";
            }
            return false;
        }

        private static string Normalize(string code) => code.Trim().Replace('_', '-').ToLowerInvariant();

        internal static void ResetRuntimeState()
        {
            lock (Sync)
            {
                Cache.Clear();
                Overrides.Clear();
            }
        }

        private sealed class LegacyOccitanRule : IPluralRule
        {
            public int FormCount => 2;
            public int Evaluate(int count) => count >= -1 && count <= 1 ? 0 : 1;
        }
    }
}
