using System;
using System.Collections.Generic;

namespace SimplyLocalize.Pluralization
{
    /// <summary>Instance-local overrides. Built-ins are immutable; custom rule objects are supplied and owned by the caller.</summary>
    public sealed class LocalizationPluralRules
    {
        private readonly Dictionary<string, IPluralRule> overrides = new(StringComparer.Ordinal);
        private readonly Dictionary<string, PluralRuleInfo> cache = new(StringComparer.Ordinal);
        public void Register(string language, IPluralRule rule)
        {
            string code = LocalizationLanguageCode.Normalize(language);
            overrides[code] = rule ?? throw new ArgumentNullException(nameof(rule)); cache.Clear();
        }
        public PluralRuleInfo GetRuleInfo(string language)
        {
            string code = LocalizationLanguageCode.Normalize(language);
            if (cache.TryGetValue(code, out var result)) return result;
            string parent = code;
            while (true)
            {
                if (overrides.TryGetValue(parent, out var rule)) return cache[code] = new(rule, PluralRuleSupport.Custom, parent);
                int separator = parent.LastIndexOf('-'); if (separator < 0) break;
                parent = parent.Substring(0, separator);
            }
            return cache[code] = PluralRuleProvider.GetBuiltInRuleInfo(code);
        }
        public LocalizationPluralRules Copy()
        {
            var result = new LocalizationPluralRules();
            foreach (var pair in overrides) result.overrides.Add(pair.Key, pair.Value);
            return result;
        }
    }
}
