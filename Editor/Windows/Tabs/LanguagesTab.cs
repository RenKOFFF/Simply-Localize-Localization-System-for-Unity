using System.IO;
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

            // Header with add button
            var headerRow = new VisualElement();
            headerRow.style.flexDirection = FlexDirection.Row;
            headerRow.style.justifyContent = Justify.SpaceBetween;
            headerRow.style.marginBottom = 12;

            var title = new Label("Languages");
            title.style.fontSize = 14;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            headerRow.Add(title);

            var addBtn = new Button(OnAddLanguageClicked);
            addBtn.text = "+ Add language";
            addBtn.style.fontSize = 12;
            headerRow.Add(addBtn);

            root.Add(headerRow);

            // Default / Fallback
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
            }

            // Language list
            if (_config != null)
            {
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

            // Info
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

            // Asset flags
            var flagsRow = new VisualElement();
            flagsRow.style.flexDirection = FlexDirection.Row;
            flagsRow.style.marginTop = 2;

            flagsRow.Add(MakeBadge("Text", profile.hasText));
            flagsRow.Add(MakeBadge("Sprites", profile.hasSprites));
            flagsRow.Add(MakeBadge("Audio", profile.hasAudio));

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

            // Actions
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
                Screen.width / 2f - 150, Screen.height / 2f - 80, 300, 160);
        }

        private void OnRemoveLanguage(LanguageProfile profile)
        {
            if (!EditorUtility.DisplayDialog("Remove language",
                $"Remove '{profile.displayName}' ({profile.Code}) from the config?\n" +
                "The profile asset and translation files will NOT be deleted.",
                "Remove", "Cancel"))
                return;

            Undo.RecordObject(_config, "Remove language");
            _config.languages.Remove(profile);
            EditorUtility.SetDirty(_config);
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

            EditorGUILayout.Space(8);

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
            profile.hasText = true;

            string configPath = AssetDatabase.GetAssetPath(_config);
            string configDir = Path.GetDirectoryName(configPath);
            string profilePath = Path.Combine(configDir ?? "Assets", $"Profile_{_code}.asset");
            profilePath = AssetDatabase.GenerateUniqueAssetPath(profilePath);

            AssetDatabase.CreateAsset(profile, profilePath);

            // Add to config
            Undo.RecordObject(_config, "Add language");
            _config.languages.Add(profile);
            EditorUtility.SetDirty(_config);

            // Create text folder with empty JSON
            if (!string.IsNullOrEmpty(_data.BasePath))
            {
                string textDir = Path.Combine(_data.BasePath, _code, "text");
                Directory.CreateDirectory(textDir);

                string jsonPath = Path.Combine(textDir, _code + ".json");
                File.WriteAllText(jsonPath, "{\n  \"translations\": {}\n}");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            _onCreated?.Invoke();
        }
    }
}
