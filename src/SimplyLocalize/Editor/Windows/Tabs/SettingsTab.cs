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

            if (_config == null)
            {
                root.Add(new Label("No config assigned."));
                container.Add(root);
                return;
            }

            var imgui = new IMGUIContainer(() =>
            {
                // ── Initialization ──
                EditorGUILayout.LabelField("Initialization", EditorStyles.boldLabel);

                bool configInResources = IsConfigInResources();

                EditorGUI.BeginChangeCheck();

                bool autoInit = EditorGUILayout.Toggle(
                    new GUIContent("Auto-initialize",
                        "Initialize localization automatically before scene load. Config must use the fixed startup path."),
                    _config.autoInitialize);

                if (autoInit && !configInResources)
                {
                    EditorGUILayout.HelpBox(
                        "Config is not at the startup path. Auto-initialize will not use this config.\n" +
                        "Use the button below to move it with its GUID preserved.",
                        MessageType.Warning);

                    if (GUILayout.Button("Use for automatic startup"))
                    {
                        MoveConfigToResources();
                    }
                }
                else if (autoInit && configInResources)
                {
                    EditorGUILayout.HelpBox(
                        "Config is at the startup path. Check languages and default/fallback settings below.",
                        MessageType.Info);
                }

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

        private void MoveConfigToResources()
        {
            try { LocalizationProjectSetup.UseForAutomaticStartup(_config); }
            catch (System.Exception error) { EditorUtility.DisplayDialog("Setup failed", error.Message, "OK"); }
        }
    }
}
