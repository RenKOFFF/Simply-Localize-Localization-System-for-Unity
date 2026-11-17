using SimplyLocalize.Editor.Windows;
using UnityEditor;
using UnityEngine;

namespace SimplyLocalize.Editor.Inspectors
{
    [CustomEditor(typeof(LanguageProfile))]
    public class LanguageProfileEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            if (GUILayout.Button("Open in Simply Localize")) LocalizationEditorWindow.Open();
            EditorGUILayout.HelpBox("This is a legacy language profile. Configure languages and appearance in Simply Localize. These saved fields remain visible for reference; channel runtimes do not read this profile.", MessageType.Warning);
            using (new EditorGUI.DisabledScope(true)) DrawDefaultInspector();
        }
    }
}
