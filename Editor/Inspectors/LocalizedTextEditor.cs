using System.Collections.Generic;
using System.Linq;
using SimplyLocalize.Components;
using SimplyLocalize.Editor.Data;
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
            KeySelectorUI.DrawTranslationPreview(_keyProp.stringValue);

            serializedObject.ApplyModifiedProperties();
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
            EditorGUILayout.LabelField("Named parameters", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_namedParamsProp, true);

            KeySelectorUI.DrawTranslationPreview(_keyProp.stringValue);
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

                var template = TextProcessing.TokenParser.Parse(rawValue);

                string formatted = template != null
                    ? TextProcessing.TextFormatter.Format(template, profile.Code, null, namedArgs)
                    : rawValue;

                EditorGUILayout.LabelField(profile.displayName, formatted,
                    EditorStyles.helpBox);
            }
        }
    }

    /// <summary>
    /// Shared key selection UI used by all localized component inspectors.
    /// Pattern: button opens SearchWindow, text field + "Add" for new keys.
    /// </summary>
    internal static class KeySelectorUI
    {
        public static void DrawKeySelector(SerializedProperty keyProp, ref string newKeyInput)
        {
            var allKeys = EditorDataCache.AllKeys;
            string currentKey = keyProp.stringValue;

            bool keyExists = !string.IsNullOrEmpty(currentKey) && allKeys.Contains(currentKey);
            bool isEmpty = string.IsNullOrEmpty(currentKey);

            // Current key display + search button
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("Key");

            // Show current key as a button — clicking opens SearchWindow
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

            // "Add" button — shown only when current key doesn't exist but has a value
            if (!isEmpty && !keyExists)
            {
                if (GUILayout.Button("Add", GUILayout.Width(40)))
                {
                    AddKeyToData(currentKey);
                    keyProp.serializedObject.ApplyModifiedProperties();
                }
            }

            EditorGUILayout.EndHorizontal();

            // New key input field + Add button
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("Add new key");

            bool isDuplicate = !string.IsNullOrEmpty(newKeyInput) && allKeys.Contains(newKeyInput);

            prevColor = GUI.color;

            if (isDuplicate)
                GUI.color = new Color(1f, 0.35f, 0.35f);

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

            // Duplicate warning
            if (isDuplicate)
            {
                EditorGUILayout.HelpBox("This key already exists.", MessageType.Error);
            }
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

                if (string.IsNullOrEmpty(value))
                    GUI.color = new Color(1f, 0.7f, 0.7f);

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

                // If this was a new key (from the "Add:" option), create it
                if (!string.IsNullOrEmpty(selectedKey) && !keys.Contains(selectedKey))
                {
                    AddKeyToData(selectedKey);
                }

                EditorDataCache.Invalidate();
            }, pendingNew);

            var mousePos = GUIUtility.GUIToScreenPoint(Event.current.mousePosition);
            SearchWindow.Open(new SearchWindowContext(mousePos), searchWindow);
        }

        private static string FormatKeyDisplay(string key)
        {
            if (!key.Contains('/'))
                return key;

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
