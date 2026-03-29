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

        private void OnEnable()
        {
            _keyProp = serializedObject.FindProperty("_key");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.Space(2);

            KeySelectorUI.DrawKeySelector(_keyProp, ref _newKeyInput);
            DrawTranslationsWithEdit(_keyProp.stringValue, ref _editMode);

            serializedObject.ApplyModifiedProperties();
        }

        internal static void DrawTranslationsWithEdit(string key, ref bool editMode)
        {
            if (string.IsNullOrEmpty(key)) return;

            var data = EditorDataCache.Data;
            var config = EditorDataCache.Config;
            if (data == null || config == null) return;

            EditorGUILayout.Space(4);

            // Header with edit toggle
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Translations", EditorStyles.boldLabel);

            var toggleContent = editMode
                ? new GUIContent("✎ editing", "Click to lock translations")
                : new GUIContent("✎", "Click to edit translations");

            var toggleStyle = new GUIStyle(editMode ? EditorStyles.miniButtonMid : EditorStyles.miniButton);

            if (editMode)
                GUI.color = new Color(0.4f, 0.7f, 1f);

            if (GUILayout.Button(toggleContent, toggleStyle, GUILayout.Width(editMode ? 70 : 24)))
                editMode = !editMode;

            GUI.color = Color.white;
            EditorGUILayout.EndHorizontal();

            // Translation rows
            foreach (var profile in config.languages)
            {
                if (profile == null) continue;

                string value = data.GetTranslation(key, profile.Code) ?? "";
                bool isMissing = string.IsNullOrEmpty(value);

                EditorGUILayout.BeginHorizontal();

                EditorGUILayout.LabelField(
                    $"{profile.displayName} ({profile.Code})",
                    GUILayout.Width(120));

                Color prev = GUI.color;
                if (isMissing) GUI.color = new Color(1f, 0.7f, 0.7f);

                if (editMode)
                {
                    // Auto-grow text area based on content
                    int lineCount = Mathf.Max(1, value.Split('\n').Length);
                    float height = Mathf.Max(18f, lineCount * 16f + 4f);

                    var textAreaStyle = new GUIStyle(EditorStyles.textArea)
                    {
                        wordWrap = true,
                        fontSize = 11
                    };

                    string newValue = EditorGUILayout.TextArea(value, textAreaStyle,
                        GUILayout.MinHeight(height));

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
                    string display = isMissing ? "(missing)" : value;

                    // Auto-grow read-only label
                    int lineCount = Mathf.Max(1, display.Split('\n').Length);
                    float height = Mathf.Max(18f, lineCount * 16f + 4f);

                    var readOnlyStyle = new GUIStyle(EditorStyles.helpBox)
                    {
                        wordWrap = true,
                        fontSize = 11
                    };

                    EditorGUILayout.LabelField(display, readOnlyStyle,
                        GUILayout.MinHeight(height));
                }

                GUI.color = prev;
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

            // Editable translations
            LocalizedTextEditor.DrawTranslationsWithEdit(_keyProp.stringValue, ref _editMode);

            // Formatted preview
            DrawFormattedPreview();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawAutoParams()
        {
            string key = _keyProp.stringValue;

            if (string.IsNullOrEmpty(key))
                return;

            var data = EditorDataCache.Data;
            var config = EditorDataCache.Config;
            if (data == null || config == null) return;

            string refLang = config.DefaultLanguageCode
                ?? (config.languages.Count > 0 ? config.languages[0].Code : null);

            if (string.IsNullOrEmpty(refLang)) return;

            string rawTemplate = data.GetTranslation(key, refLang) ?? "";
            var templateParams = ExtractParamNames(rawTemplate);

            if (templateParams.Count == 0)
                return;

            SyncParams(templateParams);

            EditorGUILayout.LabelField("Parameters", EditorStyles.boldLabel);

            for (int i = 0; i < _namedParamsProp.arraySize; i++)
            {
                var elem = _namedParamsProp.GetArrayElementAtIndex(i);
                var nameProp = elem.FindPropertyRelative("name");
                var valueProp = elem.FindPropertyRelative("value");

                EditorGUILayout.BeginHorizontal();

                // Name — read-only
                var nameStyle = new GUIStyle(EditorStyles.label)
                {
                    fontStyle = FontStyle.Bold,
                    fontSize = 11
                };

                string paramDisplay = nameProp.stringValue;

                // Show {0} style for indexed, {name} style for named
                if (int.TryParse(paramDisplay, out _))
                    paramDisplay = "{" + paramDisplay + "}";
                else
                    paramDisplay = "{" + paramDisplay + "}";

                EditorGUILayout.LabelField(paramDisplay, nameStyle, GUILayout.Width(120));

                valueProp.stringValue = EditorGUILayout.TextField(valueProp.stringValue);

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space(2);
        }

        private void SyncParams(List<string> templateParams)
        {
            var existing = new Dictionary<string, int>();

            for (int i = 0; i < _namedParamsProp.arraySize; i++)
            {
                string name = _namedParamsProp.GetArrayElementAtIndex(i)
                    .FindPropertyRelative("name").stringValue;

                if (!string.IsNullOrEmpty(name))
                    existing[name] = i;
            }

            for (int i = _namedParamsProp.arraySize - 1; i >= 0; i--)
            {
                string name = _namedParamsProp.GetArrayElementAtIndex(i)
                    .FindPropertyRelative("name").stringValue;

                if (!templateParams.Contains(name))
                    _namedParamsProp.DeleteArrayElementAtIndex(i);
            }

            foreach (var param in templateParams)
            {
                if (existing.ContainsKey(param)) continue;

                int newIndex = _namedParamsProp.arraySize;
                _namedParamsProp.InsertArrayElementAtIndex(newIndex);

                var newElem = _namedParamsProp.GetArrayElementAtIndex(newIndex);
                newElem.FindPropertyRelative("name").stringValue = param;

                bool isIndexed = int.TryParse(param, out _);
                bool isLikelyNumeric = isIndexed || param == "count"
                    || param == "amount" || param == "level"
                    || param == "hp" || param == "damage";

                newElem.FindPropertyRelative("value").stringValue =
                    isLikelyNumeric ? "0" : "";
            }
        }

        private void DrawFormattedPreview()
        {
            string key = _keyProp.stringValue;
            if (string.IsNullOrEmpty(key)) return;
            if (_namedParamsProp.arraySize == 0) return;

            var data = EditorDataCache.Data;
            var config = EditorDataCache.Config;
            if (data == null || config == null) return;

            // Build both indexed args and named args from serialized params
            var namedArgs = new Dictionary<string, object>();
            var indexedArgsList = new SortedDictionary<int, object>();

            for (int i = 0; i < _namedParamsProp.arraySize; i++)
            {
                var elem = _namedParamsProp.GetArrayElementAtIndex(i);
                string pName = elem.FindPropertyRelative("name").stringValue;
                string pValue = elem.FindPropertyRelative("value").stringValue;

                if (string.IsNullOrEmpty(pName)) continue;

                object parsedValue;

                if (int.TryParse(pValue, out int intVal))
                    parsedValue = intVal;
                else
                    parsedValue = pValue ?? "";

                // If param name is a number, it's an indexed param
                if (int.TryParse(pName, out int paramIndex))
                {
                    indexedArgsList[paramIndex] = parsedValue;
                }
                else
                {
                    namedArgs[pName] = parsedValue;
                }
            }

            // Convert indexed args to array
            object[] indexedArgs = null;

            if (indexedArgsList.Count > 0)
            {
                int maxIndex = indexedArgsList.Keys.Max();
                indexedArgs = new object[maxIndex + 1];

                foreach (var kvp in indexedArgsList)
                    indexedArgs[kvp.Key] = kvp.Value;
            }

            bool hasAnyArgs = (indexedArgs != null && indexedArgs.Length > 0)
                || namedArgs.Count > 0;

            if (!hasAnyArgs) return;

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Formatted preview", EditorStyles.boldLabel);

            foreach (var profile in config.languages)
            {
                if (profile == null) continue;

                string rawValue = data.GetTranslation(key, profile.Code);
                if (string.IsNullOrEmpty(rawValue)) continue;

                var template = TokenParser.Parse(rawValue);

                string formatted = template != null
                    ? TextFormatter.Format(template, profile.Code, indexedArgs,
                        namedArgs.Count > 0 ? namedArgs : null)
                    : rawValue;

                EditorGUILayout.LabelField(profile.displayName, formatted,
                    EditorStyles.helpBox);
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
    /// Shared key selection UI.
    /// </summary>
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
                OpenSearchWindow(keyProp, allKeys, newKeyInput);

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
