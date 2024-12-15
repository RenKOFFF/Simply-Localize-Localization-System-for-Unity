using SimplyLocalize.Runtime.Components.TextComponents;
using UnityEditor;

namespace SimplyLocalize.Editor.Components
{
    [CustomEditor(typeof(FormattableLocalizationText))]
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