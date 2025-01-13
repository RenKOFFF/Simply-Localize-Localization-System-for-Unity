using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SimplyLocalize.Editor
{
    [CustomPropertyDrawer(typeof(LocalizationKey))]
    public class LocalizedStringDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var keyProperty = property.FindPropertyRelative("_key");

            var currentIndex = LocalizeEditor.GetLocalizationKeysData().Keys.IndexOf(keyProperty.stringValue);

            var displayedOptions = new List<string>();
            displayedOptions.AddRange(LocalizeEditor.GetLocalizationKeysData().Keys);
            
            var isErrorKey = currentIndex == -1;
            if (isErrorKey)
                displayedOptions.Insert(0, $"<None> : ({keyProperty.stringValue})");
            
            var newIndex = EditorGUI.Popup(position, label.text, currentIndex, displayedOptions.ToArray());

            if (isErrorKey && newIndex == 0 || newIndex == currentIndex)
            {
                return;
            }
            
            if (isErrorKey)
                newIndex -= 1;
                
            keyProperty.stringValue = LocalizeEditor.GetLocalizationKeysData().Keys[newIndex];
            property.serializedObject.ApplyModifiedProperties();
        }
    }
}