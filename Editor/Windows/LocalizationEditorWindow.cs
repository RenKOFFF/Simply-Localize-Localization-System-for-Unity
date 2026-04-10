using SimplyLocalize.Editor.Data;
using SimplyLocalize.Editor.Windows.Tabs;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace SimplyLocalize.Editor.Windows
{
    public class LocalizationEditorWindow : EditorWindow
    {
        private LocalizationConfig _config;
        private EditorLocalizationData _data;

        private VisualElement _tabContent;
        private VisualElement _tabBar;
        private int _activeTabIndex;

        private IEditorTab[] _tabs;
        private string[] _tabNames;

        [MenuItem("Window/SimplyLocalize/Localization Editor")]
        public static void Open()
        {
            var window = GetWindow<LocalizationEditorWindow>();
            window.titleContent = new GUIContent("SimplyLocalize");
            window.minSize = new Vector2(700, 400);
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

            var title = new Label("SimplyLocalize");
            title.style.fontSize = 14;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.Add(title);

            var configField = new UnityEditor.UIElements.ObjectField("Config");
            configField.objectType = typeof(LocalizationConfig);
            configField.value = _config;
            configField.style.width = 250;
            configField.RegisterValueChangedCallback(evt =>
            {
                _config = evt.newValue as LocalizationConfig;
                if (_config != null) _data.Load(_config);
                RebuildTabs();
                RefreshActiveTab();
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
            SetActiveTab(0);
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
                var msg = new Label("No LocalizationConfig assigned. Create one via\nCreate → SimplyLocalize → Localization Config\nand assign it above.");
                msg.style.paddingTop = 40;
                msg.style.paddingLeft = 20;
                msg.style.whiteSpace = WhiteSpace.Normal;
                msg.style.color = new Color(0.5f, 0.5f, 0.5f);
                _tabContent.Add(msg);
                return;
            }

            if (index >= 0 && index < _tabs.Length)
                _tabs[index].Build(_tabContent);
        }

        private void RefreshActiveTab()
        {
            if (_activeTabIndex >= 0 && _activeTabIndex < _tabs.Length)
            {
                _tabContent.Clear();
                _tabs[_activeTabIndex].Build(_tabContent);
            }
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

        // ──────────────────────────────────────────────
        //  Helpers
        // ──────────────────────────────────────────────

        private void FindConfig()
        {
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
