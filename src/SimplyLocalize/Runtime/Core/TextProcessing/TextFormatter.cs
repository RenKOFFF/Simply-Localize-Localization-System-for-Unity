using System.Collections.Generic;
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
            var sb = _sb ??= new StringBuilder(256);
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

            if (tokens.Count == 0)
                return string.Empty;

            // Fast path: single text token
            if (tokens.Count == 1 && tokens[0].Type == TokenType.Text)
                return tokens[0].Text;

            var sb = GetBuilder();
            IPluralRule pluralRule = null;

            for (int i = 0; i < tokens.Count; i++)
            {
                var token = tokens[i];

                switch (token.Type)
                {
                    case TokenType.Text:
                        sb.Append(token.Text);
                        break;

                    case TokenType.IndexedParam:
                    {
                        object value = GetIndexedValue(args, token.Index);

                        if (token.HasForms)
                        {
                            pluralRule ??= PluralRuleProvider.GetRule(languageCode);
                            AppendForm(sb, token.Forms, value, pluralRule);
                        }
                        else
                        {
                            AppendValue(sb, value, token.Index);
                        }

                        break;
                    }

                    case TokenType.NamedParam:
                    {
                        object value = GetNamedValue(namedArgs, token.Name);

                        if (token.HasForms)
                        {
                            pluralRule ??= PluralRuleProvider.GetRule(languageCode);
                            AppendForm(sb, token.Forms, value, pluralRule);
                        }
                        else
                        {
                            AppendValue(sb, value, token.Name);
                        }

                        break;
                    }
                }
            }

            return sb.ToString();
        }

        private static void AppendForm(StringBuilder sb, string[] forms, object value, IPluralRule rule)
        {
            if (forms.Length == 0)
                return;

            int count = ToInt(value);
            int formIndex = rule.Evaluate(count);

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
            if (namedArgs != null && namedArgs.TryGetValue(name, out var value))
                return value;
            return null;
        }

        private static int ToInt(object value)
        {
            if (value == null) return 0;

            return value switch
            {
                int i => i,
                long l => (int)l,
                float f => (int)f,
                double d => (int)d,
                short s => s,
                byte b => b,
                _ => int.TryParse(value.ToString(), out int parsed) ? parsed : 0
            };
        }
    }
}