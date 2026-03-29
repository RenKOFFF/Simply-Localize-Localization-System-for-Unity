using System.Linq;
using SimplyLocalize.Components;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace SimplyLocalize.Editor.Inspectors
{
    [CustomEditor(typeof(LocalizedProfileOverride))]
    public class LocalizedProfileOverrideEditor : UnityEditor.Editor
    {
        private SerializedProperty _entries;

        private void OnEnable()
        {
            _entries = serializedObject.FindProperty("_entries");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.LabelField("Profile overrides per language", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Override specific parts of the global language profile for this text.\n" +
                "Checked sections use local values; unchecked sections use the global profile.",
                MessageType.Info);

            EditorGUILayout.Space(4);

            for (int i = 0; i < _entries.arraySize; i++)
            {
                DrawEntry(i);
                EditorGUILayout.Space(2);
            }

            EditorGUILayout.Space(4);

            if (GUILayout.Button("+ Add language override"))
            {
                _entries.InsertArrayElementAtIndex(_entries.arraySize);
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawEntry(int index)
        {
            var entry = _entries.GetArrayElementAtIndex(index);
            var profileProp = entry.FindPropertyRelative("profile");

            string profileName = profileProp.objectReferenceValue != null
                ? ((LanguageProfile)profileProp.objectReferenceValue).displayName
                : "(none)";

            // Entry header with foldout
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PropertyField(profileProp, new GUIContent($"Language: {profileName}"));

            if (GUILayout.Button("X", GUILayout.Width(22)))
            {
                _entries.DeleteArrayElementAtIndex(index);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
                return;
            }

            EditorGUILayout.EndHorizontal();

            if (profileProp.objectReferenceValue == null)
            {
                EditorGUILayout.LabelField("Assign a LanguageProfile to configure overrides.",
                    EditorStyles.miniLabel);
                EditorGUILayout.EndVertical();
                return;
            }

            EditorGUI.indentLevel++;

            // Font & Material section
            var overrideFont = entry.FindPropertyRelative("overrideFont");
            overrideFont.boolValue = EditorGUILayout.ToggleLeft(
                "Override font & material", overrideFont.boolValue, EditorStyles.boldLabel);

            if (overrideFont.boolValue)
            {
                EditorGUI.indentLevel++;

                var fontProp = entry.FindPropertyRelative("font");
                EditorGUILayout.PropertyField(fontProp);

                // Material dropdown — shows materials compatible with the selected font
                var materialProp = entry.FindPropertyRelative("material");
                var font = fontProp.objectReferenceValue as TMP_FontAsset;

                if (font != null)
                {
                    DrawMaterialDropdown(materialProp, font);
                }
                else
                {
                    EditorGUILayout.PropertyField(materialProp);
                }

                EditorGUI.indentLevel--;
            }

            // Typography section
            var overrideTypo = entry.FindPropertyRelative("overrideTypography");
            overrideTypo.boolValue = EditorGUILayout.ToggleLeft(
                "Override typography", overrideTypo.boolValue, EditorStyles.boldLabel);

            if (overrideTypo.boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(entry.FindPropertyRelative("fontSizeMultiplier"));
                EditorGUILayout.PropertyField(entry.FindPropertyRelative("fontWeight"));
                EditorGUILayout.PropertyField(entry.FindPropertyRelative("fontStyle"));
                EditorGUI.indentLevel--;
            }

            // Spacing section
            var overrideSpacing = entry.FindPropertyRelative("overrideSpacing");
            overrideSpacing.boolValue = EditorGUILayout.ToggleLeft(
                "Override spacing", overrideSpacing.boolValue, EditorStyles.boldLabel);

            if (overrideSpacing.boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(entry.FindPropertyRelative("lineSpacingAdjustment"));
                EditorGUILayout.PropertyField(entry.FindPropertyRelative("characterSpacingAdjustment"));
                EditorGUILayout.PropertyField(entry.FindPropertyRelative("wordSpacingAdjustment"));
                EditorGUI.indentLevel--;
            }

            // Layout section
            var overrideLayout = entry.FindPropertyRelative("overrideLayout");
            overrideLayout.boolValue = EditorGUILayout.ToggleLeft(
                "Override layout", overrideLayout.boolValue, EditorStyles.boldLabel);

            if (overrideLayout.boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(entry.FindPropertyRelative("textDirection"));

                var overrideAlign = entry.FindPropertyRelative("overrideAlignment");
                EditorGUILayout.PropertyField(overrideAlign);

                if (overrideAlign.boolValue)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(entry.FindPropertyRelative("alignmentOverride"));
                    EditorGUI.indentLevel--;
                }

                EditorGUI.indentLevel--;
            }

            EditorGUI.indentLevel--;
            EditorGUILayout.EndVertical();
        }

        /// <summary>
        /// Draws a dropdown of materials that are compatible with the given TMP font asset.
        /// Mimics TMP's built-in Material Preset dropdown.
        /// </summary>
        private static void DrawMaterialDropdown(SerializedProperty materialProp, TMP_FontAsset font)
        {
            // Find all materials in the project that use this font's atlas
            var fontMaterials = FindFontMaterials(font);

            if (fontMaterials.Length == 0)
            {
                EditorGUILayout.PropertyField(materialProp, new GUIContent("Material"));
                return;
            }

            // Build names array
            var names = new string[fontMaterials.Length + 1];
            names[0] = "(default)";
            int currentIndex = 0;

            for (int i = 0; i < fontMaterials.Length; i++)
            {
                names[i + 1] = fontMaterials[i].name;

                if (materialProp.objectReferenceValue == fontMaterials[i])
                    currentIndex = i + 1;
            }

            int newIndex = EditorGUILayout.Popup("Material preset", currentIndex, names);

            if (newIndex != currentIndex)
            {
                materialProp.objectReferenceValue = newIndex == 0 ? null : fontMaterials[newIndex - 1];
            }
        }

        /// <summary>
        /// Finds all Material assets that reference the same atlas texture as the font.
        /// </summary>
        private static Material[] FindFontMaterials(TMP_FontAsset font)
        {
            if (font == null || font.atlasTexture == null)
                return System.Array.Empty<Material>();

            var atlasTexture = font.atlasTexture;

            // Search for materials that use this atlas
            var guids = AssetDatabase.FindAssets("t:Material");
            var materials = new System.Collections.Generic.List<Material>();

            // Always include the font's default material first
            if (font.material != null)
                materials.Add(font.material);

            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);

                if (mat == null || mat == font.material)
                    continue;

                // Check if this material uses the same atlas texture
                if (mat.HasTexture("_MainTex") && mat.GetTexture("_MainTex") == atlasTexture)
                    materials.Add(mat);
            }

            return materials.ToArray();
        }
    }
}