using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using SimplyLocalize.Editor.Data;
using SimplyLocalize.Editor.Inspectors;
using SimplyLocalize.Editor.Utilities;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;

namespace SimplyLocalize.Editor
{
    /// <summary>
    /// Custom drawer for [LocalizationKey] attribute.
    /// Shows a search popup button instead of a plain text field.
    /// If [LocalizationPreview] is also present, shows translation preview below.
    /// </summary>
    [CustomPropertyDrawer(typeof(LocalizationKeyAttribute))]
    public class LocalizationKeyDrawer : PropertyDrawer
    {
        private static readonly float LineHeight = EditorGUIUtility.singleLineHeight;
        private static readonly float Spacing = EditorGUIUtility.standardVerticalSpacing;

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.String)
                return LineHeight;

            float height = LineHeight; // Main row (label + popup + add button)

            var previewAttr = GetPreviewAttribute();

            if (previewAttr != null && !string.IsNullOrEmpty(property.stringValue))
            {
                var config = EditorDataCache.Config;

                if (config != null)
                {
                    if (previewAttr.ShowAllLanguages)
                    {
                        int langCount = config.languages.Count(p => p != null);
                        height += (LineHeight + Spacing) * langCount;
                    }
                    else
                    {
                        height += LineHeight + Spacing;
                    }
                }
            }

            return height;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.String)
            {
                EditorGUI.PropertyField(position, property, label);
                return;
            }

            EditorGUI.BeginProperty(position, label, property);

            var allKeys = EditorDataCache.AllKeys;
            string currentKey = property.stringValue;
            bool keyExists = !string.IsNullOrEmpty(currentKey) && allKeys.Contains(currentKey);
            bool isEmpty = string.IsNullOrEmpty(currentKey);

            // Main row
            var mainRect = new Rect(position.x, position.y, position.width, LineHeight);

            // Label
            var labelRect = new Rect(mainRect.x, mainRect.y,
                EditorGUIUtility.labelWidth, LineHeight);
            EditorGUI.LabelField(labelRect, label);

            // Popup button
            float btnX = labelRect.xMax + 2;
            bool hasAddBtn = !isEmpty && !keyExists;
            float addBtnWidth = hasAddBtn ? 42 : 0;
            float popupWidth = mainRect.xMax - btnX - addBtnWidth;

            var popupRect = new Rect(btnX, mainRect.y, popupWidth, LineHeight);

            // Display text
            string display = isEmpty ? "<None>"
                : keyExists ? FormatKey(currentKey)
                : $"<None> : ({currentKey})";

            Color prev = GUI.color;
            if (!isEmpty && !keyExists) GUI.color = new Color(1f, 0.35f, 0.35f);

            if (GUI.Button(popupRect, display, EditorStyles.popup))
            {
                OpenSearchWindow(property, allKeys);
            }

            GUI.color = prev;

            // Add button for unknown keys
            if (hasAddBtn)
            {
                var addRect = new Rect(popupRect.xMax + 2, mainRect.y, 40, LineHeight);

                if (GUI.Button(addRect, "Add"))
                {
                    AddKeyToData(currentKey);
                    property.serializedObject.ApplyModifiedProperties();
                }
            }

            // Stale key detection
            if (!isEmpty && !keyExists)
            {
                var data = EditorDataCache.Data;

                if (data != null)
                {
                    string newKey = data.FindKeyByPreviousName(currentKey);

                    if (!string.IsNullOrEmpty(newKey))
                    {
                        // Show inline fix (we'll draw it in the preview area)
                    }
                }
            }

            // Preview (if [LocalizationPreview] is present)
            var previewAttr = GetPreviewAttribute();

            if (previewAttr != null && !isEmpty)
            {
                float y = mainRect.yMax + Spacing;
                DrawPreview(position, y, currentKey, previewAttr.ShowAllLanguages);
            }

            EditorGUI.EndProperty();
        }

        private void DrawPreview(Rect position, float startY, string key, bool showAll)
        {
            var data = EditorDataCache.Data;
            var config = EditorDataCache.Config;
            if (data == null || config == null) return;

            var richStyle = new GUIStyle(EditorStyles.helpBox)
            {
                richText = true,
                fontSize = 10,
                wordWrap = true
            };

            float y = startY;
            float indent = EditorGUIUtility.labelWidth + 2;

            if (showAll)
            {
                foreach (var profile in config.languages)
                {
                    if (profile == null) continue;

                    string value = data.GetTranslation(key, profile.Code) ?? "";
                    string display = string.IsNullOrEmpty(value)
                        ? "<color=#CC5555>(missing)</color>"
                        : ParameterHighlighter.Highlight(value);

                    var labelRect = new Rect(position.x, y,
                        EditorGUIUtility.labelWidth - 4, LineHeight);
                    EditorGUI.LabelField(labelRect, $"{profile.Code}",
                        EditorStyles.miniLabel);

                    var valueRect = new Rect(position.x + indent, y,
                        position.width - indent, LineHeight);
                    EditorGUI.LabelField(valueRect, display, richStyle);

                    y += LineHeight + Spacing;
                }
            }
            else
            {
                // Default language only
                string defaultLang = config.DefaultLanguageCode;

                if (!string.IsNullOrEmpty(defaultLang))
                {
                    string value = data.GetTranslation(key, defaultLang) ?? "";
                    string display = string.IsNullOrEmpty(value)
                        ? "<color=#CC5555>(missing)</color>"
                        : ParameterHighlighter.Highlight(value);

                    var valueRect = new Rect(position.x + indent, y,
                        position.width - indent, LineHeight);
                    EditorGUI.LabelField(valueRect, display, richStyle);
                }
            }
        }

        private LocalizationPreviewAttribute GetPreviewAttribute()
        {
            if (fieldInfo == null) return null;
            return fieldInfo.GetCustomAttribute<LocalizationPreviewAttribute>();
        }

        private static void OpenSearchWindow(SerializedProperty property, List<string> keys)
        {
            var searchWindow = ScriptableObject.CreateInstance<KeySearchWindow>();

            searchWindow.Init(new List<string>(keys), selectedKey =>
            {
                property.serializedObject.Update();
                property.stringValue = selectedKey ?? "";
                property.serializedObject.ApplyModifiedProperties();

                if (!string.IsNullOrEmpty(selectedKey) && !keys.Contains(selectedKey))
                    AddKeyToData(selectedKey);

                EditorDataCache.Invalidate();
            });

            var mousePos = GUIUtility.GUIToScreenPoint(Event.current.mousePosition);
            SearchWindow.Open(new SearchWindowContext(mousePos), searchWindow);
        }

        private static string FormatKey(string key) =>
            key.Contains('/') ? $"{key.Split('/')[^1]}    ({key})" : key;

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