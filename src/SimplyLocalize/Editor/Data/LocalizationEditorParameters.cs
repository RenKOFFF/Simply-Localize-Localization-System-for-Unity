using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SimplyLocalize.Components;
using SimplyLocalize.Pluralization;
using SimplyLocalize.TextProcessing;
using UnityEditor;

namespace SimplyLocalize.Editor.Data
{
    public static class LocalizationEditorParameters
    {
        public static IReadOnlyList<string> Names(string template) => TokenParser.Parse(template ?? "").Tokens
            .Where(t => t.Type == TokenType.IndexedParam && t.Index >= 0 || t.Type == TokenType.NamedParam)
            .Select(t => t.Type == TokenType.IndexedParam ? t.Index.ToString(CultureInfo.InvariantCulture) : t.Name).Distinct(StringComparer.Ordinal).ToArray();
        /// <summary>Explicitly adds missing parameters. Existing and extra values are preserved.</summary>
        public static void AddMissing(SerializedObject target, string template)
        {
            if (target == null || target.targetObjects.Length != 1) throw new ArgumentException("Select one formattable text component.", nameof(target));
            var names = Names(template);
            if (names.Any(name => int.TryParse(name, out int index) && index > FormattableLocalizedText.MaxParameterIndex))
                throw new InvalidOperationException("Use parameter indices up to 65535 or named parameters before adding defaults.");
            target.Update(); var list = target.FindProperty("_defaultNamedParams") ?? throw new ArgumentException("Expected a formattable text component.");
            var existing = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < list.arraySize; i++) existing.Add(list.GetArrayElementAtIndex(i).FindPropertyRelative("name").stringValue);
            foreach (string name in names)
            {
                if (!existing.Add(name)) continue;
                int index = list.arraySize++; var item = list.GetArrayElementAtIndex(index);
                item.FindPropertyRelative("name").stringValue = name; item.FindPropertyRelative("value").stringValue = "";
            }
            target.ApplyModifiedProperties();
        }
        public static string Format(string template, string language, SerializedProperty parameters, LocalizationPluralRules rules = null)
        {
            var indexed = new Dictionary<int, object>(); var named = new Dictionary<string, object>(StringComparer.Ordinal);
            for (int i = 0; i < parameters.arraySize; i++)
            {
                var item = parameters.GetArrayElementAtIndex(i); string name = item.FindPropertyRelative("name").stringValue;
                if (string.IsNullOrEmpty(name)) continue;
                object value = new FormattableLocalizedText.NamedParam { value = item.FindPropertyRelative("value").stringValue }.ParsedValue;
                if (int.TryParse(name, out int index))
                {
                    if (index < 0) continue;
                    if (index > FormattableLocalizedText.MaxParameterIndex) throw new InvalidOperationException("Inspector preview supports parameter indices up to 65535. Use smaller indices or named parameters.");
                    if (!indexed.TryGetValue(index, out var previous) || previous == null) indexed[index] = value;
                }
                else if (!named.ContainsKey(name)) named.Add(name, value);
            }
            object[] args = indexed.Count == 0 ? null : new object[indexed.Keys.Max() + 1];
            foreach (var pair in indexed) args[pair.Key] = pair.Value;
            IFormatProvider culture = CultureInfo.InvariantCulture;
            try { culture = CultureInfo.GetCultureInfo(language); } catch (CultureNotFoundException) { }
            return TextFormatter.Format(TokenParser.Parse(template ?? ""), (rules ?? new LocalizationPluralRules()).GetRuleInfo(language).Rule, culture, args, named);
        }
    }
}
