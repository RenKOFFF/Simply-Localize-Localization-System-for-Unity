using System.Collections.Generic;
using System.Linq;
using SimplyLocalize.Components;
using UnityEditor;
using UnityEngine;

namespace SimplyLocalize.Editor.Inspectors
{
    [CustomEditor(typeof(LocalizedText))]
    public class LocalizedTextEditor : UnityEditor.Editor
    {
        private SerializedProperty _keyProp;
        private List<string> _allKeys;
        private string _searchFilter = "";

        private void OnEnable()
        {
            _keyProp = serializedObject.FindProperty("_key");
            RefreshKeys();
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.Space(2);

            // Key selection
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("Key");

            string currentKey = _keyProp.stringValue;

            if (_allKeys != null && _allKeys.Count > 0)
            {
                int currentIndex = _allKeys.IndexOf(currentKey);
                if (currentIndex < 0) currentIndex = 0;

                int newIndex = EditorGUILayout.Popup(currentIndex, _allKeys.ToArray());

                if (newIndex != currentIndex)
                    _keyProp.stringValue = _allKeys[newIndex];
            }
            else
            {
                _keyProp.stringValue = EditorGUILayout.TextField(currentKey);
            }

            if (GUILayout.Button("+", GUILayout.Width(22)))
            {
                var newKey = EditorInputDialog.Show("New key", "Enter the localization key:", "");

                if (!string.IsNullOrEmpty(newKey))
                {
                    _keyProp.stringValue = newKey;
                    RefreshKeys();
                }
            }

            EditorGUILayout.EndHorizontal();

            // Free text field for manual entry
            _keyProp.stringValue = EditorGUILayout.TextField("  or type key", _keyProp.stringValue);

            // Preview
            if (!string.IsNullOrEmpty(_keyProp.stringValue) && Localization.IsInitialized)
            {
                EditorGUILayout.Space(4);
                var style = new GUIStyle(EditorStyles.helpBox)
                {
                    fontSize = 12,
                    richText = false,
                    padding = new RectOffset(8, 8, 6, 6)
                };

                string preview = Localization.Get(_keyProp.stringValue);
                string lang = Localization.CurrentLanguage ?? "?";
                EditorGUILayout.LabelField($"Preview ({lang})", preview, style);
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void RefreshKeys()
        {
            _allKeys = new List<string>();

            if (Localization.IsInitialized)
            {
                _allKeys.AddRange(Localization.GetAllKeys().OrderBy(k => k));
            }
        }
    }

    [CustomEditor(typeof(FormattableLocalizedText))]
    public class FormattableLocalizedTextEditor : UnityEditor.Editor
    {
        private SerializedProperty _keyProp;
        private SerializedProperty _namedParamsProp;
        private List<string> _allKeys;

        private void OnEnable()
        {
            _keyProp = serializedObject.FindProperty("_key");
            _namedParamsProp = serializedObject.FindProperty("_defaultNamedParams");
            RefreshKeys();
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.Space(2);

            // Key selection
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("Key");

            string currentKey = _keyProp.stringValue;

            if (_allKeys != null && _allKeys.Count > 0)
            {
                int currentIndex = _allKeys.IndexOf(currentKey);
                if (currentIndex < 0) currentIndex = 0;

                int newIndex = EditorGUILayout.Popup(currentIndex, _allKeys.ToArray());

                if (newIndex != currentIndex)
                    _keyProp.stringValue = _allKeys[newIndex];
            }
            else
            {
                _keyProp.stringValue = EditorGUILayout.TextField(currentKey);
            }

            if (GUILayout.Button("+", GUILayout.Width(22)))
            {
                var newKey = EditorInputDialog.Show("New key", "Enter the localization key:", "");

                if (!string.IsNullOrEmpty(newKey))
                {
                    _keyProp.stringValue = newKey;
                    RefreshKeys();
                }
            }

            EditorGUILayout.EndHorizontal();

            _keyProp.stringValue = EditorGUILayout.TextField("  or type key", _keyProp.stringValue);

            EditorGUILayout.Space(4);

            // Named parameters list
            EditorGUILayout.LabelField("Named parameters", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_namedParamsProp, true);

            // Preview
            if (!string.IsNullOrEmpty(_keyProp.stringValue) && Localization.IsInitialized)
            {
                EditorGUILayout.Space(4);

                // Show raw template
                string raw = Localization.Get(_keyProp.stringValue);
                string lang = Localization.CurrentLanguage ?? "?";

                var templateStyle = new GUIStyle(EditorStyles.helpBox)
                {
                    fontSize = 11,
                    richText = false,
                    padding = new RectOffset(8, 8, 4, 4)
                };
                EditorGUILayout.LabelField($"Template ({lang})", raw, templateStyle);

                // Show formatted preview with default params
                var component = target as FormattableLocalizedText;
                if (component != null)
                {
                    // Build preview dict from serialized params
                    var previewArgs = new Dictionary<string, object>();

                    for (int i = 0; i < _namedParamsProp.arraySize; i++)
                    {
                        var elem = _namedParamsProp.GetArrayElementAtIndex(i);
                        var nameProp = elem.FindPropertyRelative("name");
                        var valueProp = elem.FindPropertyRelative("value");

                        if (!string.IsNullOrEmpty(nameProp.stringValue))
                        {
                            string v = valueProp.stringValue;
                            object parsed = int.TryParse(v, out int intVal)
                                ? (object)intVal : v;
                            previewArgs[nameProp.stringValue] = parsed;
                        }
                    }

                    string formatted = Localization.Get(_keyProp.stringValue, previewArgs);

                    var previewStyle = new GUIStyle(EditorStyles.helpBox)
                    {
                        fontSize = 12,
                        richText = false,
                        padding = new RectOffset(8, 8, 6, 6)
                    };
                    EditorGUILayout.LabelField($"Preview ({lang})", formatted, previewStyle);
                }
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void RefreshKeys()
        {
            _allKeys = new List<string>();

            if (Localization.IsInitialized)
            {
                _allKeys.AddRange(Localization.GetAllKeys().OrderBy(k => k));
            }
        }
    }

    /// <summary>
    /// Simple input dialog utility.
    /// </summary>
    public static class EditorInputDialog
    {
        public static string Show(string title, string message, string defaultValue)
        {
            return defaultValue; // Unity doesn't have a built-in input dialog
            // This will be replaced with a proper popup in production.
            // For now, users can type directly in the key field.
        }
    }
}
