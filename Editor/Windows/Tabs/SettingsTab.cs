using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace SimplyLocalize.Editor.Windows.Tabs
{
    public class SettingsTab : IEditorTab
    {
        private readonly LocalizationConfig _config;
        private readonly LocalizationEditorWindow _window;

        public SettingsTab(LocalizationConfig config, LocalizationEditorWindow window)
        {
            _config = config;
            _window = window;
        }

        public void Build(VisualElement container)
        {
            var root = new VisualElement();
            root.style.paddingTop = 12;
            root.style.paddingLeft = 16;
            root.style.paddingRight = 16;

            var title = new Label("Settings");
            title.style.fontSize = 14;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom = 12;
            root.Add(title);

            if (_config == null)
            {
                root.Add(new Label("No config assigned."));
                container.Add(root);
                return;
            }

            var imguiContainer = new IMGUIContainer(() =>
            {
                // Config section
                EditorGUILayout.LabelField("Localization config", EditorStyles.boldLabel);

                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.ObjectField("Config asset", _config,
                        typeof(LocalizationConfig), false);
                }

                EditorGUI.BeginChangeCheck();

                var newPath = EditorGUILayout.TextField("Resources base path",
                    _config.resourcesBasePath);
                var newMode = (KeyConversionMode)EditorGUILayout.EnumPopup(
                    "Key conversion mode", _config.keyConversionMode);
                var newLogging = EditorGUILayout.Toggle("Enable logging",
                    _config.enableLogging);

                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(_config, "Change localization settings");
                    _config.resourcesBasePath = newPath;
                    _config.keyConversionMode = newMode;
                    _config.enableLogging = newLogging;
                    EditorUtility.SetDirty(_config);
                }

                EditorGUILayout.Space(16);

                // Game View Dropdown section
                EditorGUILayout.LabelField("Game View language dropdown", EditorStyles.boldLabel);

                bool dropdownEnabled = EditorPrefs.GetBool(
                    GameViewLanguageDropdown.EnabledPrefKey, true);

                bool newEnabled = EditorGUILayout.Toggle("Show in Play Mode", dropdownEnabled);

                if (newEnabled != dropdownEnabled)
                    EditorPrefs.SetBool(GameViewLanguageDropdown.EnabledPrefKey, newEnabled);

                var currentPos = (GameViewLanguageDropdown.Position)EditorPrefs.GetInt(
                    GameViewLanguageDropdown.PositionPrefKey,
                    (int)GameViewLanguageDropdown.Position.TopRight);

                var newPos = (GameViewLanguageDropdown.Position)EditorGUILayout.EnumPopup(
                    "Position", currentPos);

                if (newPos != currentPos)
                    EditorPrefs.SetInt(GameViewLanguageDropdown.PositionPrefKey, (int)newPos);

                EditorGUILayout.Space(16);

                // Actions
                EditorGUILayout.LabelField("Actions", EditorStyles.boldLabel);

                if (GUILayout.Button("Open config in Inspector"))
                    Selection.activeObject = _config;

                if (GUILayout.Button("Refresh editor data"))
                    _window.FullRefresh();
            });

            root.Add(imguiContainer);
            container.Add(root);
        }
    }
}
