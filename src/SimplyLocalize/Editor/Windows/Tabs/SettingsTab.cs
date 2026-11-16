using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace SimplyLocalize.Editor.Windows.Tabs
{
    public class SettingsTab : IEditorTab
    {
        private readonly LocalizationConfig _config;
        private readonly LocalizationEditorWindow _window;
        private string _startupMessage;
        private bool _startupError;

        public SettingsTab(LocalizationConfig config, LocalizationEditorWindow window)
        {
            _config = config;
            _window = window;
        }

        public void Build(VisualElement container)
        {
            var root = new ScrollView();
            root.style.paddingTop = 12;
            root.style.paddingLeft = 16;
            root.style.paddingRight = 16;

            if (_config == null)
            {
                root.Add(new Label("No config assigned."));
                container.Add(root);
                return;
            }

            var startupTitle = new Label("Automatic startup");
            startupTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            startupTitle.style.fontSize = 14;
            root.Add(startupTitle);
            bool atStartupPath = IsConfigInResources();
            root.Add(new Label("Current config: " + AssetDatabase.GetAssetPath(_config)) { style = { whiteSpace = WhiteSpace.Normal, marginTop = 8 } });
            root.Add(new Label("Startup path: " + LocalizationProjectSetup.ConfigAssetPath) { style = { whiteSpace = WhiteSpace.Normal } });
            root.Add(new HelpBox(atStartupPath
                ? "This config is at the startup path. Auto-initialize controls whether it starts before the first scene."
                : "To use this config automatically, move it to the startup path. Language profiles and translation files stay where they are; existing references are preserved.",
                atStartupPath ? HelpBoxMessageType.Info : HelpBoxMessageType.Warning));
            if (!atStartupPath)
                root.Add(new Button(() =>
                {
                    try
                    {
                        LocalizationProjectSetup.UseForAutomaticStartup(_config);
                        _startupMessage = "Config moved. References preserved. Automatic startup enabled.";
                        _startupError = false;
                    }
                    catch (System.Exception error) { _startupMessage = error.Message; _startupError = true; }
                    _window.RefreshCurrentTab();
                }) { name = "useForAutomaticStartup", text = "Move config and enable automatic startup" });
            if (!string.IsNullOrEmpty(_startupMessage))
                root.Add(new HelpBox(_startupMessage, _startupError ? HelpBoxMessageType.Error : HelpBoxMessageType.Info) { name = "startupMessage" });

            var imgui = new IMGUIContainer(() =>
            {
                // ── Initialization ──
                EditorGUILayout.LabelField("Initialization", EditorStyles.boldLabel);

                EditorGUI.BeginChangeCheck();

                bool autoInit = EditorGUILayout.Toggle(
                    new GUIContent("Auto-initialize",
                        "Initialize localization automatically before scene load. Config must use the fixed startup path."),
                    _config.autoInitialize);

                bool autoDetect = _config.autoDetectLanguage;

                if (autoInit)
                {
                    EditorGUI.indentLevel++;

                    autoDetect = EditorGUILayout.Toggle(
                        new GUIContent("Auto-detect language",
                            "Try to match device SystemLanguage to a language profile on startup"),
                        _config.autoDetectLanguage);

                    if (!autoDetect && _config.defaultLanguage != null)
                    {
                        EditorGUILayout.LabelField(
                            $"  Will start with: {_config.defaultLanguage.displayName} ({_config.defaultLanguage.Code})",
                            EditorStyles.miniLabel);
                    }

                    EditorGUI.indentLevel--;
                }

                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(_config, "Change initialization settings");
                    _config.autoInitialize = autoInit;
                    _config.autoDetectLanguage = autoDetect;
                    EditorUtility.SetDirty(_config);
                }

                EditorGUILayout.Space(12);

                // ── Data ──
                EditorGUILayout.LabelField("Data", EditorStyles.boldLabel);

                EditorGUI.BeginChangeCheck();

                var newPath = EditorGUILayout.TextField("Resources base path",
                    _config.resourcesBasePath);
                var newLoadText = EditorGUILayout.Toggle(new GUIContent("Load text translations", "Disable for an audio/image-only context; missing text files will not be requested."), _config.loadTextData);
                var newMode = (KeyConversionMode)EditorGUILayout.EnumPopup(
                    "Key conversion mode", _config.keyConversionMode);
                var newLogging = EditorGUILayout.Toggle("Enable logging",
                    _config.enableLogging);
                var newLoggingBuild = EditorGUILayout.Toggle("Enable logging in build",
                    _config.enableLoggingInBuild);

                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(_config, "Change data settings");
                    _config.resourcesBasePath = newPath;
                    _config.loadTextData = newLoadText;
                    _config.keyConversionMode = newMode;
                    _config.enableLogging = newLogging;
                    _config.enableLoggingInBuild = newLoggingBuild;
                    EditorUtility.SetDirty(_config);
                }

                EditorGUILayout.Space(12);

                // ── Play Mode Language Dropdown ──
                EditorGUILayout.LabelField("Game View / Simulator language dropdown", EditorStyles.boldLabel);

                bool dropdownEnabled = EditorPrefs.GetBool(
                    GameViewLanguageDropdown.EnabledPrefKey, true);

                bool newEnabled = EditorGUILayout.Toggle("Show in Play Mode", dropdownEnabled);

                if (newEnabled != dropdownEnabled)
                    EditorPrefs.SetBool(GameViewLanguageDropdown.EnabledPrefKey, newEnabled);

                var currentPos = (GameViewLanguageDropdown.Position)EditorPrefs.GetInt(
                    GameViewLanguageDropdown.PositionPrefKey,
                    (int)GameViewLanguageDropdown.Position.Right);

                var newPos = (GameViewLanguageDropdown.Position)EditorGUILayout.EnumPopup(
                    "Game View position", currentPos);

                if (newPos != currentPos)
                    EditorPrefs.SetInt(GameViewLanguageDropdown.PositionPrefKey, (int)newPos);

                EditorGUILayout.HelpBox(
                    "Switch languages in Game View or in Device Simulator's Control Panel under Simply Localize.",
                    MessageType.Info);

                EditorGUILayout.Space(12);

                // ── Actions ──
                EditorGUILayout.LabelField("Actions", EditorStyles.boldLabel);

                if (GUILayout.Button("Open config in Inspector"))
                    Selection.activeObject = _config;

                if (GUILayout.Button("Refresh editor data"))
                    _window.FullRefresh();
            });

            root.Add(imgui);
            container.Add(root);
        }

        private bool IsConfigInResources()
        {
            string path = AssetDatabase.GetAssetPath(_config);
            return !string.IsNullOrEmpty(path) && path == LocalizationProjectSetup.ConfigAssetPath;
        }

    }
}
