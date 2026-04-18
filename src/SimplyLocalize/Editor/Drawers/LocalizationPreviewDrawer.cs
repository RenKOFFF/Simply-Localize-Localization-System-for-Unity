using System.Linq;
using SimplyLocalize.Editor.Data;
using SimplyLocalize.Editor.Utilities;
using UnityEditor;
using UnityEngine;

namespace SimplyLocalize.Editor
{
    /// <summary>
    /// Custom drawer for [LocalizationPreview] when used without [LocalizationKey].
    /// Shows a normal text field + read-only translation preview below.
    /// If [LocalizationKey] is also present, that drawer handles everything — this one is skipped.
    /// </summary>
    [CustomPropertyDrawer(typeof(LocalizationPreviewAttribute))]
    public class LocalizationPreviewDrawer : PropertyDrawer
    {
        private static readonly float LineHeight = EditorGUIUtility.singleLineHeight;
        private static readonly float Spacing = EditorGUIUtility.standardVerticalSpacing;

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.String)
                return LineHeight;

            float height = LineHeight; // Text field

            var attr = (LocalizationPreviewAttribute)attribute;
            string key = property.stringValue;

            if (!string.IsNullOrEmpty(key))
            {
                var config = EditorDataCache.Config;

                if (config != null)
                {
                    if (attr.ShowAllLanguages)
                    {
                        int count = config.languages.Count(p => p != null);
                        height += (LineHeight + Spacing) * count;
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

            // Normal text field
            var fieldRect = new Rect(position.x, position.y, position.width, LineHeight);
            EditorGUI.PropertyField(fieldRect, property, label);

            // Preview below
            var attr = (LocalizationPreviewAttribute)attribute;
            string key = property.stringValue;

            if (!string.IsNullOrEmpty(key))
            {
                float y = fieldRect.yMax + Spacing;
                DrawPreview(position, y, key, attr.ShowAllLanguages);
            }

            EditorGUI.EndProperty();
        }

        private static void DrawPreview(Rect position, float startY, string key, bool showAll)
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
                    EditorGUI.LabelField(labelRect, profile.displayName, EditorStyles.miniLabel);

                    var valueRect = new Rect(position.x + indent, y,
                        position.width - indent, LineHeight);
                    EditorGUI.LabelField(valueRect, display, richStyle);

                    y += LineHeight + Spacing;
                }
            }
            else
            {
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
    }
}