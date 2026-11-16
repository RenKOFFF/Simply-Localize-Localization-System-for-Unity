using SimplyLocalize.Editor.Windows;
using UnityEditor;
using UnityEngine;

namespace SimplyLocalize.Editor
{
    [CustomEditor(typeof(LocalizationConfig))]
    internal sealed class LocalizationConfigEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var config = (LocalizationConfig)target;
            if (GUILayout.Button("Open in Simply Localize"))
                LocalizationEditorWindow.OpenConfig(config);
            EditorGUILayout.HelpBox("Manage languages, translations and automatic startup in the Simply Localize window. The fields below are an alternative for direct editing.", MessageType.Info);
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.TextField("Startup address", LocalizationConfig.StartupResourcePath);
            DrawDefaultInspector();
            foreach (var issue in config.GetValidationErrors()) EditorGUILayout.HelpBox(issue, MessageType.Error);
        }
    }
}
