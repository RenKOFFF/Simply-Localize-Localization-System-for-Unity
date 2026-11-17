using System;
using System.Collections.Generic;
using System.Linq;
using SimplyLocalize.Editor.Data;
using SimplyLocalize.Editor.Inspectors;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;

namespace SimplyLocalize.Editor
{
    internal static class LocalizationStringFieldGUI
    {
        internal static float Line => EditorGUIUtility.singleLineHeight;
        internal static float Step => Line + EditorGUIUtility.standardVerticalSpacing;
        internal static float Height(SerializedProperty property, bool preview, bool all)
        {
            if (property.propertyType != SerializedPropertyType.String) return Line;
            int rows = 2; // Field and explicit scope/status.
            if (preview && !property.hasMultipleDifferentValues && !string.IsNullOrEmpty(property.stringValue))
            {
                try { rows += all ? Math.Max(1, LocalizationEditorFields.StringContext?.Channel.Languages.Count ?? 0) : 1; }
                catch { rows++; }
            }
            return rows * Step - EditorGUIUtility.standardVerticalSpacing;
        }
        internal static void Draw(Rect position, SerializedProperty property, GUIContent label, bool picker, bool preview, bool all)
        {
            if (property.propertyType != SerializedPropertyType.String) { EditorGUI.LabelField(position, label.text, "Localization attributes require a string."); return; }
            using var scope = new EditorGUI.PropertyScope(position, label, property);
            var row = new Rect(position.x, position.y, position.width, Line);
            bool mixed = EditorGUI.showMixedValue; EditorGUI.showMixedValue = property.hasMultipleDifferentValues;
            try
            {
                if (picker)
                {
                    var button = EditorGUI.PrefixLabel(row, label);
                    string display = property.hasMultipleDifferentValues ? "Mixed keys" : string.IsNullOrEmpty(property.stringValue) ? "Choose key" : property.stringValue;
                    if (GUI.Button(button, new GUIContent(display, "Existing text keys in Main's default set."), EditorStyles.popup))
                    {
                        var assign = LocalizationEditorFields.CaptureAssignment(property, false);
                        var search = ScriptableObject.CreateInstance<KeySearchWindow>();
                        search.Init(LocalizationEditorFields.StringEntries.Select(e => e.Key).ToList(), selected =>
                        {
                            try { assign(selected, null); } catch (Exception failure) { EditorUtility.DisplayDialog("Simply Localize", failure.Message, "OK"); }
                        });
                        SearchWindow.Open(new SearchWindowContext(GUIUtility.GUIToScreenPoint(Event.current.mousePosition)), search);
                    }
                }
                else
                {
                    EditorGUI.BeginChangeCheck(); string value = EditorGUI.TextField(row, label, property.stringValue);
                    if (EditorGUI.EndChangeCheck()) property.stringValue = value;
                }
                EditorGUI.showMixedValue = mixed;
                row.y += Step;
                var context = LocalizationEditorFields.StringContext;
                if (context == null) { Message(row, EditorProjectSelection.Message); return; }
                string route = context.Channel.Name + " / " + (context.Set?.Name ?? "(no default set)");
                bool hasKey = !property.hasMultipleDifferentValues && !string.IsNullOrEmpty(property.stringValue);
                var entry = hasKey ? context.Set?.Entries.FirstOrDefault(e => e.Kind == LocalizationEntryKind.Text && e.Key == property.stringValue) : null;
                if (hasKey && entry == null) { Message(row, route + " · key is not registered"); return; }
                Message(row, route + " · string key", "Unqualified string lookup uses Main's default set, independently of the window's selected channel. Use an entry reference for other sets or rename-safe bindings.");
                if (!preview || property.hasMultipleDifferentValues || string.IsNullOrEmpty(property.stringValue)) return;
                row.y += Step;
                IEnumerable<string> languages = all ? context.Channel.Languages : new[] { context.Channel.DefaultLanguage };
                foreach (string language in languages)
                {
                    bool found = context.Text.TryResolve(entry.Id, language, out var cell);
                    string caption = language + (found && cell.Language != language ? " → " + cell.Language : "");
                    string value = found ? cell.Value : "(missing)";
                    var content = new GUIContent(caption + "   " + value.Replace("\r", " ").Replace("\n", " "), caption + "\n" + value);
                    EditorGUI.LabelField(row, content, EditorStyles.miniLabel); row.y += Step;
                }
            }
            catch (Exception failure) { Message(new Rect(position.x, position.y + Step, position.width, Line), failure.Message); }
            finally { EditorGUI.showMixedValue = mixed; }
        }
        private static void Message(Rect row, string message, string tooltip = null) =>
            EditorGUI.LabelField(row, new GUIContent(message ?? "", tooltip ?? message), EditorStyles.miniLabel);
    }
}
