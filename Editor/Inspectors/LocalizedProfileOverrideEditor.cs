using System.Linq;
using SimplyLocalize.Components;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace SimplyLocalize.Editor.Inspectors
{
    [CustomEditor(typeof(LocalizedProfileOverride))]
    public class LocalizedProfileOverrideEditor : UnityEditor.Editor
    {
        private SerializedProperty _entries;
        private bool _isTMP;
        private bool _isLegacy;
        private bool _noText;

        private void OnEnable()
        {
            _entries = serializedObject.FindProperty("_entries");
            DetectTextType();
        }

        private void DetectTextType()
        {
            var go = ((Component)target).gameObject;
            _isTMP = go.GetComponent<TMP_Text>() != null;
            _isLegacy = !_isTMP && go.GetComponent<Text>() != null;
            _noText = !_isTMP && !_isLegacy;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            // Show detected mode
            string mode = _isTMP ? "TextMeshPro" : _isLegacy ? "Legacy Text" : "No text component";
            EditorGUILayout.LabelField($"Profile overrides ({mode})", EditorStyles.boldLabel);

            if (_noText)
            {
                EditorGUILayout.HelpBox(
                    "No TMP_Text or legacy Text component found on this GameObject.\n" +
                    "Add a text component first.",
                    MessageType.Warning);
                serializedObject.ApplyModifiedProperties();
                return;
            }

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

            // ── Font section ──
            var overrideFont = entry.FindPropertyRelative("overrideFont");
            overrideFont.boolValue = EditorGUILayout.ToggleLeft(
                "Override font", overrideFont.boolValue, EditorStyles.boldLabel);

            if (overrideFont.boolValue)
            {
                EditorGUI.indentLevel++;

                if (_isTMP)
                {
                    var tmpFontProp = entry.FindPropertyRelative("tmpFont");
                    EditorGUILayout.PropertyField(tmpFontProp, new GUIContent("TMP Font"));

                    var tmpMaterialProp = entry.FindPropertyRelative("tmpMaterial");
                    var font = tmpFontProp.objectReferenceValue as TMP_FontAsset;

                    if (font != null)
                        DrawMaterialDropdown(tmpMaterialProp, font);
                    else
                        EditorGUILayout.PropertyField(tmpMaterialProp, new GUIContent("Material"));
                }
                else // legacy
                {
                    EditorGUILayout.PropertyField(
                        entry.FindPropertyRelative("legacyFont"), new GUIContent("Font"));
                }

                EditorGUI.indentLevel--;
            }

            // ── Typography section ──
            var overrideTypo = entry.FindPropertyRelative("overrideTypography");
            overrideTypo.boolValue = EditorGUILayout.ToggleLeft(
                "Override typography", overrideTypo.boolValue, EditorStyles.boldLabel);

            if (overrideTypo.boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(entry.FindPropertyRelative("fontSizeMultiplier"));

                if (_isTMP)
                {
                    EditorGUILayout.PropertyField(entry.FindPropertyRelative("fontWeight"));
                    EditorGUILayout.PropertyField(entry.FindPropertyRelative("fontStyle"),
                        new GUIContent("TMP Font Style"));
                }
                else
                {
                    EditorGUILayout.PropertyField(entry.FindPropertyRelative("legacyFontStyle"),
                        new GUIContent("Font Style"));
                }

                EditorGUI.indentLevel--;
            }

            // ── Spacing section (TMP only) ──
            if (_isTMP)
            {
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
            }

            // ── Layout section ──
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

                    if (_isTMP)
                    {
                        EditorGUILayout.PropertyField(
                            entry.FindPropertyRelative("tmpAlignmentOverride"),
                            new GUIContent("Alignment"));
                    }
                    else
                    {
                        EditorGUILayout.PropertyField(
                            entry.FindPropertyRelative("legacyAlignmentOverride"),
                            new GUIContent("Alignment"));
                    }

                    EditorGUI.indentLevel--;
                }

                EditorGUI.indentLevel--;
            }

            EditorGUI.indentLevel--;
            EditorGUILayout.EndVertical();
        }

        private static void DrawMaterialDropdown(SerializedProperty materialProp, TMP_FontAsset font)
        {
            var fontMaterials = FindFontMaterials(font);

            if (fontMaterials.Length == 0)
            {
                EditorGUILayout.PropertyField(materialProp, new GUIContent("Material"));
                return;
            }

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

        private static Material[] FindFontMaterials(TMP_FontAsset font)
        {
            if (font == null || font.atlasTexture == null)
                return System.Array.Empty<Material>();

            var atlasTexture = font.atlasTexture;
            var guids = AssetDatabase.FindAssets("t:Material");
            var materials = new System.Collections.Generic.List<Material>();

            if (font.material != null)
                materials.Add(font.material);

            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);

                if (mat == null || mat == font.material)
                    continue;

                if (mat.HasTexture("_MainTex") && mat.GetTexture("_MainTex") == atlasTexture)
                    materials.Add(mat);
            }

            return materials.ToArray();
        }
    }
}