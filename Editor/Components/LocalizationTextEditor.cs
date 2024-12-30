using TMPro;
using UnityEditor;

namespace SimplyLocalize.Editor
{
    [CustomEditor(typeof(LocalizationText), true)]
    public class LocalizationTextEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var overrideTextsElementProp = serializedObject.FindProperty("_overrideTextElements");
            var localizationKeyProp = serializedObject.FindProperty("_localizationKey");
            var textElementProp = serializedObject.FindProperty("_textElement");
            var textElementLegacyProp = serializedObject.FindProperty("_textElementLegacy");

            EditorGUILayout.PropertyField(localizationKeyProp);
            EditorGUILayout.PropertyField(overrideTextsElementProp);

            var hasTextLegacy = textElementProp.propertyType != SerializedPropertyType.ObjectReference || textElementProp.objectReferenceValue != null;
            var hasTextTMP = textElementLegacyProp.propertyType != SerializedPropertyType.ObjectReference || textElementLegacyProp.objectReferenceValue != null;

            if (overrideTextsElementProp.boolValue)
            {
                var targetObject = (LocalizationText)serializedObject.targetObject;

                if (targetObject != null)
                {
                    textElementProp.objectReferenceValue ??= targetObject.GetComponent<TMP_Text>();
                    textElementLegacyProp.objectReferenceValue ??= targetObject.GetComponent<UnityEngine.UI.Text>();
                }
                
                EditorGUILayout.PropertyField(textElementProp);
                EditorGUILayout.PropertyField(textElementLegacyProp);
            }
            else
            {
                var targetObject = (LocalizationText)serializedObject.targetObject;

                if (targetObject != null)
                {
                    textElementProp.objectReferenceValue = targetObject.GetComponent<TMP_Text>();
                    textElementLegacyProp.objectReferenceValue = targetObject.GetComponent<UnityEngine.UI.Text>();
                }
                
                if (hasTextLegacy)
                {
                    EditorGUI.BeginDisabledGroup(true);
                    EditorGUILayout.PropertyField(textElementProp);
                    EditorGUI.EndDisabledGroup();
                }

                if (hasTextTMP)
                {
                    EditorGUI.BeginDisabledGroup(true);
                    EditorGUILayout.PropertyField(textElementLegacyProp);
                    EditorGUI.EndDisabledGroup();
                }
            }
            
            if (!hasTextLegacy && !hasTextTMP)
            {
                EditorGUILayout.HelpBox("You must add at least one text component.", MessageType.Warning);
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}