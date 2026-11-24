using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace SimplyLocalize
{
    /// <summary>Canonical supported BCP-47 tags. Private languages use x-...; paths and names are rejected.</summary>
    public static class LocalizationLanguageCode
    {
        private static readonly Regex Tag = new(@"^(?:[a-z]{2,3}(?:-[a-z]{4})?(?:-(?:[a-z]{2}|[0-9]{3}))?(?:-(?:[a-z0-9]{5,8}|[0-9][a-z0-9]{3}))*|x(?:-[a-z0-9]{1,8})+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly HashSet<string> Languages = new(LanguageSubtags.Language.Split(' '), StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> Scripts = new(LanguageSubtags.Script.Split(' '), StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> Regions = new(LanguageSubtags.Region.Split(' '), StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> Variants = new(LanguageSubtags.Variant.Split(' '), StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> Aliases = BuildAliases();

        public static string Normalize(string input)
        {
            string code = (input ?? "").Trim().Replace('_', '-');
            if (code.Equals("zh-CHS", StringComparison.OrdinalIgnoreCase)) return "zh-Hans";
            if (code.Equals("zh-CHT", StringComparison.OrdinalIgnoreCase)) return "zh-Hant";
            if (!Tag.IsMatch(code)) throw new ArgumentException("Use a language code such as en, ru, pt-BR or zh-Hans; use x-... for private languages.", nameof(input));
            string[] parts = code.ToLowerInvariant().Split('-');
            if (parts[0] == "x") return string.Join("-", parts);
            if (Aliases.TryGetValue(parts[0], out string alias)) parts[0] = alias;
            if (!Languages.Contains(parts[0])) throw new ArgumentException("Unknown language code: " + parts[0], nameof(input));
            int index = 1;
            if (index < parts.Length && parts[index].Length == 4 && char.IsLetter(parts[index][0]))
            {
                if (!Scripts.Contains(parts[index])) throw new ArgumentException("Unknown script code: " + parts[index], nameof(input));
                parts[index] = char.ToUpperInvariant(parts[index][0]) + parts[index].Substring(1);
                index++;
            }
            if (index < parts.Length && (parts[index].Length == 2 || parts[index].Length == 3))
            {
                if (!Regions.Contains(parts[index])) throw new ArgumentException("Unknown region code: " + parts[index], nameof(input));
                parts[index] = parts[index].ToUpperInvariant();
                index++;
            }
            var variants = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (; index < parts.Length; index++)
                if (!Variants.Contains(parts[index]) || !variants.Add(parts[index]))
                    throw new ArgumentException("Unknown or repeated language variant: " + parts[index], nameof(input));
            return string.Join("-", parts);
        }

        public static bool IsCanonical(string code)
        {
            try { return string.Equals(code, Normalize(code), StringComparison.Ordinal); }
            catch (ArgumentException) { return false; }
        }

        private static Dictionary<string, string> BuildAliases()
        {
            var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            { ["iw"] = "he", ["in"] = "id", ["ji"] = "yi" };
            foreach (var culture in CultureInfo.GetCultures(CultureTypes.NeutralCultures))
                if (culture.TwoLetterISOLanguageName.Length == 2 && culture.ThreeLetterISOLanguageName.Length == 3)
                    aliases[culture.ThreeLetterISOLanguageName] = culture.TwoLetterISOLanguageName;
            return aliases;
        }
    }
}
