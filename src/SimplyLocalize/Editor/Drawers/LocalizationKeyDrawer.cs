using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace SimplyLocalize.Editor
{
    [CustomPropertyDrawer(typeof(LocalizationKeyAttribute))]
    public class LocalizationKeyDrawer : PropertyDrawer
    {
        private LocalizationPreviewAttribute Preview => fieldInfo?.GetCustomAttribute<LocalizationPreviewAttribute>();
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label) =>
            LocalizationStringFieldGUI.Height(property, Preview != null, Preview?.ShowAllLanguages ?? false);
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label) =>
            LocalizationStringFieldGUI.Draw(position, property, label, true, Preview != null, Preview?.ShowAllLanguages ?? false);
    }
}
