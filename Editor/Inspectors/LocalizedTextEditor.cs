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
        private bool _editMode;

        private void OnEnable() => _keyProp = serializedObject.FindProperty("_key");

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.Space(2);
            KeySelectorUI.DrawKeySelector(_keyProp, ref _newKeyInput);
            DrawTranslations(_keyProp.stringValue, ref _editMode);
            serializedObject.ApplyModifiedProperties();
        }

        /// <summary>
        /// Compact translation display: label + value on same line for short text,
        /// wraps naturally for longer text. Edit toggle for inline editing.
        /// </summary>
        internal static void DrawTranslations(string key, ref bool editMode)
        {
            if (string.IsNullOrEmpty(key)) return;

            var data = EditorDataCache.Data;
            var config = EditorDataCache.Config;
            if (data == null || config == null) return;

            EditorGUILayout.Space(4);

            // Header + edit toggle
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Translations", EditorStyles.boldLabel);

            if (editMode) GUI.color = new Color(0.4f, 0.7f, 1f);
            if (GUILayout.Button(editMode ? "✎ editing" : "✎",
                editMode ? EditorStyles.miniButtonMid : EditorStyles.miniButton,
                GUILayout.Width(editMode ? 70 : 24)))
                editMode = !editMode;
            GUI.color = Color.white;

            EditorGUILayout.EndHorizontal();

            foreach (var profile in config.languages)
            {
                if (profile == null) continue;

                string value = data.GetTranslation(key, profile.Code) ?? "";
                bool missing = string.IsNullOrEmpty(value);

                Color prev = GUI.color;
                if (missing) GUI.color = new Color(1f, 0.7f, 0.7f);

                if (editMode)
                {
                    // Edit mode: label on top, TextArea below (supports multiline)
                    EditorGUILayout.LabelField($"{profile.displayName} ({profile.Code})",
                        EditorStyles.miniLabel);

                    var style = new GUIStyle(EditorStyles.textArea)
                    {
                        wordWrap = true,
                        fontSize = 11
                    };

                    float h = style.CalcHeight(new GUIContent(value),
                        EditorGUIUtility.currentViewWidth - 40);
                    h = Mathf.Max(h, EditorGUIUtility.singleLineHeight);

                    string newValue = EditorGUILayout.TextArea(value, style,
                        GUILayout.MinHeight(h));

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
                }
                else
                {
                    // Read mode: compact — label : value on one line, wraps if long
                    string display = missing ? "(missing)" : value;

                    EditorGUILayout.LabelField(
                        $"{profile.displayName} ({profile.Code})",
                        display,
                        EditorStyles.helpBox);
                }

                GUI.color = prev;
            }
        }
    }

    [CustomEditor(typeof(FormattableLocalizedText))]
    public class FormattableLocalizedTextEditor : UnityEditor.Editor
    {
        private SerializedProperty _keyProp;
        private SerializedProperty _namedParamsProp;
        private string _newKeyInput = "";
        private bool _editMode;

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
            DrawAutoParams();
            LocalizedTextEditor.DrawTranslations(_keyProp.stringValue, ref _editMode);
            DrawFormattedPreview();
            serializedObject.ApplyModifiedProperties();
        }

        private void DrawAutoParams()
        {
            string key = _keyProp.stringValue;
            if (string.IsNullOrEmpty(key)) return;

            var data = EditorDataCache.Data;
            var config = EditorDataCache.Config;
            if (data == null || config == null) return;

            string refLang = config.DefaultLanguageCode
                ?? (config.languages.Count > 0 ? config.languages[0].Code : null);
            if (string.IsNullOrEmpty(refLang)) return;

            string raw = data.GetTranslation(key, refLang) ?? "";
            var templateParams = ExtractParamNames(raw);
            if (templateParams.Count == 0) return;

            SyncParams(templateParams);

            EditorGUILayout.LabelField("Parameters", EditorStyles.boldLabel);

            for (int i = 0; i < _namedParamsProp.arraySize; i++)
            {
                var elem = _namedParamsProp.GetArrayElementAtIndex(i);
                var nameProp = elem.FindPropertyRelative("name");
                var valueProp = elem.FindPropertyRelative("value");

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("{" + nameProp.stringValue + "}",
                    EditorStyles.boldLabel, GUILayout.Width(120));
                valueProp.stringValue = EditorGUILayout.TextField(valueProp.stringValue);
                EditorGUILayout.EndHorizontal();
            }
        }

        private void SyncParams(List<string> templateParams)
        {
            var existing = new Dictionary<string, int>();
            for (int i = 0; i < _namedParamsProp.arraySize; i++)
            {
                string n = _namedParamsProp.GetArrayElementAtIndex(i)
                    .FindPropertyRelative("name").stringValue;
                if (!string.IsNullOrEmpty(n)) existing[n] = i;
            }

            for (int i = _namedParamsProp.arraySize - 1; i >= 0; i--)
            {
                string n = _namedParamsProp.GetArrayElementAtIndex(i)
                    .FindPropertyRelative("name").stringValue;
                if (!templateParams.Contains(n))
                    _namedParamsProp.DeleteArrayElementAtIndex(i);
            }

            foreach (var param in templateParams)
            {
                if (existing.ContainsKey(param)) continue;
                int idx = _namedParamsProp.arraySize;
                _namedParamsProp.InsertArrayElementAtIndex(idx);
                var elem = _namedParamsProp.GetArrayElementAtIndex(idx);
                elem.FindPropertyRelative("name").stringValue = param;
                bool numeric = int.TryParse(param, out _)
                    || param is "count" or "amount" or "level" or "hp" or "damage" or "price";
                elem.FindPropertyRelative("value").stringValue = numeric ? "0" : "";
            }
        }

        private void DrawFormattedPreview()
        {
            string key = _keyProp.stringValue;
            if (string.IsNullOrEmpty(key) || _namedParamsProp.arraySize == 0) return;

            var data = EditorDataCache.Data;
            var config = EditorDataCache.Config;
            if (data == null || config == null) return;

            var namedArgs = new Dictionary<string, object>();
            var indexedArgs = new SortedDictionary<int, object>();

            for (int i = 0; i < _namedParamsProp.arraySize; i++)
            {
                var elem = _namedParamsProp.GetArrayElementAtIndex(i);
                string pName = elem.FindPropertyRelative("name").stringValue;
                string pValue = elem.FindPropertyRelative("value").stringValue;
                if (string.IsNullOrEmpty(pName)) continue;

                object parsed = int.TryParse(pValue, out int iv) ? (object)iv : pValue ?? "";

                if (int.TryParse(pName, out int pidx))
                    indexedArgs[pidx] = parsed;
                else
                    namedArgs[pName] = parsed;
            }

            object[] idxArr = null;
            if (indexedArgs.Count > 0)
            {
                idxArr = new object[indexedArgs.Keys.Max() + 1];
                foreach (var kvp in indexedArgs) idxArr[kvp.Key] = kvp.Value;
            }

            if ((idxArr == null || idxArr.Length == 0) && namedArgs.Count == 0) return;

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Formatted preview", EditorStyles.boldLabel);

            foreach (var profile in config.languages)
            {
                if (profile == null) continue;
                string raw = data.GetTranslation(key, profile.Code);
                if (string.IsNullOrEmpty(raw)) continue;

                var template = TokenParser.Parse(raw);
                string formatted = template != null
                    ? TextFormatter.Format(template, profile.Code, idxArr,
                        namedArgs.Count > 0 ? namedArgs : null)
                    : raw;

                EditorGUILayout.LabelField(profile.displayName, formatted, EditorStyles.helpBox);
            }
        }

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
                        if (id.Length > 0 && seen.Add(id)) result.Add(id);
                        pos = close + 1;
                        continue;
                    }
                }
                pos++;
            }
            return result;
        }
    }

    internal static class KeySelectorUI
    {
        public static void DrawKeySelector(SerializedProperty keyProp, ref string newKeyInput)
        {
            var allKeys = EditorDataCache.AllKeys;
            string currentKey = keyProp.stringValue;
            bool keyExists = !string.IsNullOrEmpty(currentKey) && allKeys.Contains(currentKey);
            bool isEmpty = string.IsNullOrEmpty(currentKey);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("Key");

            string display = isEmpty ? "<None>"
                : keyExists ? FormatKey(currentKey) : $"<None> : ({currentKey})";

            Color prev = GUI.color;
            if (!isEmpty && !keyExists) GUI.color = new Color(1f, 0.35f, 0.35f);
            if (GUILayout.Button(display, EditorStyles.popup))
                OpenSearch(keyProp, allKeys, newKeyInput);
            GUI.color = prev;

            if (!isEmpty && !keyExists)
                if (GUILayout.Button("Add", GUILayout.Width(40)))
                    AddKey(currentKey);

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("Add new key");
            bool dup = !string.IsNullOrEmpty(newKeyInput) && allKeys.Contains(newKeyInput);
            prev = GUI.color;
            if (dup) GUI.color = new Color(1f, 0.35f, 0.35f);
            newKeyInput = EditorGUILayout.TextField(newKeyInput);
            GUI.color = prev;

            GUI.enabled = !string.IsNullOrEmpty(newKeyInput) && !dup;
            if (GUILayout.Button("Add", GUILayout.Width(40)))
            {
                AddKey(newKeyInput);
                keyProp.stringValue = newKeyInput;
                keyProp.serializedObject.ApplyModifiedProperties();
                newKeyInput = "";
            }
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            if (dup) EditorGUILayout.HelpBox("This key already exists.", MessageType.Error);
        }

        private static void OpenSearch(SerializedProperty keyProp, List<string> keys, string pending)
        {
            var win = ScriptableObject.CreateInstance<KeySearchWindow>();
            win.Init(new List<string>(keys), sel =>
            {
                keyProp.serializedObject.Update();
                keyProp.stringValue = sel ?? "";
                keyProp.serializedObject.ApplyModifiedProperties();
                if (!string.IsNullOrEmpty(sel) && !keys.Contains(sel)) AddKey(sel);
                EditorDataCache.Invalidate();
            }, pending);
            SearchWindow.Open(
                new SearchWindowContext(GUIUtility.GUIToScreenPoint(Event.current.mousePosition)), win);
        }

        private static string FormatKey(string key) =>
            key.Contains('/') ? $"{key.Split('/')[^1]}    ({key})" : key;

        private static void AddKey(string key)
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