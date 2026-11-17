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
            if (GUILayout.Button("Open in Simply Localize"))
                LocalizationEditorWindow.Open();
            EditorGUILayout.HelpBox("This is a legacy config. The channel architecture uses LocalizationProjectConfig. Rebuild the project configuration explicitly in Simply Localize; this asset is not used by the new runtime.", MessageType.Warning);
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.TextField("Startup address", LocalizationConfig.StartupResourcePath);
            using (new EditorGUI.DisabledScope(true)) DrawDefaultInspector();
        }
    }
}
