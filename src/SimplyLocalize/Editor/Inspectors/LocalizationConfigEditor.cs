using System;
using UnityEditor;
using UnityEngine;

namespace SimplyLocalize.Editor
{
    [CustomEditor(typeof(LocalizationConfig))]
    internal sealed class LocalizationConfigEditor : UnityEditor.Editor
    {
        private string _languages = "en,ru";
        private string _message;
        private bool _error;

        public override void OnInspectorGUI()
        {
            var config = (LocalizationConfig)target;
            EditorGUILayout.LabelField("Project setup", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.TextField("Startup address", LocalizationConfig.StartupResourcePath);
            bool startup = AssetDatabase.GetAssetPath(config) == LocalizationProjectSetup.ConfigAssetPath;
            EditorGUILayout.HelpBox(startup
                ? "This is the startup config. Enable Auto Initialize below to start localization before the first scene."
                : "Explicit config references work at any path. Automatic startup requires the fixed project path shown below.", MessageType.Info);
            EditorGUILayout.SelectableLabel(LocalizationProjectSetup.ConfigAssetPath, EditorStyles.wordWrappedLabel, GUILayout.Height(34));
            if (!startup && GUILayout.Button("Use for automatic startup"))
                Run(() => { LocalizationProjectSetup.UseForAutomaticStartup(config); return "Config moved with its GUID preserved. Automatic startup enabled."; });
            _languages = EditorGUILayout.TextField("Language codes", _languages);
            if (GUILayout.Button("Add languages and missing files"))
                Run(() =>
                {
                    var result = LocalizationProjectSetup.AddLanguages(config, Array.ConvertAll(_languages.Split(','), c => c.Trim()));
                    return "Created " + result.CreatedPaths.Count + " files. Existing translations were preserved. Set default/fallback languages below.";
                });
            if (!string.IsNullOrEmpty(_message)) EditorGUILayout.HelpBox(_message, _error ? MessageType.Error : MessageType.Info);
            EditorGUILayout.Space();
            DrawDefaultInspector();
            foreach (var issue in config.GetValidationErrors()) EditorGUILayout.HelpBox(issue, MessageType.Error);
        }

        private void Run(Func<string> action)
        {
            try { _message = action(); _error = false; }
            catch (Exception error) { _message = error.Message; _error = true; }
        }
    }
}
