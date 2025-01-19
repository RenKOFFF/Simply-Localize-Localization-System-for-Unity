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
            
            serializedObject.ApplyModifiedProperties();
        }
    }
}