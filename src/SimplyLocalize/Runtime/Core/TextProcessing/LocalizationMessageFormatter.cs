using System;
using System.Collections.Generic;
using System.Globalization;
using SimplyLocalize.Pluralization;

namespace SimplyLocalize.TextProcessing
{
    public enum LocalizationValueCulture { ResolvedLanguage, RequestedLanguage, Invariant }

    /// <summary>Implement in the game to replace message formatting. Ready reads must not start I/O.</summary>
    public interface ILocalizationTextFormatter
    {
        string Format(LocalizationTextValue value, object[] args, Dictionary<string, object> namedArgs);
    }

    /// <summary>Instance-owned bounded template cache. Plurals always use the resolved template's language.</summary>
    public sealed class LocalizationMessageFormatter : ILocalizationTextFormatter
    {
        private readonly Dictionary<string, ParsedTemplate> templates = new(StringComparer.Ordinal);
        private readonly Queue<string> insertionOrder = new();
        private readonly LocalizationPluralRules rules;
        public int MaxTemplates { get; }
        public int CachedTemplateCount => templates.Count;
        public LocalizationValueCulture ValueCulture { get; }
        public LocalizationMessageFormatter(int maxTemplates = 1024, LocalizationPluralRules pluralRules = null,
            LocalizationValueCulture valueCulture = LocalizationValueCulture.ResolvedLanguage)
        {
            if (maxTemplates < 1) throw new ArgumentOutOfRangeException(nameof(maxTemplates));
            if (!Enum.IsDefined(typeof(LocalizationValueCulture), valueCulture)) throw new ArgumentOutOfRangeException(nameof(valueCulture));
            MaxTemplates = maxTemplates; ValueCulture = valueCulture; rules = (pluralRules ?? new LocalizationPluralRules()).Copy();
        }
        public PluralRuleInfo GetPluralRuleInfo(string language) => rules.GetRuleInfo(language);
        public string Format(LocalizationTextValue value, object[] args, Dictionary<string, object> namedArgs)
        {
            if (string.IsNullOrEmpty(value.Text)) return value.Text ?? "";
            if (!templates.TryGetValue(value.Text, out var template))
            {
                template = TokenParser.Parse(value.Text);
                if (templates.Count >= MaxTemplates) templates.Remove(insertionOrder.Dequeue());
                templates.Add(value.Text, template); insertionOrder.Enqueue(value.Text);
            }
            string cultureCode = ValueCulture == LocalizationValueCulture.RequestedLanguage ? value.RequestedLanguage : value.Language;
            IFormatProvider culture = CultureInfo.InvariantCulture;
            if (ValueCulture != LocalizationValueCulture.Invariant)
                try { culture = CultureInfo.GetCultureInfo(cultureCode); } catch (CultureNotFoundException) { }
            return TextFormatter.Format(template, rules.GetRuleInfo(value.Language).Rule, culture, args, namedArgs);
        }
        public void ClearCache() { templates.Clear(); insertionOrder.Clear(); }
    }
}
