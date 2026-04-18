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
                        "Initialize localization automatically before scene load. Config must be in a Resources folder."),
                    _config.autoInitialize);

                if (autoInit && !configInResources)
                {
                    EditorGUILayout.HelpBox(
                        "Config is NOT in a Resources folder. Auto-initialize will not work.\n" +
                        "Move the config asset into any Resources/ folder, or use the button below.",
                        MessageType.Warning);

                    if (GUILayout.Button("Move config to Resources"))
                    {
                        MoveConfigToResources();
                    }
                }
                else if (autoInit && configInResources)
                {
                    EditorGUILayout.HelpBox(
                        "Config found in Resources. Auto-initialization will work.",
                        MessageType.Info);
                }

                bool autoDetect = false;

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
                    _config.keyConversionMode = newMode;
                    _config.enableLogging = newLogging;
                    _config.enableLoggingInBuild = newLoggingBuild;
                    EditorUtility.SetDirty(_config);
                }

                EditorGUILayout.Space(12);

                // ── Game View Dropdown ──
                EditorGUILayout.LabelField("Game View language dropdown", EditorStyles.boldLabel);

                bool dropdownEnabled = EditorPrefs.GetBool(
                    GameViewLanguageDropdown.EnabledPrefKey, true);

                bool newEnabled = EditorGUILayout.Toggle("Show in Play Mode", dropdownEnabled);

                if (newEnabled != dropdownEnabled)
                    EditorPrefs.SetBool(GameViewLanguageDropdown.EnabledPrefKey, newEnabled);

                var currentPos = (GameViewLanguageDropdown.Position)EditorPrefs.GetInt(
                    GameViewLanguageDropdown.PositionPrefKey,
                    (int)GameViewLanguageDropdown.Position.Right);

                var newPos = (GameViewLanguageDropdown.Position)EditorGUILayout.EnumPopup(
                    "Position", currentPos);

                if (newPos != currentPos)
                    EditorPrefs.SetInt(GameViewLanguageDropdown.PositionPrefKey, (int)newPos);

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
            return !string.IsNullOrEmpty(path) && path.Contains("/Resources/");
        }

        private void MoveConfigToResources()
        {
            string currentPath = AssetDatabase.GetAssetPath(_config);

            if (string.IsNullOrEmpty(currentPath))
                return;

            // Find or create a Resources folder near the config
            string dir = System.IO.Path.GetDirectoryName(currentPath);
            string resourcesDir = System.IO.Path.Combine(dir, "Resources");

            if (!System.IO.Directory.Exists(resourcesDir))
                System.IO.Directory.CreateDirectory(resourcesDir);

            string fileName = System.IO.Path.GetFileName(currentPath);
            string newPath = System.IO.Path.Combine(resourcesDir, fileName);
            newPath = AssetDatabase.GenerateUniqueAssetPath(newPath);

            AssetDatabase.MoveAsset(currentPath, newPath);
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog("Moved",
                $"Config moved to:\n{newPath}", "OK");
        }
    }
}