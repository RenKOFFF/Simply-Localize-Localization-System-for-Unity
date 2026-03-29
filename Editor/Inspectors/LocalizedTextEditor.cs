using System.Collections.Generic;
using System.Linq;
using SimplyLocalize.Components;
using SimplyLocalize.Editor.Data;
using UnityEditor;
using UnityEngine;

namespace SimplyLocalize.Editor.Inspectors
{
    [CustomEditor(typeof(LocalizedText))]
    public class LocalizedTextEditor : UnityEditor.Editor
    {
        private SerializedProperty _keyProp;
        private string _keyFilter = "";

        private void OnEnable()
        {
            _keyProp = serializedObject.FindProperty("_key");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.Space(2);

            DrawKeySelector(_keyProp, ref _keyFilter);
            DrawTranslationPreview(_keyProp.stringValue);

            serializedObject.ApplyModifiedProperties();
        }

        // ──────────────────────────────────────────────
        //  Shared drawing methods
        // ──────────────────────────────────────────────

        internal static void DrawKeySelector(SerializedProperty keyProp, ref string keyFilter)
        {
            var allKeys = EditorDataCache.AllKeys;
            string currentKey = keyProp.stringValue;

            // Search field
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("Search");
            keyFilter = EditorGUILayout.TextField(keyFilter, EditorStyles.toolbarSearchField);
            EditorGUILayout.EndHorizontal();

            // Filter keys
            List<string> filtered;

            if (string.IsNullOrEmpty(keyFilter))
            {
                filtered = allKeys;
            }
            else
            {
                string lower = keyFilter.ToLowerInvariant();
                filtered = allKeys.Where(k => k.ToLowerInvariant().Contains(lower)).ToList();
            }

            // Dropdown
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("Key");

            if (filtered.Count > 0)
            {
                // Build display array with current key at top if it exists
                var display = new List<string>(filtered);

                if (!string.IsNullOrEmpty(currentKey) && !display.Contains(currentKey))
                    display.Insert(0, currentKey + " (custom)");

                int currentIndex = display.IndexOf(currentKey);
                if (currentIndex < 0) currentIndex = 0;

                int newIndex = EditorGUILayout.Popup(currentIndex, display.ToArray());

                string selected = display[newIndex];

                if (selected.EndsWith(" (custom)"))
                    selected = selected.Replace(" (custom)", "");

                if (selected != currentKey)
                    keyProp.stringValue = selected;
            }
            else
            {
                EditorGUILayout.LabelField("No keys found", EditorStyles.miniLabel);
            }

            // "+" button — creates a new key
            if (GUILayout.Button("+", GUILayout.Width(22)))
            {
                ShowAddKeyWindow(keyProp);
            }

            EditorGUILayout.EndHorizontal();

            // Manual entry fallback
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("Manual key");
            string manual = EditorGUILayout.TextField(currentKey);

            if (manual != currentKey)
                keyProp.stringValue = manual;

            EditorGUILayout.EndHorizontal();
        }

        internal static void DrawTranslationPreview(string key)
        {
            if (string.IsNullOrEmpty(key))
                return;

            var data = EditorDataCache.Data;
            var config = EditorDataCache.Config;

            if (data == null || config == null)
                return;

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Translations", EditorStyles.boldLabel);

            var style = new GUIStyle(EditorStyles.helpBox)
            {
                fontSize = 11,
                richText = false,
                wordWrap = true,
                padding = new RectOffset(8, 8, 4, 4)
            };

            foreach (var profile in config.languages)
            {
                if (profile == null) continue;

                string value = data.GetTranslation(key, profile.Code);
                string display = string.IsNullOrEmpty(value) ? "(missing)" : value;

                Color prevColor = GUI.color;

                if (string.IsNullOrEmpty(value))
                    GUI.color = new Color(1f, 0.7f, 0.7f);

                EditorGUILayout.LabelField(
                    $"{profile.displayName} ({profile.Code})",
                    display, style);

                GUI.color = prevColor;
            }
        }

        private static void ShowAddKeyWindow(SerializedProperty keyProp)
        {
            var window = ScriptableObject.CreateInstance<AddKeyFromInspector>();
            window.Init(keyProp);
            window.titleContent = new GUIContent("New key");
            window.ShowUtility();
            window.position = new Rect(
                Screen.width / 2f - 150, Screen.height / 2f - 60, 300, 130);
        }
    }

    [CustomEditor(typeof(FormattableLocalizedText))]
    public class FormattableLocalizedTextEditor : UnityEditor.Editor
    {
        private SerializedProperty _keyProp;
        private SerializedProperty _namedParamsProp;
        private string _keyFilter = "";

        private void OnEnable()
        {
            _keyProp = serializedObject.FindProperty("_key");
            _namedParamsProp = serializedObject.FindProperty("_defaultNamedParams");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.Space(2);

            LocalizedTextEditor.DrawKeySelector(_keyProp, ref _keyFilter);

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Named parameters", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_namedParamsProp, true);

            // Translation preview (raw templates)
            LocalizedTextEditor.DrawTranslationPreview(_keyProp.stringValue);

            // Formatted preview with current params
            DrawFormattedPreview();

            serializedObject.ApplyModifiedProperties();
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

            var style = new GUIStyle(EditorStyles.helpBox)
            {
                fontSize = 11,
                wordWrap = true,
                padding = new RectOffset(8, 8, 4, 4)
            };

            foreach (var profile in config.languages)
            {
                if (profile == null) continue;

                string rawValue = data.GetTranslation(key, profile.Code);
                if (string.IsNullOrEmpty(rawValue)) continue;

                // Parse and format
                var template = SimplyLocalize.TextProcessing.TokenParser.Parse(rawValue);

                string formatted = template != null
                    ? SimplyLocalize.TextProcessing.TextFormatter.Format(
                        template, profile.Code, null, namedArgs)
                    : rawValue;

                EditorGUILayout.LabelField(
                    $"{profile.displayName}", formatted, style);
            }
        }
    }

    /// <summary>
    /// Popup window for creating a new key from the component inspector.
    /// Creates the key in the data, saves to disk, and assigns to the component.
    /// </summary>
    public class AddKeyFromInspector : EditorWindow
    {
        private SerializedProperty _keyProp;
        private string _newKey = "";
        private int _fileIndex;

        public void Init(SerializedProperty keyProp)
        {
            _keyProp = keyProp;
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(4);
            _newKey = EditorGUILayout.TextField("Key", _newKey);

            var data = EditorDataCache.Data;

            if (data != null && data.SourceFiles.Count > 0)
            {
                var files = data.SourceFiles.ToArray();
                _fileIndex = Mathf.Clamp(_fileIndex, 0, files.Length - 1);
                _fileIndex = EditorGUILayout.Popup("File", _fileIndex, files);
            }

            EditorGUILayout.Space(4);

            bool canCreate = !string.IsNullOrEmpty(_newKey) && data != null;

            using (new EditorGUI.DisabledScope(!canCreate))
            {
                if (GUILayout.Button("Create & assign"))
                {
                    string file = data.SourceFiles.Count > 0
                        ? data.SourceFiles[_fileIndex]
                        : "global";

                    data.AddKey(_newKey, file);
                    data.SaveFileAllLanguages(file);
                    AssetDatabase.Refresh();
                    EditorDataCache.Invalidate();

                    // Assign to component
                    _keyProp.serializedObject.Update();
                    _keyProp.stringValue = _newKey;
                    _keyProp.serializedObject.ApplyModifiedProperties();

                    Close();
                }
            }
        }
    }
}
