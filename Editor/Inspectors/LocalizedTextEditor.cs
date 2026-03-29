using System.Collections.Generic;
using System.Linq;
using SimplyLocalize.Components;
using SimplyLocalize.Editor.Data;
using SimplyLocalize.TextProcessing;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;

namespace SimplyLocalize.Editor.Inspectors
{
    [CustomEditor(typeof(LocalizedText))]
    public class LocalizedTextEditor : UnityEditor.Editor
    {
        private SerializedProperty _keyProp;
        private string _newKeyInput = "";

        private void OnEnable()
        {
            _keyProp = serializedObject.FindProperty("_key");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.Space(2);

            KeySelectorUI.DrawKeySelector(_keyProp, ref _newKeyInput);
            DrawEditableTranslations(_keyProp.stringValue);

            serializedObject.ApplyModifiedProperties();
        }

        /// <summary>
        /// Shows translations with an edit button per language.
        /// Clicking the edit button makes the field editable, saves on blur.
        /// </summary>
        private void DrawEditableTranslations(string key)
        {
            if (string.IsNullOrEmpty(key)) return;

            var data = EditorDataCache.Data;
            var config = EditorDataCache.Config;
            if (data == null || config == null) return;

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Translations", EditorStyles.boldLabel);

            foreach (var profile in config.languages)
            {
                if (profile == null) continue;

                string value = data.GetTranslation(key, profile.Code) ?? "";
                bool isMissing = string.IsNullOrEmpty(value);

                EditorGUILayout.BeginHorizontal();

                // Language label
                EditorGUILayout.LabelField(
                    $"{profile.displayName} ({profile.Code})",
                    GUILayout.Width(120));

                // Editable text field
                Color prev = GUI.color;
                if (isMissing) GUI.color = new Color(1f, 0.7f, 0.7f);

                string newValue = EditorGUILayout.TextField(value);
                GUI.color = prev;

                // Save if changed
                if (newValue != value)
                {
                    string file = data.GetFileForKey(key);

                    if (!string.IsNullOrEmpty(file))
                    {
                        data.SetTranslation(key, profile.Code, newValue);
                        data.SaveFile(file, profile.Code);
                        EditorDataCache.Invalidate();
                    }
                }

                EditorGUILayout.EndHorizontal();
            }
        }
    }

    [CustomEditor(typeof(FormattableLocalizedText))]
    public class FormattableLocalizedTextEditor : UnityEditor.Editor
    {
        private SerializedProperty _keyProp;
        private SerializedProperty _namedParamsProp;
        private string _newKeyInput = "";

        private void OnEnable()
        {
            _keyProp = serializedObject.FindProperty("_key");
            _namedParamsProp = serializedObject.FindProperty("_defaultNamedParams");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.Space(2);

            KeySelectorUI.DrawKeySelector(_keyProp, ref _newKeyInput);

            EditorGUILayout.Space(4);

            // Auto-fill params from template, then draw them
            DrawAutoParams();

            // Translations + formatted preview
            KeySelectorUI.DrawTranslationPreview(_keyProp.stringValue);
            DrawFormattedPreview();

            serializedObject.ApplyModifiedProperties();
        }

