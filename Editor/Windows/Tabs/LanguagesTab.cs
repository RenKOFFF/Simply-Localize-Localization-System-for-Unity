using System.IO;
using System.Linq;
using SimplyLocalize.Editor.Data;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace SimplyLocalize.Editor.Windows.Tabs
{
    public class LanguagesTab : IEditorTab
    {
        private readonly EditorLocalizationData _data;
        private readonly LocalizationConfig _config;
        private readonly LocalizationEditorWindow _window;

        public LanguagesTab(EditorLocalizationData data, LocalizationConfig config,
            LocalizationEditorWindow window)
        {
            _data = data;
            _config = config;
            _window = window;
        }

        public void Build(VisualElement container)
        {
            var root = new VisualElement();
            root.style.paddingTop = 12;
            root.style.paddingLeft = 16;
            root.style.paddingRight = 16;

            var headerRow = new VisualElement();
            headerRow.style.flexDirection = FlexDirection.Row;
            headerRow.style.justifyContent = Justify.SpaceBetween;
            headerRow.style.marginBottom = 12;

            var title = new Label("Languages");
            title.style.fontSize = 14;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            headerRow.Add(title);

            var btnGroup = new VisualElement();
            btnGroup.style.flexDirection = FlexDirection.Row;

            var addExistingBtn = new Button(OnAddExistingProfileClicked);
            addExistingBtn.text = "+ Add existing profile";
            addExistingBtn.style.fontSize = 12;
            addExistingBtn.style.marginRight = 4;
            btnGroup.Add(addExistingBtn);

            var addBtn = new Button(OnAddLanguageClicked);
            addBtn.text = "+ Create new language";
            addBtn.style.fontSize = 12;
            btnGroup.Add(addBtn);

            headerRow.Add(btnGroup);

            root.Add(headerRow);

            if (_config != null)
            {
                var configSection = new VisualElement();
                configSection.style.marginBottom = 16;
                configSection.style.paddingBottom = 12;
                configSection.style.borderBottomWidth = 1;
                configSection.style.borderBottomColor = new Color(0, 0, 0, 0.1f);

                var defaultField = new UnityEditor.UIElements.ObjectField("Default language");
                defaultField.objectType = typeof(LanguageProfile);
                defaultField.value = _config.defaultLanguage;
                defaultField.RegisterValueChangedCallback(evt =>
                {
                    Undo.RecordObject(_config, "Change default language");
                    _config.defaultLanguage = evt.newValue as LanguageProfile;
                    EditorUtility.SetDirty(_config);
                });
                configSection.Add(defaultField);

                var fallbackField = new UnityEditor.UIElements.ObjectField("Fallback language");
                fallbackField.objectType = typeof(LanguageProfile);
                fallbackField.value = _config.fallbackLanguage;
                fallbackField.RegisterValueChangedCallback(evt =>
                {
                    Undo.RecordObject(_config, "Change fallback language");
                    _config.fallbackLanguage = evt.newValue as LanguageProfile;
                    EditorUtility.SetDirty(_config);
                });
                configSection.Add(fallbackField);

                root.Add(configSection);

                foreach (var profile in _config.languages)
                {
                    if (profile == null) continue;
                    root.Add(BuildLanguageCard(profile));
                }
            }

            container.Add(root);
        }

        private VisualElement BuildLanguageCard(LanguageProfile profile)
        {
            var card = new VisualElement();
            card.style.borderBottomWidth = 1;
            card.style.borderBottomColor = new Color(0, 0, 0, 0.08f);
            card.style.paddingTop = 8;
            card.style.paddingBottom = 8;
            card.style.flexDirection = FlexDirection.Row;
            card.style.alignItems = Align.Center;

            var info = new VisualElement();
            info.style.flexGrow = 1;

            var nameRow = new VisualElement();
            nameRow.style.flexDirection = FlexDirection.Row;
            nameRow.style.alignItems = Align.Center;

            var nameLabel = new Label(profile.displayName);
            nameLabel.style.fontSize = 13;
            nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            nameRow.Add(nameLabel);

            var codeLabel = new Label($"  ({profile.Code})");
            codeLabel.style.fontSize = 11;
            codeLabel.style.color = new Color(0.5f, 0.5f, 0.5f);
            nameRow.Add(codeLabel);

            var sysLangLabel = new Label($"  SystemLanguage.{profile.systemLanguage}");
            sysLangLabel.style.fontSize = 10;
            sysLangLabel.style.color = new Color(0.6f, 0.6f, 0.6f);
            nameRow.Add(sysLangLabel);

            info.Add(nameRow);

            var flagsRow = new VisualElement();
            flagsRow.style.flexDirection = FlexDirection.Row;
            flagsRow.style.marginTop = 2;

            //TODO этого тут быть не должно, также метод наверное должен не принимать булки вот так явно
            flagsRow.Add(MakeBadge("Text", false/*profile.hasText*/));
            flagsRow.Add(MakeBadge("Sprites", false/*profile.hasSprites*/));
            flagsRow.Add(MakeBadge("Audio", false/*profile.hasAudio*/));

            if (profile.primaryFont != null)
            {
                var fontBadge = new Label($"Font: {profile.primaryFont.name}");
                fontBadge.style.fontSize = 10;
                fontBadge.style.color = new Color(0.4f, 0.4f, 0.4f);
                fontBadge.style.marginLeft = 8;
                flagsRow.Add(fontBadge);
            }

            info.Add(flagsRow);
            card.Add(info);

            var selectBtn = new Button(() =>
            {
                Selection.activeObject = profile;
                EditorGUIUtility.PingObject(profile);
            });
            selectBtn.text = "Select";
            selectBtn.style.fontSize = 11;
            selectBtn.style.marginRight = 4;
            card.Add(selectBtn);

            var removeBtn = new Button(() => OnRemoveLanguage(profile));
            removeBtn.text = "Remove";
            removeBtn.style.fontSize = 11;
            removeBtn.style.color = new Color(0.8f, 0.3f, 0.3f);
            card.Add(removeBtn);

            return card;
        }

        private Label MakeBadge(string text, bool enabled)
        {
            var badge = new Label(text);
            badge.style.fontSize = 10;
            badge.style.paddingLeft = 6;
            badge.style.paddingRight = 6;
            badge.style.paddingTop = 1;
            badge.style.paddingBottom = 1;
            badge.style.marginRight = 4;
            badge.style.borderTopLeftRadius = 4;
            badge.style.borderTopRightRadius = 4;
            badge.style.borderBottomLeftRadius = 4;
            badge.style.borderBottomRightRadius = 4;

            if (enabled)
            {
                badge.style.backgroundColor = new Color(0.2f, 0.7f, 0.3f, 0.15f);
                badge.style.color = new Color(0.2f, 0.6f, 0.2f);
            }
            else
            {
                badge.style.backgroundColor = new Color(0, 0, 0, 0.05f);
                badge.style.color = new Color(0.6f, 0.6f, 0.6f);
            }

            return badge;
        }

        private void OnAddLanguageClicked()
        {
            var popup = ScriptableObject.CreateInstance<AddLanguagePopup>();
            popup.Init(_config, _data, () => _window.FullRefresh());
            popup.ShowUtility();
            popup.position = new Rect(
                Screen.width / 2f - 150, Screen.height / 2f - 100, 300, 180);
        }

        private void OnAddExistingProfileClicked()
        {
            // Find all LanguageProfile assets in the project that aren't already in the config
            var existingCodes = new System.Collections.Generic.HashSet<string>(
                _config.languages.Where(p => p != null).Select(p => p.Code));

            var guids = AssetDatabase.FindAssets("t:LanguageProfile");
            var available = new System.Collections.Generic.List<LanguageProfile>();

            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var profile = AssetDatabase.LoadAssetAtPath<LanguageProfile>(path);

                if (profile != null && !existingCodes.Contains(profile.Code))
                    available.Add(profile);
            }

            if (available.Count == 0)
            {
                EditorUtility.DisplayDialog("No profiles available",
                    "All existing LanguageProfile assets are already in the config.\n" +
                    "Use 'Create new language' to make a new one.", "OK");
                return;
            }

            // Show a picker menu
            var menu = new GenericMenu();

            foreach (var profile in available)
            {
                var p = profile;
                string label = $"{p.displayName} ({p.Code})";

                menu.AddItem(new GUIContent(label), false, () =>
                {
                    Undo.RecordObject(_config, "Add existing language");
                    _config.languages.Add(p);
                    EditorUtility.SetDirty(_config);

                    // Ensure the language folder + JSON files exist
                    if (!string.IsNullOrEmpty(_data.BasePath))
                    {
                        string textDir = System.IO.Path.Combine(_data.BasePath, p.Code, "text");

                        if (!System.IO.Directory.Exists(textDir))
                            System.IO.Directory.CreateDirectory(textDir);

                        // Create JSON for each existing source file (if missing)
                        foreach (var fileName in _data.SourceFiles)
                        {
                            string jsonPath = System.IO.Path.Combine(textDir, fileName + ".json");

                            if (!System.IO.File.Exists(jsonPath))
                                System.IO.File.WriteAllText(jsonPath, "{\n  \"translations\": {}\n}");
                        }

                        if (_data.SourceFiles.Count == 0)
                        {
                            string jsonPath = System.IO.Path.Combine(textDir, "global.json");
                            if (!System.IO.File.Exists(jsonPath))
                                System.IO.File.WriteAllText(jsonPath, "{\n  \"translations\": {}\n}");
                        }
                    }

                    AssetDatabase.Refresh();
                    _window.FullRefresh();
                });
            }

            menu.ShowAsContext();
        }

        private void OnRemoveLanguage(LanguageProfile profile)
        {
            // Three-choice dialog: Remove from config only / Delete everything / Cancel
            int choice = EditorUtility.DisplayDialogComplex(
                "Remove language",
                $"Remove '{profile.displayName}' ({profile.Code})?\n\n" +
                "• 'Remove from config' keeps the profile asset and data files on disk.\n" +
                "• 'Delete everything' removes the profile asset AND the language data folder.",
                "Delete everything",
                "Cancel",
                "Remove from config only");

            if (choice == 1) // Cancel
                return;

            Undo.RecordObject(_config, "Remove language");
            _config.languages.Remove(profile);

            // If this was default or fallback, clear the reference
            if (_config.defaultLanguage == profile)
                _config.defaultLanguage = null;
            if (_config.fallbackLanguage == profile)
                _config.fallbackLanguage = null;

            EditorUtility.SetDirty(_config);

            if (choice == 0) // Delete everything
            {
                // Delete data folder
                if (!string.IsNullOrEmpty(_data.BasePath))
                {
                    string langFolder = Path.Combine(_data.BasePath, profile.Code);

                    if (Directory.Exists(langFolder))
                    {
                        Directory.Delete(langFolder, true);

                        // Delete .meta file too
                        string metaPath = langFolder + ".meta";
                        if (File.Exists(metaPath))
                            File.Delete(metaPath);
                    }
                }

                // Delete profile asset
                string profilePath = AssetDatabase.GetAssetPath(profile);
                if (!string.IsNullOrEmpty(profilePath))
                    AssetDatabase.DeleteAsset(profilePath);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            _window.FullRefresh();
        }
    }

    public class AddLanguagePopup : EditorWindow
    {
        private LocalizationConfig _config;
        private EditorLocalizationData _data;
        private System.Action _onCreated;

        private string _code = "";
        private string _displayName = "";
        private SystemLanguage _systemLanguage = SystemLanguage.Unknown;

        public void Init(LocalizationConfig config, EditorLocalizationData data, System.Action onCreated)
        {
            _config = config;
            _data = data;
            _onCreated = onCreated;
            titleContent = new GUIContent("Add language");
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(4);
            _code = EditorGUILayout.TextField("Language code", _code);
            _displayName = EditorGUILayout.TextField("Display name", _displayName);
            _systemLanguage = (SystemLanguage)EditorGUILayout.EnumPopup("System language", _systemLanguage);

            EditorGUILayout.Space(4);

            // Show what will be created
            if (!string.IsNullOrEmpty(_code))
            {
                EditorGUILayout.HelpBox(
                    $"Will create:\n" +
                    $"• Profile_{_code}.asset\n" +
                    $"• {_code}/text/ folder with JSON files for all existing source files",
                    MessageType.Info);
            }

            EditorGUILayout.Space(4);

            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(_code) || string.IsNullOrEmpty(_displayName)))
            {
                if (GUILayout.Button("Create"))
                {
                    CreateLanguage();
                    Close();
                }
            }
        }

        private void CreateLanguage()
        {
            // Create profile asset
            var profile = ScriptableObject.CreateInstance<LanguageProfile>();
            profile.languageCode = _code;
            profile.displayName = _displayName;
            profile.systemLanguage = _systemLanguage;
            
            //TODO этого тут быть не должно
            // profile.hasText = true;

            string configPath = AssetDatabase.GetAssetPath(_config);
            string configDir = Path.GetDirectoryName(configPath);
            string profilePath = Path.Combine(configDir ?? "Assets", $"Profile_{_code}.asset");
            profilePath = AssetDatabase.GenerateUniqueAssetPath(profilePath);

            AssetDatabase.CreateAsset(profile, profilePath);

            // Add to config
            Undo.RecordObject(_config, "Add language");
            _config.languages.Add(profile);
            EditorUtility.SetDirty(_config);

            // Create text folder and JSON files for ALL existing source files
            if (!string.IsNullOrEmpty(_data.BasePath))
            {
                string textDir = Path.Combine(_data.BasePath, _code, "text");
                Directory.CreateDirectory(textDir);

                var existingFiles = _data.SourceFiles;

                if (existingFiles.Count > 0)
                {
                    // Create empty JSON for every existing source file
                    foreach (var fileName in existingFiles)
                    {
                        string jsonPath = Path.Combine(textDir, fileName + ".json");

                        if (!File.Exists(jsonPath))
                            File.WriteAllText(jsonPath, "{\n  \"translations\": {}\n}");
                    }
                }
                else
                {
                    // No existing files — create global.json as default
                    string jsonPath = Path.Combine(textDir, "global.json");
                    File.WriteAllText(jsonPath, "{\n  \"translations\": {}\n}");
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            _onCreated?.Invoke();
        }
    }
}
