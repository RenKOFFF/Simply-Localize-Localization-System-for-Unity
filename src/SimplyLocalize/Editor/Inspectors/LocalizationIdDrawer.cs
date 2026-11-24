using UnityEditor;
using UnityEngine;

namespace SimplyLocalize.Editor.Inspectors
{
    [CustomPropertyDrawer(typeof(LocalizationIdAttribute))]
    internal sealed class LocalizationIdDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            using (new EditorGUI.DisabledScope(true)) EditorGUI.PropertyField(position, property, label);
        }
    }
}
