using UnityEditor;
using UnityEngine;

namespace SimplyLocalize.Editor
{
    public static class LocalizationEditorStyles
    {
        public const int LINE_HEIGHT = 30;      
        public const int MAX_FIELD_HEIGHT = 400;

        public static GUIStyle LabelStylePrefix => new(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleLeft,
            fontStyle = FontStyle.Bold,
            fontSize = 14,
            fixedHeight = LINE_HEIGHT,
            wordWrap = true,
            normal = new GUIStyleState
            {
                textColor = Color.white
            },
            fixedWidth = 200
        };
        public static GUIStyle LabelStyle => new()
        {
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
            fontSize = 14,
            fixedHeight = LINE_HEIGHT,
            wordWrap = true,
            normal = new GUIStyleState
            {
                textColor = Color.white
            }
        };

        public static GUIStyle KeyStyle => new(GUI.skin.textField)
        {
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Italic,
            fontSize = 15,
            fixedHeight = LINE_HEIGHT,
        };

        public static GUIStyle PopupStyle => new(EditorStyles.popup)
        {
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Italic,
            fontSize = 15,
            fixedHeight = LINE_HEIGHT,
        };
        
        public static GUIStyle ToggleStyle => new(GUI.skin.toggle)
        {
            fixedHeight = LINE_HEIGHT,
        };

        public static GUIStyle TextAreaStyle => new(GUI.skin.textArea)
        {
            alignment = TextAnchor.UpperCenter,
            fontStyle = FontStyle.Italic,
            fontSize = 15,
        };

        public static GUIStyle ButtonStyle => new(GUI.skin.button)
        {
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
            fontSize = 14,
            fixedHeight = LINE_HEIGHT,
        };
    }
}