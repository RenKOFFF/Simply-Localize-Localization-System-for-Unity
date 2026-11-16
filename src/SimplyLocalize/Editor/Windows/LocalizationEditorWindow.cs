using SimplyLocalize.Editor.Data;
using SimplyLocalize.Editor.Windows.Tabs;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace SimplyLocalize.Editor.Windows
{
    public class LocalizationEditorWindow : EditorWindow
    {
        [SerializeField] private LocalizationConfig _config;
        private EditorLocalizationData _data;

        private VisualElement _tabContent;
        private VisualElement _tabBar;
        [SerializeField] private int _activeTabIndex;

        private IEditorTab[] _tabs;
        private string[] _tabNames;

        [MenuItem("Window/Simply Localize/Localization Editor", priority = 100)]
        public static void Open()
        {
            var window = GetWindow<LocalizationEditorWindow>();
            window.titleContent = new GUIContent("Simply Localize");
            window.minSize = new Vector2(700, 400);
        }

        public static void OpenConfig(LocalizationConfig config)
        {
            Open();
            var window = GetWindow<LocalizationEditorWindow>();
            window.AssignConfig(config);
        }

        private void AssignConfig(LocalizationConfig config)
        {
            _data?.FlushPendingAssetRefresh();
            _config = config;
            _data = new EditorLocalizationData();
            if (_config != null) _data.Load(_config);
            BuildUI();
        }

        private void OnEnable()
        {
            FindConfig();
            _data = new EditorLocalizationData();

            if (_config != null)
                _data.Load(_config);

            BuildUI();

            // Flush pending AssetDatabase.Refresh before entering Play Mode
            // (edits are deferred for performance — see EditorLocalizationData.MarkPendingAssetRefresh)
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private void OnDisable()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        }

        private void OnDestroy()
        {
            // Window is closing — make sure any pending edits are picked up by AssetDatabase
            _data?.FlushPendingAssetRefresh();
        }

        private void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingEditMode)
                _data?.FlushPendingAssetRefresh();
        }

        private void OnFocus()
        {
            if (_config != null && _data != null)
            {
                // Only reload from disk if there are no pending in-memory edits waiting to flush.
                // Reloading would clobber unsaved-to-AssetDatabase changes with stale cached data.
                if (!_data.HasPendingAssetRefresh)
                {
                    _data.Reload(_config);
                    RefreshActiveTab();
                }
            }
        }

        // ──────────────────────────────────────────────
        //  UI
        // ──────────────────────────────────────────────

        private void BuildUI()
        {
            rootVisualElement.Clear();
            rootVisualElement.style.flexDirection = FlexDirection.Column;
            rootVisualElement.style.flexGrow = 1;

            // Header
            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.justifyContent = Justify.SpaceBetween;
            header.style.alignItems = Align.Center;
            header.style.paddingTop = 6;
            header.style.paddingBottom = 6;
            header.style.paddingLeft = 12;
            header.style.paddingRight = 12;
            header.style.borderBottomWidth = 1;
            header.style.borderBottomColor = new Color(0, 0, 0, 0.15f);

            var title = new Label("Simply Localize");
            title.style.fontSize = 14;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.Add(title);

            var configField = new UnityEditor.UIElements.ObjectField("Config");
            configField.name = "configField";
            configField.objectType = typeof(LocalizationConfig);
            configField.allowSceneObjects = false;
            configField.value = _config;
            configField.style.width = 350;
            configField.labelElement.style.minWidth = 50;
            configField.labelElement.style.width = 50;
            configField.RegisterValueChangedCallback(evt =>
            {
                AssignConfig(evt.newValue as LocalizationConfig);
            });
            header.Add(configField);

            rootVisualElement.Add(header);

            // Tab bar
            _tabBar = new VisualElement();
            _tabBar.style.flexDirection = FlexDirection.Row;
            _tabBar.style.borderBottomWidth = 1;
            _tabBar.style.borderBottomColor = new Color(0, 0, 0, 0.15f);
            _tabBar.style.paddingLeft = 8;
            rootVisualElement.Add(_tabBar);

            // Tab content
            _tabContent = new VisualElement();
            _tabContent.style.flexGrow = 1;
            rootVisualElement.Add(_tabContent);

            RebuildTabs();
            SetActiveTab(_activeTabIndex);
        }

        private void RebuildTabs()
        {
            var tabList = new System.Collections.Generic.List<IEditorTab>
            {
                new TranslationsTab(_data, _config, this),
                new AssetsTab(_data, _config, this),
                new LanguagesTab(_data, _config, this),
                new ProfilesTab(_config),
                new CoverageTab(_data, _config),
                new AutoLocalizeTab(_data, _config, this),
                new ToolsTab(_data, _config),
                new SettingsTab(_config, this),
            };

            var nameList = new System.Collections.Generic.List<string>
            {
                "Translations", "Assets", "Languages", "Profiles",
                "Coverage", "Auto Localize", "Tools", "Settings"
            };

            // Discover custom tabs registered via [LocalizationEditorTab]
            var customTabTypes = UnityEditor.TypeCache.GetTypesWithAttribute<LocalizationEditorTabAttribute>();

            var customEntries = new System.Collections.Generic.List<(int order, string name, IEditorTab tab)>();

            foreach (var type in customTabTypes)
            {
                if (!typeof(IEditorTab).IsAssignableFrom(type)) continue;

                var attr = (LocalizationEditorTabAttribute)
                    System.Attribute.GetCustomAttribute(type, typeof(LocalizationEditorTabAttribute));

                if (attr == null) continue;

                try
                {
                    var tab = (IEditorTab)System.Activator.CreateInstance(type);
                    customEntries.Add((attr.Order, attr.TabName, tab));
                }
                catch (System.Exception e)
                {
                    UnityEngine.Debug.LogWarning(
                        $"[SimplyLocalize] Failed to create custom tab '{attr.TabName}': {e.Message}");
                }
            }

            // Sort custom tabs by order and append
            customEntries.Sort((a, b) => a.order.CompareTo(b.order));

            foreach (var (_, name, tab) in customEntries)
            {
                tabList.Add(tab);
                nameList.Add(name);
            }

            _tabs = tabList.ToArray();
            _tabNames = nameList.ToArray();

            RebuildTabBar();
        }

        private void RebuildTabBar()
        {
            _tabBar.Clear();

            for (int i = 0; i < _tabNames.Length; i++)
            {
                int index = i;
                var btn = new Button(() => SetActiveTab(index));
                btn.name = "tab" + _tabNames[i].Replace(" ", "");
                btn.SetEnabled(_config != null);
                btn.text = _tabNames[i];
                btn.style.borderBottomWidth = 2;
                btn.style.borderLeftWidth = 0;
                btn.style.borderRightWidth = 0;
                btn.style.borderTopWidth = 0;
                btn.style.borderBottomColor = i == _activeTabIndex
                    ? new Color(0.2f, 0.5f, 0.9f) : Color.clear;
                btn.style.backgroundColor = Color.clear;
                btn.style.paddingTop = 8;
                btn.style.paddingBottom = 8;
                btn.style.paddingLeft = 14;
                btn.style.paddingRight = 14;
                btn.style.marginLeft = 0;
                btn.style.marginRight = 0;
                btn.style.fontSize = 12;

                if (i == _activeTabIndex)
                    btn.style.color = new Color(0.2f, 0.5f, 0.9f);

                _tabBar.Add(btn);
            }
        }

        private void SetActiveTab(int index)
        {
            _activeTabIndex = index;
            RebuildTabBar();

            _tabContent.Clear();

            if (_config == null)
            {
                BuildSetup(_tabContent);
                return;
            }

            if (index >= 0 && index < _tabs.Length)
                _tabs[index].Build(_tabContent);
        }

        private void RefreshActiveTab()
        {
            SetActiveTab(_activeTabIndex);
        }

        private void BuildSetup(VisualElement container)
        {
            var setup = new ScrollView { name = "projectSetup" };
            setup.style.paddingTop = 24;
            setup.style.paddingLeft = 24;
            setup.style.paddingRight = 24;
            var title = new Label("Set up localization");
            title.style.fontSize = 20;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom = 12;
            setup.Add(title);
            setup.Add(new HelpBox("Create your config, language profiles and translation files here. You can edit translations in this window or in an external text editor.", HelpBoxMessageType.Info));
            var codes = new TextField("Language codes") { name = "setupLanguages", value = "en,ru", tooltip = "Comma-separated codes, for example en, ru, pl." };
            codes.style.marginTop = 16;
            var defaultCode = new TextField("Default language") { name = "setupDefault", value = "en" };
            var fallbackCode = new TextField("Fallback language") { name = "setupFallback", value = "en", tooltip = "Used when a translation is missing. Leave empty to disable global fallback." };
            setup.Add(codes);
            setup.Add(defaultCode);
            setup.Add(fallbackCode);
            var path = new Label("Config: " + LocalizationProjectSetup.ConfigAssetPath);
            path.style.whiteSpace = WhiteSpace.Normal;
            path.style.marginTop = 16;
            setup.Add(path);
            setup.Add(new Label("Automatic startup will be enabled. Existing translation files are preserved.") { style = { whiteSpace = WhiteSpace.Normal } });
            var message = new HelpBox("", HelpBoxMessageType.Error) { name = "setupMessage" };
            message.style.display = DisplayStyle.None;
            var create = new Button(() =>
            {
                try
                {
                    string fallback = fallbackCode.value.Trim();
                    var result = LocalizationProjectSetup.Configure(
                        System.Array.ConvertAll(codes.value.Split(','), code => code.Trim()),
                        defaultCode.value.Trim(), fallback.Length == 0 ? null : fallback);
                    _activeTabIndex = 2;
                    AssignConfig(result.Config);
                }
                catch (System.Exception error)
                {
                    message.text = error.Message;
                    message.style.display = DisplayStyle.Flex;
                }
            }) { name = "createConfig", text = "Create config and languages" };
            create.style.marginTop = 16;
            create.style.height = 30;
            setup.Add(create);
            setup.Add(message);
            var existing = new HelpBox("Already have a config? Select it in the Config field above. Open Settings to use it for automatic startup.", HelpBoxMessageType.Info);
            existing.style.marginTop = 20;
            setup.Add(existing);
            container.Add(setup);
        }

        // ──────────────────────────────────────────────
        //  Public for tabs
        // ──────────────────────────────────────────────

        public void FullRefresh()
        {
            if (_config != null)
                _data.Reload(_config);

            RebuildTabs();
            SetActiveTab(_activeTabIndex);
        }

        public void RefreshCurrentTab() => RefreshActiveTab();

        internal void EditProfile(LanguageProfile profile)
        {
            SetActiveTab(3);
            var selector = _tabContent.Q<PopupField<string>>("profileSelector");
            if (selector != null) selector.index = _config.languages.IndexOf(profile);
        }

        // ──────────────────────────────────────────────
        //  Helpers
        // ──────────────────────────────────────────────

        private void FindConfig()
        {
            if (_config != null) return;

            _config = AssetDatabase.LoadAssetAtPath<LocalizationConfig>(LocalizationProjectSetup.ConfigAssetPath);
            if (_config != null) return;

            // Search by type name. If the user's project has a different class named
            // "LocalizationConfig" in another namespace, LoadAssetAtPath<LocalizationConfig>
            // will return null for it — we skip and try the next match.
            var guids = AssetDatabase.FindAssets("t:LocalizationConfig");

            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var candidate = AssetDatabase.LoadAssetAtPath<LocalizationConfig>(path);

                if (candidate != null)
                {
                    _config = candidate;
                    return;
                }
            }
        }
    }
}
