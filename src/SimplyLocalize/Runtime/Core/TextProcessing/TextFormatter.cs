using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using SimplyLocalize.Pluralization;

namespace SimplyLocalize.TextProcessing
{
    /// <summary>
    /// Formats parsed translation templates by substituting parameters
    /// and resolving plural forms via CLDR plural rules.
    ///
    /// Two types of parameters:
    ///   - Indexed: {0}, {1} — resolved from the args array
    ///   - Named: {playerName}, {level} — resolved from the namedArgs dictionary
    ///
    /// Plural forms ({0|coin|coins}, {count|яблоко|яблока|яблок}) are resolved
    /// by evaluating the parameter's numeric value through the current language's plural rule.
    ///
    /// Uses a pooled StringBuilder to minimize GC allocations.
    /// </summary>
    public static class TextFormatter
    {
        [System.ThreadStatic]
        private static StringBuilder _sb;

        private static StringBuilder GetBuilder()
        {
            var sb = _sb ?? new StringBuilder(256);
            // Take ownership until formatting completes. User ToString/rules may reenter Format.
            _sb = null;
            sb.Clear();
            return sb;
        }

        /// <summary>
        /// Formats a template with indexed parameters only.
        /// </summary>
        public static string Format(ParsedTemplate template, string languageCode, object[] args)
        {
            return Format(template, languageCode, args, null);
        }

        /// <summary>
        /// Formats a template with named parameters only.
        /// </summary>
        public static string Format(ParsedTemplate template, string languageCode,
            Dictionary<string, object> namedArgs)
        {
            return Format(template, languageCode, null, namedArgs);
        }

        /// <summary>
        /// Formats a template with both indexed and named parameters.
        /// </summary>
        public static string Format(ParsedTemplate template, string languageCode,
            object[] args, Dictionary<string, object> namedArgs)
        {
            if (template == null)
                return string.Empty;

            var tokens = template.Tokens;

            if (tokens == null || tokens.Count == 0)
                return string.Empty;

            // Fast path: single text token
            if (tokens.Count == 1 && tokens[0].Type == TokenType.Text)
                return tokens[0].Text;

            var sb = GetBuilder();
            IPluralRule pluralRule = null;

            try
            {
                for (int i = 0; i < tokens.Count; i++)
                {
                    var token = tokens[i];
                    if (token.Type == TokenType.Text)
                    {
                        sb.Append(token.Text);
                        continue;
                    }
                    object value = token.Type == TokenType.IndexedParam
                        ? GetIndexedValue(args, token.Index)
                        : GetNamedValue(namedArgs, token.Name);
                    if (token.HasForms)
                    {
                        if (value is not FormSelector)
                            pluralRule ??= PluralRuleProvider.GetRule(languageCode);
                        AppendForm(sb, token.Forms, value, pluralRule);
                    }
                    else if (value == null && token.RawText != null)
                    {
                        sb.Append(token.RawText);
                    }
                    else if (token.Type == TokenType.IndexedParam)
                        AppendValue(sb, value, token.Index);
                    else
                        AppendValue(sb, value, token.Name);
                }
                return sb.ToString();
            }
            finally
            {
                // Avoid retaining a one-off giant translation for the lifetime of the thread.
                if (sb.Capacity <= 16384) _sb = sb;
            }
        }

        private static void AppendForm(StringBuilder sb, string[] forms, object value, IPluralRule rule)
        {
            if (forms.Length == 0)
                return;

            int formIndex;
            if (value is FormSelector selector)
                formIndex = selector.Index;
            else if (value is int integer)
                formIndex = rule.Evaluate(integer);
            else
            {
                decimal count = ToNumber(value);
                formIndex = rule is IDecimalPluralRule numericRule
                    ? numericRule.Evaluate(count)
                    // Legacy custom rules accept only Int32. Keep truncation, avoid wraparound.
                    : rule.Evaluate(count > int.MaxValue ? int.MaxValue : count < int.MinValue ? int.MinValue : (int)count);
            }

            // Clamp to available forms
            if (formIndex >= forms.Length)
                formIndex = forms.Length - 1;
            if (formIndex < 0)
                formIndex = 0;

            sb.Append(forms[formIndex]);
        }

        private static void AppendValue(StringBuilder sb, object value, int indexFallback)
        {
            if (value != null)
                sb.Append(value);
            else
                sb.Append('{').Append(indexFallback).Append('}');
        }

        private static void AppendValue(StringBuilder sb, object value, string nameFallback)
        {
            if (value != null)
                sb.Append(value);
            else
                sb.Append('{').Append(nameFallback).Append('}');
        }

        private static object GetIndexedValue(object[] args, int index)
        {
            if (args != null && index >= 0 && index < args.Length)
                return args[index];
            return null;
        }

        private static object GetNamedValue(Dictionary<string, object> namedArgs, string name)
        {
            if (name != null && namedArgs != null && namedArgs.TryGetValue(name, out var value))
                return value;
            return null;
        }

        private static decimal ToNumber(object value)
        {
            if (value == null) return 0;

            switch (value)
            {
                case decimal number: return number;
                case sbyte or byte or short or ushort or int or uint or long or ulong:
                    return Convert.ToDecimal(value, CultureInfo.InvariantCulture);
                case float number:
                    return ParseNumber(number.ToString("R", CultureInfo.InvariantCulture));
                case double number:
                    return ParseNumber(number.ToString("R", CultureInfo.InvariantCulture));
                default: return ParseNumber(value.ToString());
            }
        }

        // Preserve the legacy zero default for missing/non-numeric values. NaN, infinity,
        // and values outside Decimal's range use that same deterministic default.
        private static decimal ParseNumber(string value) =>
            decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : 0;
    }
}
