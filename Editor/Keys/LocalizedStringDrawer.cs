using System;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;

namespace SimplyLocalize.Editor
{
    [CustomPropertyDrawer(typeof(LocalizationKey))]
    public class LocalizedStringDrawer : PropertyDrawer
    {
        private string _addNewKey;
        
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var errorColor = new Color(1, 0.3f, 0.3f);
            
            var keyProperty = property.FindPropertyRelative("_key");

            var keys = LocalizeEditor.GetLocalizationKeysData().Keys;
            var isErrorKey = keys.IndexOf(keyProperty.stringValue) < 0 || string.IsNullOrEmpty(keyProperty.stringValue);
            var errorKey = $"<None> : ({keyProperty.stringValue})";

            var currentKey = keyProperty.stringValue;
            if (isErrorKey == false)
            {
                if (currentKey.Split('/', StringSplitOptions.RemoveEmptyEntries).Length > 1)
                {
                    var splitKey = currentKey.Split('/');
                    
                    currentKey = $"{splitKey[^1]} ({currentKey})";
                }
                else
                {
                    currentKey = $"{currentKey}";
                }
            }
            else
            {
                currentKey = $"{errorKey}";
            }

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, LocalizationEditorStyles.LabelStylePrefix);
            
            GUI.color = isErrorKey ? errorColor : Color.white;
            
            if (GUILayout.Button(currentKey, LocalizationEditorStyles.PopupStyle, GUILayout.MaxWidth(Screen.width * .7f)))
            {
                var context = new SearchWindowContext(GUIUtility.GUIToScreenPoint(Event.current.mousePosition));
                
                var searchWindowProvider = ScriptableObject.CreateInstance<StringKeysSearchWindow>();
                searchWindowProvider.SetKeys(keys, OnSelectEntry);

                SearchWindow.Open(context, searchWindowProvider);
            }
            
            GUI.color = Color.white;
            
            if (isErrorKey)
            {
                if (GUILayout.Button("Add", LocalizationEditorStyles.ButtonStyle))
                {
                    LocalizeEditor.TryAddNewKey(keyProperty.stringValue, false);
                }
            }
            
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space();

            EditorGUILayout.BeginHorizontal();

            var canAddNewKey = LocalizeEditor.CanAddNewKey(_addNewKey);
            GUI.color = canAddNewKey ? Color.white : errorColor;
            
            _addNewKey = EditorGUILayout.TextField(_addNewKey, LocalizationEditorStyles.KeyStyle);
            
            GUI.color = Color.white;

            if (string.IsNullOrEmpty(_addNewKey) || canAddNewKey == false)
            {
                GUI.enabled = false;
            }
            
            if (GUILayout.Button("Add new key", LocalizationEditorStyles.ButtonStyle))
            {
                _addNewKey = _addNewKey.ToCorrectLocalizationKeyName();
                LocalizeEditor.TryAddNewKey(_addNewKey, false);
                OnSelectEntry(_addNewKey);
                
                _addNewKey = string.Empty;
            }
            
            GUI.enabled = true;
            
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);

            return;

            void OnSelectEntry(string key)
            {
                if (string.IsNullOrEmpty(key)) return;
                
                keyProperty.stringValue = key;
                property.serializedObject.ApplyModifiedProperties();
            }
        }
    }
}