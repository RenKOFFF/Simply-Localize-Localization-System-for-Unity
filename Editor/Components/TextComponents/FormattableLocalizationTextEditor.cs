using System;
using UnityEditor;

namespace SimplyLocalize.Editor
{
    [CustomEditor(typeof(FormattableLocalizationText))]
    [CanEditMultipleObjects]
    public class FormattableLocalizationTextEditor : LocalizationTextEditor
    {
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();
            
            var defaultValuesProp = serializedObject.FindProperty("_defaultValues");
            EditorGUILayout.PropertyField(defaultValuesProp);
            
            DrawTranslateButton(false);
            serializedObject.ApplyModifiedProperties();
            
            DrawReplaceToButton("Replace to Localization Text", ReplaceFormattableToBase, ReplaceFormattableToBaseValidate);
        }
        
        private void ReplaceFormattableToBase()
        {
            var formattable = (FormattableLocalizationText)serializedObject.targetObject;

            if (formattable == null)
                throw new ArgumentNullException("Target object is not " + nameof(FormattableLocalizationText) + ".");
            
            var gameObject = formattable.gameObject;

            var soFormattable = new SerializedObject(formattable);
            soFormattable.Update();
            var localizationKeyFormattable = soFormattable.FindProperty("_localizationKey");

            var valueFormattable = localizationKeyFormattable.FindPropertyRelative("_key");

            Undo.RegisterCompleteObjectUndo(gameObject, $"Replace {formattable.GetType().Name} with {nameof(LocalizationText)}");
            Undo.DestroyObjectImmediate(formattable);

            var localizationText = Undo.AddComponent(gameObject, typeof(LocalizationText));

            var soLocalizationText = new SerializedObject(localizationText);
            var localizationKeyText = soLocalizationText.FindProperty("_localizationKey");

            var valueText = localizationKeyText.FindPropertyRelative("_key");

            valueText.stringValue = valueFormattable.stringValue;

            soLocalizationText.ApplyModifiedProperties();
            Selection.activeObject = localizationText;
        }

        private bool ReplaceFormattableToBaseValidate()
        {
            return serializedObject.targetObject is FormattableLocalizationText formattable &&
                   formattable != null && 
                   formattable.GetType() == typeof(FormattableLocalizationText);
        }
    }
}