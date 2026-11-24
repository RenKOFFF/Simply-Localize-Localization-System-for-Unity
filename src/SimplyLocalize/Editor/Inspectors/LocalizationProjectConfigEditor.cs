using SimplyLocalize.Editor.Windows;
using UnityEditor;
using UnityEngine;

namespace SimplyLocalize.Editor.Inspectors
{
    [CustomEditor(typeof(LocalizationProjectConfig), true)]
    public sealed class LocalizationProjectConfigEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var config = (LocalizationProjectConfig)target;
            if (GUILayout.Button("Open in Simply Localize")) LocalizationEditorWindow.OpenConfig(config);
            EditorGUILayout.HelpBox("The Simply Localize window is the main setup surface. The serialized definitions below remain available for direct inspection and editing; identities are read-only.", MessageType.Info);
            DrawDefaultInspector();
            foreach (var issue in config.Validate()) EditorGUILayout.HelpBox(issue.Path + ": " + issue.Message, issue.Severity == LocalizationIssueSeverity.Error ? MessageType.Error : MessageType.Warning);
        }
    }
}
