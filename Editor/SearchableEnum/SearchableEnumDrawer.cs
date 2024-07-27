// ---------------------------------------------------------------------------- 
// Author: Ryan Hipple
// Date:   05/01/2018
// ----------------------------------------------------------------------------

using System;
using SimplyLocalize.Runtime.Data.SearchableEnum;
using UnityEditor;
using UnityEngine;

namespace SimplyLocalize.Editor.SearchableEnum
{
    [CustomPropertyDrawer(typeof(SearchableStringEnumAttribute))]
    public class SearchableEnumDrawer : PropertyDrawer
    {
        private const string _TYPE_ERROR = "SearchableEnum can only be used on enum fields.";
        private int _idHash;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (!property.type.Contains(nameof(StringEnum)))
            {
                GUIStyle errorStyle = "CN EntryErrorIconSmall";
                
                var r = new Rect(position)
                {
                    width = errorStyle.fixedWidth
                };

                position.xMin = r.xMax;
                
                GUI.Label(r, "", errorStyle);
                GUI.Label(position, _TYPE_ERROR);
                
                return;
            }

            if (_idHash == 0)
            {
                _idHash = nameof(SearchableEnumDrawer).GetHashCode();
            }
            
            var id = GUIUtility.GetControlID(_idHash, FocusType.Keyboard, position);
            
            label = EditorGUI.BeginProperty(position, label, property);
            position = EditorGUI.PrefixLabel(position, id, label);
            
            var stringValueProperty = property.FindPropertyRelative("_stringValue");
            var valueProperty = property.FindPropertyRelative("_value");

            if (stringValueProperty == null || valueProperty == null)
                return;

            GUIContent buttonText;
            if (valueProperty.enumValueIndex < 0 || valueProperty.enumValueIndex >= valueProperty.enumDisplayNames.Length) 
            {
                buttonText = new GUIContent();
            }
            else 
            {
                buttonText = new GUIContent(valueProperty.enumDisplayNames[valueProperty.enumValueIndex]);
            }
            
            if (DropdownButton(id, position, buttonText))
            {
                void OnSelect(string key, int index)
                {
                    valueProperty.enumValueIndex = index;
                    stringValueProperty.stringValue = key;
                    
                    property.serializedObject.ApplyModifiedProperties();
                }

                SearchablePopup.Show(position, valueProperty.enumDisplayNames, valueProperty.enumValueIndex, OnSelect);
            }
            EditorGUI.EndProperty();
        }
        
        private static bool DropdownButton(int id, Rect position, GUIContent content)
        {
            var current = Event.current;
            switch (current.type)
            {
                case EventType.MouseDown:
                    if (position.Contains(current.mousePosition) && current.button == 0)
                    {
                        Event.current.Use();
                        return true;
                    }
                    break;
                case EventType.KeyDown:
                    if (GUIUtility.keyboardControl == id && current.character =='\n')
                    {
                        Event.current.Use();
                        return true;
                    }
                    break;
                case EventType.Repaint:
                    EditorStyles.popup.Draw(position, content, id, false);
                    break;
            }
            return false;
        }
    }
}