        /// <summary>
        /// Extracts named and indexed params from the translation template,
        /// syncs them with the serialized _defaultNamedParams list.
        /// Param names are read-only, values are editable.
        /// </summary>
        private void DrawAutoParams()
        {
            string key = _keyProp.stringValue;

            if (string.IsNullOrEmpty(key))
            {
                EditorGUILayout.LabelField("Named parameters", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox("Select a key to auto-detect parameters.", MessageType.Info);
                return;
            }

            var data = EditorDataCache.Data;
            var config = EditorDataCache.Config;

            if (data == null || config == null) return;

            // Find the reference language template
            string refLang = config.DefaultLanguageCode
                ?? (config.languages.Count > 0 ? config.languages[0].Code : null);

            if (string.IsNullOrEmpty(refLang)) return;

            string rawTemplate = data.GetTranslation(key, refLang) ?? "";
            var templateParams = ExtractParamNames(rawTemplate);

            if (templateParams.Count == 0)
            {
                // No params in template — hide the section
                if (_namedParamsProp.arraySize > 0)
                {
                    EditorGUILayout.LabelField("Parameters", EditorStyles.boldLabel);
                    EditorGUILayout.HelpBox("Template has no parameters.", MessageType.Info);
                }

                return;
            }

            // Sync serialized list with detected params
            SyncParams(templateParams);

            // Draw params — names read-only, values editable
            EditorGUILayout.LabelField("Parameters (auto-detected)", EditorStyles.boldLabel);

            for (int i = 0; i < _namedParamsProp.arraySize; i++)
            {
                var elem = _namedParamsProp.GetArrayElementAtIndex(i);
                var nameProp = elem.FindPropertyRelative("name");
                var valueProp = elem.FindPropertyRelative("value");

                EditorGUILayout.BeginHorizontal();

                // Name — read-only, displayed as label with mono font
                var nameStyle = new GUIStyle(EditorStyles.label)
                {
                    fontStyle = FontStyle.Bold,
                    fontSize = 11
                };
                EditorGUILayout.LabelField(nameProp.stringValue, nameStyle, GUILayout.Width(120));

                // Value — editable
                valueProp.stringValue = EditorGUILayout.TextField(valueProp.stringValue);

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space(2);
        }

        /// <summary>
        /// Syncs the serialized param list with params detected from the template.
        /// Adds missing params with default values, removes params no longer in template.
        /// Preserves existing values for params that still exist.
        /// </summary>
        private void SyncParams(List<string> templateParams)
        {
            // Build existing param map
            var existing = new Dictionary<string, int>();

            for (int i = 0; i < _namedParamsProp.arraySize; i++)
            {
                string name = _namedParamsProp.GetArrayElementAtIndex(i)
                    .FindPropertyRelative("name").stringValue;

                if (!string.IsNullOrEmpty(name))
                    existing[name] = i;
            }

            // Remove params not in template (iterate backwards)
            for (int i = _namedParamsProp.arraySize - 1; i >= 0; i--)
            {
                string name = _namedParamsProp.GetArrayElementAtIndex(i)
                    .FindPropertyRelative("name").stringValue;

                if (!templateParams.Contains(name))
                    _namedParamsProp.DeleteArrayElementAtIndex(i);
            }

            // Add missing params
            foreach (var param in templateParams)
            {
                if (existing.ContainsKey(param)) continue;

                int newIndex = _namedParamsProp.arraySize;
                _namedParamsProp.InsertArrayElementAtIndex(newIndex);

                var newElem = _namedParamsProp.GetArrayElementAtIndex(newIndex);
                newElem.FindPropertyRelative("name").stringValue = param;

                // Default value: "0" for likely-numeric params, empty for others
                bool isLikelyNumeric = param == "count" || param == "amount"
                    || param == "level" || param == "hp" || param == "damage"
                    || int.TryParse(param, out _);

                newElem.FindPropertyRelative("value").stringValue =
                    isLikelyNumeric ? "0" : "";
            }
        }

        private void DrawFormattedPreview()
        {
            string key = _keyProp.stringValue;
            if (string.IsNullOrEmpty(key)) return;

            var data = EditorDataCache.Data;
            var config = EditorDataCache.Config;
            if (data == null || config == null) return;

            // Build param dict from serialized data
            var namedArgs = new Dictionary<string, object>();

            for (int i = 0; i < _namedParamsProp.arraySize; i++)
            {
                var elem = _namedParamsProp.GetArrayElementAtIndex(i);
                string pName = elem.FindPropertyRelative("name").stringValue;
                string pValue = elem.FindPropertyRelative("value").stringValue;

                if (string.IsNullOrEmpty(pName)) continue;

                if (int.TryParse(pValue, out int intVal))
                    namedArgs[pName] = intVal;
                else
                    namedArgs[pName] = pValue ?? "";
            }

            if (namedArgs.Count == 0) return;

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Formatted preview", EditorStyles.boldLabel);

            foreach (var profile in config.languages)
            {
                if (profile == null) continue;

                string rawValue = data.GetTranslation(key, profile.Code);
                if (string.IsNullOrEmpty(rawValue)) continue;

                var template = TokenParser.Parse(rawValue);

                string formatted = template != null
                    ? TextFormatter.Format(template, profile.Code, null, namedArgs)
                    : rawValue;

                EditorGUILayout.LabelField(profile.displayName, formatted,
                    EditorStyles.helpBox);
            }
        }

        /// <summary>
        /// Extracts all unique parameter names from a translation template string.
        /// Returns both indexed (as "0", "1") and named (as "playerName", "count") params.
        /// </summary>
        private static List<string> ExtractParamNames(string template)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(template)) return result;

            var seen = new HashSet<string>();
            int pos = 0;

            while (pos < template.Length)
            {
                if (template[pos] == '{' && pos + 1 < template.Length && template[pos + 1] != '{')
                {
                    int close = template.IndexOf('}', pos + 1);

                    if (close > pos)
                    {
                        string content = template.Substring(pos + 1, close - pos - 1);
                        int pipe = content.IndexOf('|');
                        string id = (pipe >= 0 ? content.Substring(0, pipe) : content).Trim();

                        if (id.Length > 0 && seen.Add(id))
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

    /// <summary>
    /// Shared key selection UI used by all localized component inspectors.
    /// </summary>
    internal static class KeySelectorUI
    {
        public static void DrawKeySelector(SerializedProperty keyProp, ref string newKeyInput)
        {
            var allKeys = EditorDataCache.AllKeys;
            string currentKey = keyProp.stringValue;

            bool keyExists = !string.IsNullOrEmpty(currentKey) && allKeys.Contains(currentKey);
            bool isEmpty = string.IsNullOrEmpty(currentKey);

            // Current key — button opens SearchWindow
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("Key");

            string displayText;

            if (isEmpty)
                displayText = "<None>";
            else if (keyExists)
                displayText = FormatKeyDisplay(currentKey);
            else
                displayText = $"<None> : ({currentKey})";

            Color prevColor = GUI.color;

            if (!isEmpty && !keyExists)
                GUI.color = new Color(1f, 0.35f, 0.35f);

            if (GUILayout.Button(displayText, EditorStyles.popup))
            {
                OpenSearchWindow(keyProp, allKeys, newKeyInput);
            }

            GUI.color = prevColor;

            if (!isEmpty && !keyExists)
            {
                if (GUILayout.Button("Add", GUILayout.Width(40)))
                {
                    AddKeyToData(currentKey);
                    keyProp.serializedObject.ApplyModifiedProperties();
                }
            }

            EditorGUILayout.EndHorizontal();

            // New key input + Add
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("Add new key");

            bool isDuplicate = !string.IsNullOrEmpty(newKeyInput) && allKeys.Contains(newKeyInput);

            prevColor = GUI.color;
            if (isDuplicate) GUI.color = new Color(1f, 0.35f, 0.35f);

            newKeyInput = EditorGUILayout.TextField(newKeyInput);
            GUI.color = prevColor;

            bool canAdd = !string.IsNullOrEmpty(newKeyInput) && !isDuplicate;
            GUI.enabled = canAdd;

            if (GUILayout.Button("Add", GUILayout.Width(40)))
            {
                AddKeyToData(newKeyInput);
                keyProp.stringValue = newKeyInput;
                keyProp.serializedObject.ApplyModifiedProperties();
                newKeyInput = "";
            }

            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            if (isDuplicate)
                EditorGUILayout.HelpBox("This key already exists.", MessageType.Error);
        }

        public static void DrawTranslationPreview(string key)
        {
            if (string.IsNullOrEmpty(key)) return;

            var data = EditorDataCache.Data;
            var config = EditorDataCache.Config;
            if (data == null || config == null) return;

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Translations", EditorStyles.boldLabel);

            foreach (var profile in config.languages)
            {
                if (profile == null) continue;

                string value = data.GetTranslation(key, profile.Code);
                string display = string.IsNullOrEmpty(value) ? "(missing)" : value;

                Color prev = GUI.color;
                if (string.IsNullOrEmpty(value)) GUI.color = new Color(1f, 0.7f, 0.7f);

                EditorGUILayout.LabelField(
                    $"{profile.displayName} ({profile.Code})",
                    display, EditorStyles.helpBox);

                GUI.color = prev;
            }
        }

        private static void OpenSearchWindow(SerializedProperty keyProp, List<string> keys, string pendingNew)
        {
            var searchWindow = ScriptableObject.CreateInstance<KeySearchWindow>();

            searchWindow.Init(new List<string>(keys), selectedKey =>
            {
                keyProp.serializedObject.Update();
                keyProp.stringValue = selectedKey ?? "";
                keyProp.serializedObject.ApplyModifiedProperties();

                if (!string.IsNullOrEmpty(selectedKey) && !keys.Contains(selectedKey))
                    AddKeyToData(selectedKey);

                EditorDataCache.Invalidate();
            }, pendingNew);

            var mousePos = GUIUtility.GUIToScreenPoint(Event.current.mousePosition);
            SearchWindow.Open(new SearchWindowContext(mousePos), searchWindow);
        }

        private static string FormatKeyDisplay(string key)
        {
            if (!key.Contains('/')) return key;
            var parts = key.Split('/');
            return $"{parts[^1]}    ({key})";
        }

        private static void AddKeyToData(string key)
        {
            var data = EditorDataCache.Data;
            if (data == null) return;

            string file = data.SourceFiles.Count > 0 ? data.SourceFiles[0] : "global";
            data.AddKey(key, file);
            data.SaveFileAllLanguages(file);
            AssetDatabase.Refresh();
            EditorDataCache.Invalidate();
        }
    }
}
