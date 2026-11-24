using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace SimplyLocalize.Editor
{
    [CustomPropertyDrawer(typeof(LocalizationPreviewAttribute))]
    public class LocalizationPreviewDrawer : PropertyDrawer
    {
        // Attribute order must not change whether the field is a picker.
        private bool Picker => fieldInfo?.GetCustomAttribute<LocalizationKeyAttribute>() != null;
        private bool All => ((LocalizationPreviewAttribute)attribute).ShowAllLanguages;
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label) => LocalizationStringFieldGUI.Height(property, true, All);
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label) => LocalizationStringFieldGUI.Draw(position, property, label, Picker, true, All);
    }
}
