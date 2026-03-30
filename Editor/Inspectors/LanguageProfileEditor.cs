using UnityEditor;
using UnityEngine;

namespace SimplyLocalize.Editor.Inspectors
{
    [CustomEditor(typeof(LanguageProfile))]
    public class LanguageProfileEditor : UnityEditor.Editor
    {
        // Identity
        private SerializedProperty _languageCode;
        private SerializedProperty _displayName;
        private SerializedProperty _systemLanguage;

        // Fallback
        private SerializedProperty _fallbackProfile;

        // Assets
        private SerializedProperty _hasText;
        private SerializedProperty _hasSprites;
        private SerializedProperty _hasAudio;

        // Font
        private SerializedProperty _overrideFont;
        private SerializedProperty _primaryFont;
        private SerializedProperty _fallbackFont;
        private SerializedProperty _legacyFont;

        // Typography
        private SerializedProperty _overrideTypography;
        private SerializedProperty _fontSizeMultiplier;
        private SerializedProperty _fontWeight;
        private SerializedProperty _fontStyle;

        // Spacing
        private SerializedProperty _overrideSpacing;
        private SerializedProperty _lineSpacingAdjustment;
        private SerializedProperty _characterSpacingAdjustment;
        private SerializedProperty _wordSpacingAdjustment;

        // Layout
        private SerializedProperty _overrideLayout;
        private SerializedProperty _textDirection;
        private SerializedProperty _overrideAlignment;
        private SerializedProperty _alignmentOverride;

        private void OnEnable()
        {
            _languageCode = serializedObject.FindProperty("languageCode");
            _displayName = serializedObject.FindProperty("displayName");
            _systemLanguage = serializedObject.FindProperty("systemLanguage");

            _fallbackProfile = serializedObject.FindProperty("fallbackProfile");

            _hasText = serializedObject.FindProperty("hasText");
            _hasSprites = serializedObject.FindProperty("hasSprites");
            _hasAudio = serializedObject.FindProperty("hasAudio");

            _overrideFont = serializedObject.FindProperty("overrideFont");
            _primaryFont = serializedObject.FindProperty("primaryFont");
            _fallbackFont = serializedObject.FindProperty("fallbackFont");
            _legacyFont = serializedObject.FindProperty("legacyFont");

            _overrideTypography = serializedObject.FindProperty("overrideTypography");
            _fontSizeMultiplier = serializedObject.FindProperty("fontSizeMultiplier");
            _fontWeight = serializedObject.FindProperty("fontWeight");
            _fontStyle = serializedObject.FindProperty("fontStyle");

            _overrideSpacing = serializedObject.FindProperty("overrideSpacing");
            _lineSpacingAdjustment = serializedObject.FindProperty("lineSpacingAdjustment");
            _characterSpacingAdjustment = serializedObject.FindProperty("characterSpacingAdjustment");
            _wordSpacingAdjustment = serializedObject.FindProperty("wordSpacingAdjustment");

            _overrideLayout = serializedObject.FindProperty("overrideLayout");
            _textDirection = serializedObject.FindProperty("textDirection");
            _overrideAlignment = serializedObject.FindProperty("overrideAlignment");
            _alignmentOverride = serializedObject.FindProperty("alignmentOverride");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            // Identity — always shown
            EditorGUILayout.LabelField("Language Identity", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_languageCode);
            EditorGUILayout.PropertyField(_displayName);
            EditorGUILayout.PropertyField(_systemLanguage);

            EditorGUILayout.Space(8);

            // Fallback — always shown
            EditorGUILayout.LabelField("Fallback", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_fallbackProfile,
                new GUIContent("Per-language fallback",
                    "If a key is missing, try this language before the global fallback.\n" +
                    "Example: Ukrainian → Russian → English (global)"));

            EditorGUILayout.Space(8);

            // Assets — always shown
            EditorGUILayout.LabelField("Available Assets", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_hasText);
            EditorGUILayout.PropertyField(_hasSprites);
            EditorGUILayout.PropertyField(_hasAudio);

            EditorGUILayout.Space(8);

            // Font — toggle hides content
            DrawOverrideSection("Font", _overrideFont, () =>
            {
                EditorGUILayout.PropertyField(_primaryFont, new GUIContent("TMP Font"));
                EditorGUILayout.PropertyField(_fallbackFont, new GUIContent("TMP Fallback"));
                EditorGUILayout.PropertyField(_legacyFont, new GUIContent("Legacy Font"));
            });

            // Typography — toggle hides content
            DrawOverrideSection("Typography", _overrideTypography, () =>
            {
                EditorGUILayout.PropertyField(_fontSizeMultiplier);
                EditorGUILayout.PropertyField(_fontWeight);
                EditorGUILayout.PropertyField(_fontStyle);
            });

            // Spacing — toggle hides content
            DrawOverrideSection("Spacing", _overrideSpacing, () =>
            {
                EditorGUILayout.PropertyField(_lineSpacingAdjustment);
                EditorGUILayout.PropertyField(_characterSpacingAdjustment);
                EditorGUILayout.PropertyField(_wordSpacingAdjustment);
            });

            // Layout — toggle hides content
            DrawOverrideSection("Layout", _overrideLayout, () =>
            {
                EditorGUILayout.PropertyField(_textDirection);
                EditorGUILayout.PropertyField(_overrideAlignment);

                if (_overrideAlignment.boolValue)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(_alignmentOverride);
                    EditorGUI.indentLevel--;
                }
            });

            serializedObject.ApplyModifiedProperties();
        }

        private static void DrawOverrideSection(string label, SerializedProperty overrideToggle,
            System.Action drawContent)
        {
            EditorGUILayout.Space(4);

            EditorGUILayout.BeginHorizontal();
            overrideToggle.boolValue = EditorGUILayout.ToggleLeft(
                label, overrideToggle.boolValue, EditorStyles.boldLabel);
            EditorGUILayout.EndHorizontal();

            if (overrideToggle.boolValue)
            {
                EditorGUI.indentLevel++;
                drawContent();
                EditorGUI.indentLevel--;
            }
            else
            {
                EditorGUILayout.LabelField("  (using original component settings)",
                    EditorStyles.miniLabel);
            }
        }
    }
}