using System;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace SimplyLocalize.Editor
{
    [CustomEditor(typeof(LocalizationText), true)]
    [CanEditMultipleObjects]
    public class LocalizationTextEditor : UnityEditor.Editor
    {
        private SerializedProperty _ignoreFontChangingProp;
        private SerializedProperty _overrideTextsElementProp;
        private SerializedProperty _localizationKeyProp;
        private SerializedProperty _textElementProp;
        private SerializedProperty _textElementLegacyProp;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            _ignoreFontChangingProp = serializedObject.FindProperty("_ignoreFontChanging");
            _overrideTextsElementProp = serializedObject.FindProperty("_overrideTextElements");
            _localizationKeyProp = serializedObject.FindProperty("_localizationKey");
            _textElementProp = serializedObject.FindProperty("_textElement");
            _textElementLegacyProp = serializedObject.FindProperty("_textElementLegacy");

            if (_localizationKeyProp == null || _overrideTextsElementProp == null)
                return;
            
            EditorGUILayout.PropertyField(_localizationKeyProp);
            
            EditorGUILayout.PropertyField(_ignoreFontChangingProp);
            EditorGUILayout.Space();
            
            EditorGUILayout.PropertyField(_overrideTextsElementProp);

            var hasTextTMP = HasTextComponent(_textElementProp);
            var hasTextLegacy = HasTextComponent(_textElementLegacyProp);

            if (_overrideTextsElementProp.boolValue)
            {
                var targetObject = (LocalizationText)serializedObject.targetObject;

                if (targetObject != null)
                {
                    _textElementProp.objectReferenceValue ??= targetObject.GetComponent<TMP_Text>();
                    _textElementLegacyProp.objectReferenceValue ??= targetObject.GetComponent<UnityEngine.UI.Text>();
                }
                
                EditorGUILayout.PropertyField(_textElementProp);
                EditorGUILayout.PropertyField(_textElementLegacyProp);
            }
            else
            {
                var targetObject = (LocalizationText)serializedObject.targetObject;

                if (targetObject != null)
                {
                    _textElementProp.objectReferenceValue = targetObject.GetComponent<TMP_Text>();
                    _textElementLegacyProp.objectReferenceValue = targetObject.GetComponent<UnityEngine.UI.Text>();
                }

                if (hasTextTMP)
                {
                    EditorGUI.BeginDisabledGroup(true);
                    EditorGUILayout.PropertyField(_textElementProp);
                    EditorGUI.EndDisabledGroup();
                }
                
                if (hasTextLegacy)
                {
                    EditorGUI.BeginDisabledGroup(true);
                    EditorGUILayout.PropertyField(_textElementLegacyProp);
                    EditorGUI.EndDisabledGroup();
                }
            }
            
            if (!hasTextLegacy && !hasTextTMP)
            {
                EditorGUILayout.HelpBox("You must add at least one text component.", MessageType.Warning);
            }

            // GetType check is necessary for correct drawing of the button
            DrawTranslateButton(GetType() != typeof(LocalizationTextEditor)); 

            serializedObject.ApplyModifiedProperties();
        }

        private bool HasTextComponent(SerializedProperty property)
        {
            return property.propertyType != SerializedPropertyType.ObjectReference || property.objectReferenceValue != null;
        }

        protected void DrawTranslateButton(bool ignoreThisMethod)
        {
            if (ignoreThisMethod)
                return;
            
            EditorGUILayout.Space();

            var myButtonContent = new GUIContent("Set translation", "Set the translation for this key in the component using the current language.");
            if (GUILayout.Button(myButtonContent, LocalizationEditorStyles.ButtonStyle))
            {
                var localizationKey = _localizationKeyProp.boxedValue as LocalizationKey;
                var localizationCode = LocalizeEditor.LocalizationKeysData.DefaultLanguage.i18nLang;
                LocalizeEditor.LocalizationKeysData.Translations[localizationCode].TryGetValue(localizationKey, out var translated);
                
                var hasTextTMP = HasTextComponent(_textElementProp);
                var hasTextLegacy = HasTextComponent(_textElementLegacyProp);
                
                if (hasTextTMP)
                {
                    var textElement = _textElementProp.objectReferenceValue as TMP_Text;
                    Undo.RecordObject(textElement, "Set translation to component TMP");
                    
                    textElement.text = translated;
                }
                
                if (hasTextLegacy)
                {
                    var textElement = _textElementLegacyProp.objectReferenceValue as UnityEngine.UI.Text;
                    Undo.RecordObject(textElement, "Set translation to legacy text component");
                    
                    textElement.text = translated;
                }
            }
        }
    }
}