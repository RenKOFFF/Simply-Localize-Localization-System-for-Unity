using System.Collections.Generic;
using System.IO;
using System.Linq;
using SimplyLocalize.Editor.AssetFilters;
using SimplyLocalize.Editor.AssetPreviews;
using SimplyLocalize.Editor.Data;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace SimplyLocalize.Editor.Windows.Tabs
{
    /// <summary>
    /// Manages localized assets (sprites, audio, custom types) using UI Toolkit.
    ///
    /// - Filter dropdown is populated from AssetTypeFilterRegistry (auto-discovered).
    /// - Previews use AssetPreviewRegistry — users can add custom renderers by implementing
    ///   IAssetPreviewRenderer anywhere in their Editor code.
    /// - Rows are grouped via KeyTreeBuilder. In "All" mode, the tree is grouped by asset
    ///   type first, then by path. In type-specific filters, grouping is path-only.
    /// - Unassigned keys appear under every filter so newly-added keys stay visible
    ///   until the user fills them in.
    /// </summary>
    public class AssetsTab : IEditorTab
    {
        private readonly EditorLocalizationData _data;
        private readonly LocalizationConfig _config;
        private readonly LocalizationEditorWindow _window;

        // Loaded tables: languageCode → table
        private readonly Dictionary<string, LocalizationAssetTable> _tables = new();
        private List<string> _allAssetKeys = new();

        // State
        private IAssetTypeFilter _activeFilter;
        private readonly HashSet<string> _collapsedGroups = new();
        private readonly HashSet<string> _expandedRows = new();

        // UI elements
        private VisualElement _root;
        private ListView _listView;
        private Label _statusLabel;
        private PopupField<string> _filterPopup;
        private List<KeyTreeBuilder.FlatRow> _flatRows = new();
        private List<LanguageProfile> _cachedLanguages = new();

        public AssetsTab(EditorLocalizationData data, LocalizationConfig config,
            LocalizationEditorWindow window)
        {
            _data = data;
            _config = config;
            _window = window;
            RefreshTables();

            EditorApplication.projectChanged += OnProjectChanged;
        }

        ~AssetsTab()
        {
            EditorApplication.projectChanged -= OnProjectChanged;
        }

        private void OnProjectChanged()
        {
            RefreshTables();
            if (_listView != null) RebuildRows();
        }

        // ──────────────────────────────────────────────
        //  Build UI
        // ──────────────────────────────────────────────

        public void Build(VisualElement container)
        {
            _root = new VisualElement();
            _root.style.flexDirection = FlexDirection.Column;
            _root.style.flexGrow = 1;

            // Refresh dynamic filters from actual table contents
            var filters = AssetTypeFilterRegistry.RefreshFromTables(_tables);
            if (_activeFilter == null && filters.Count > 0)
                _activeFilter = filters[0];

            _root.Add(BuildHeader());
            _root.Add(BuildToolbar());
            _root.Add(BuildListView());
            _root.Add(BuildStatusBar());

            container.Add(_root);

            if (_config == null || _config.languages.Count == 0)
            {
                ShowNoLanguagesMessage();
                return;
            }

            RebuildRows();
        }

        private VisualElement BuildHeader()
        {
            var header = new VisualElement();
            header.style.paddingTop = 8;
            header.style.paddingBottom = 4;
            header.style.paddingLeft = 12;
            header.style.paddingRight = 12;

            var title = new Label("Localized Assets");
            title.style.fontSize = 13;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.Add(title);

            var help = new HelpBox(
                "Manage localized sprites, audio clips, and custom assets. " +
                "Each language has its own AssetTable. Click ▶ to expand a preview.",
                HelpBoxMessageType.Info);
            help.style.marginTop = 4;
            header.Add(help);

            return header;
        }

        private VisualElement BuildToolbar()
        {
            var toolbar = new VisualElement();
            toolbar.style.flexDirection = FlexDirection.Row;
            toolbar.style.alignItems = Align.Center;
            toolbar.style.paddingTop = 6;
            toolbar.style.paddingBottom = 6;
            toolbar.style.paddingLeft = 12;
            toolbar.style.paddingRight = 12;
            toolbar.style.borderBottomWidth = 1;
            toolbar.style.borderBottomColor = new Color(0, 0, 0, 0.1f);

            var filterLabel = new Label("Show:");
            filterLabel.style.fontSize = 11;
            filterLabel.style.color = new Color(0.5f, 0.5f, 0.5f);
            filterLabel.style.marginRight = 6;
            toolbar.Add(filterLabel);

            var filters = AssetTypeFilterRegistry.GetAllFilters();
            var filterNames = filters.Select(f => f.DisplayName).ToList();
            int currentIdx = _activeFilter != null
                ? Mathf.Max(0, filterNames.IndexOf(_activeFilter.DisplayName))
                : 0;

            _filterPopup = new PopupField<string>(filterNames, currentIdx);
            _filterPopup.style.minWidth = 140;
            _filterPopup.RegisterValueChangedCallback(evt =>
            {
                var currentFilters = AssetTypeFilterRegistry.GetAllFilters();
                var idx = currentFilters.Select(f => f.DisplayName).ToList().IndexOf(evt.newValue);
                if (idx >= 0) _activeFilter = currentFilters[idx];
                _expandedRows.Clear();
                _collapsedGroups.Clear();
                RebuildRows();
            });
            toolbar.Add(_filterPopup);

            var spacer = new VisualElement();
            spacer.style.flexGrow = 1;
            toolbar.Add(spacer);

            var addBtn = new Button(ShowAddKeyPopup);
            addBtn.text = "+ Add asset key";
            addBtn.style.fontSize = 11;
            addBtn.style.marginRight = 4;
            toolbar.Add(addBtn);

            var createBtn = new Button(CreateMissingTables);
            createBtn.text = "Create missing tables";
            createBtn.style.fontSize = 11;
            toolbar.Add(createBtn);

            return toolbar;
        }

        private VisualElement BuildListView()
        {
            _listView = new ListView
            {
                fixedItemHeight = 32,
                virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight,
                selectionType = SelectionType.None,
                showAlternatingRowBackgrounds = AlternatingRowBackground.None,
                showBorder = false,
                horizontalScrollingEnabled = true,
                makeItem = MakeListItem,
                bindItem = BindListItem,
                unbindItem = UnbindListItem,
                itemsSource = _flatRows
            };
            _listView.style.flexGrow = 1;
            return _listView;
        }

        private VisualElement BuildStatusBar()
        {
            _statusLabel = new Label();
            _statusLabel.style.fontSize = 11;
            _statusLabel.style.color = new Color(0.5f, 0.5f, 0.5f);
            _statusLabel.style.paddingLeft = 12;
            _statusLabel.style.paddingTop = 4;
            _statusLabel.style.paddingBottom = 4;
            _statusLabel.style.borderTopWidth = 1;
            _statusLabel.style.borderTopColor = new Color(0, 0, 0, 0.1f);
            return _statusLabel;
        }

        private void ShowNoLanguagesMessage()
        {
            var msg = new Label("No languages configured.");
            msg.style.paddingTop = 40;
            msg.style.paddingLeft = 20;
            msg.style.color = new Color(0.5f, 0.5f, 0.5f);
            _root.Add(msg);
        }

        // ──────────────────────────────────────────────
        //  Rebuild model
        // ──────────────────────────────────────────────

        private void RebuildRows()
        {
            if (_listView == null) return;

            _cachedLanguages = _config != null
                ? _config.languages.Where(p => p != null).ToList()
                : new List<LanguageProfile>();

            // Refresh dynamic filters from current table contents
            var freshFilters = AssetTypeFilterRegistry.RefreshFromTables(_tables);
            RefreshFilterPopup(freshFilters);

            // Ensure active filter is still valid
            if (_activeFilter != null && !freshFilters.Contains(_activeFilter))
            {
                // Try to find filter with the same display name (after refresh it's a new instance)
                var match = freshFilters.FirstOrDefault(f => f.DisplayName == _activeFilter.DisplayName);
                _activeFilter = match ?? (freshFilters.Count > 0 ? freshFilters[0] : null);
            }

            var filtered = new List<string>();
            IReadOnlyDictionary<string, LocalizationAssetTable> tablesRo = _tables;

            foreach (var key in _allAssetKeys)
            {
                if (_activeFilter == null || _activeFilter.MatchesKey(key, tablesRo))
                    filtered.Add(key);
            }

            filtered.Sort(System.StringComparer.Ordinal);

            KeyTreeBuilder.TreeNode root;
            bool isAllFilter = _activeFilter is AllAssetFilter;

            if (isAllFilter)
                root = KeyTreeBuilder.BuildGrouped(filtered, GetTypeGroupForKey);
            else
                root = KeyTreeBuilder.Build(filtered);

            _flatRows = KeyTreeBuilder.Flatten(root, _collapsedGroups);

            _listView.itemsSource = _flatRows;
            _listView.Rebuild();

            UpdateStatus(filtered.Count);
        }

        private string GetTypeGroupForKey(string key)
        {
            foreach (var table in _tables.Values)
            {
                if (table == null) continue;
                var asset = table.Get(key);
                if (asset == null) continue;
                return asset.GetType().Name;
            }
            return "Unassigned";
        }

        private void UpdateStatus(int visibleKeyCount)
        {
            if (_statusLabel == null) return;

            int totalCells = visibleKeyCount * _cachedLanguages.Count;
            int filledCells = 0;

            foreach (var row in _flatRows)
            {
                if (row.Type != KeyTreeBuilder.FlatRowType.KeyRow) continue;

                foreach (var lang in _cachedLanguages)
                {
                    if (GetTable(lang.Code)?.Get(row.Key) != null) filledCells++;
                }
            }

            _statusLabel.text = $"{visibleKeyCount} keys  |  {filledCells}/{totalCells} assigned  |  filter: {_activeFilter?.DisplayName ?? "None"}";
        }

        private void RefreshFilterPopup(IReadOnlyList<IAssetTypeFilter> filters)
        {
            if (_filterPopup == null) return;

            var newNames = filters.Select(f => f.DisplayName).ToList();
            var currentNames = _filterPopup.choices;

            // Only update if choices actually changed
            if (currentNames != null && currentNames.Count == newNames.Count
                && currentNames.SequenceEqual(newNames))
                return;

            string currentValue = _filterPopup.value;
            _filterPopup.choices = newNames;

            // Restore selection if possible
            if (newNames.Contains(currentValue))
                _filterPopup.SetValueWithoutNotify(currentValue);
            else if (newNames.Count > 0)
                _filterPopup.SetValueWithoutNotify(newNames[0]);
        }

        // ──────────────────────────────────────────────
        //  ListView item factory
        // ──────────────────────────────────────────────

        private VisualElement MakeListItem()
        {
            var root = new VisualElement();
            root.style.flexDirection = FlexDirection.Column;
            root.style.borderBottomWidth = 1;
            root.style.borderBottomColor = new Color(0, 0, 0, 0.08f);

            var groupContainer = new VisualElement();
            groupContainer.name = "group-container";
            groupContainer.style.flexDirection = FlexDirection.Row;
            groupContainer.style.paddingTop = 4;
            groupContainer.style.paddingBottom = 4;
            groupContainer.style.display = DisplayStyle.None;
            groupContainer.style.backgroundColor = new Color(0, 0, 0, 0.04f);

            var groupLabel = new Label();
            groupLabel.name = "group-label";
            groupLabel.style.fontSize = 11;
            groupLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            groupLabel.style.color = new Color(0.4f, 0.4f, 0.4f);
            groupContainer.Add(groupLabel);
            root.Add(groupContainer);

            var keyContainer = new VisualElement();
            keyContainer.name = "key-container";
            keyContainer.style.flexDirection = FlexDirection.Column;
            keyContainer.style.display = DisplayStyle.None;

            var mainRow = new VisualElement();
            mainRow.name = "main-row";
            mainRow.style.flexDirection = FlexDirection.Row;
            mainRow.style.alignItems = Align.Center;
            mainRow.style.paddingTop = 3;
            mainRow.style.paddingBottom = 3;

            var expandBtn = new Button();
            expandBtn.name = "expand-btn";
            expandBtn.text = "▶";
            expandBtn.style.fontSize = 10;
            expandBtn.style.width = 18;
            expandBtn.style.height = 18;
            expandBtn.style.marginLeft = 4;
            expandBtn.style.marginRight = 4;
            expandBtn.style.paddingLeft = 0;
            expandBtn.style.paddingRight = 0;
            expandBtn.style.paddingTop = 0;
            expandBtn.style.paddingBottom = 0;
            expandBtn.style.backgroundColor = new StyleColor(StyleKeyword.Null);
            expandBtn.style.borderLeftWidth = 0;
            expandBtn.style.borderRightWidth = 0;
            expandBtn.style.borderTopWidth = 0;
            expandBtn.style.borderBottomWidth = 0;
            mainRow.Add(expandBtn);

            var keyLabel = new Label();
            keyLabel.name = "key-label";
            keyLabel.style.width = 200;
            keyLabel.style.fontSize = 11;
            keyLabel.style.paddingTop = 2;
            mainRow.Add(keyLabel);

            var fieldsContainer = new VisualElement();
            fieldsContainer.name = "fields-container";
            fieldsContainer.style.flexDirection = FlexDirection.Row;
            mainRow.Add(fieldsContainer);

            for (int i = 0; i < 8; i++)
            {
                var field = new ObjectField();
                field.name = $"asset-field-{i}";
                field.style.width = 150;
                field.style.marginRight = 4;
                field.style.display = DisplayStyle.None;
                field.allowSceneObjects = false;

                int capturedIdx = i;
                field.RegisterValueChangedCallback(evt =>
                {
                    OnAssetFieldChanged(keyContainer, capturedIdx, evt.newValue);
                });

                fieldsContainer.Add(field);
            }

            keyContainer.Add(mainRow);

            var previewRow = new VisualElement();
            previewRow.name = "preview-row";
            previewRow.style.flexDirection = FlexDirection.Row;
            previewRow.style.display = DisplayStyle.None;
            previewRow.style.paddingLeft = 22 + 200 + 4;
            previewRow.style.paddingBottom = 4;

            for (int i = 0; i < 8; i++)
            {
                var previewSlot = new IMGUIContainer();
                previewSlot.name = $"preview-slot-{i}";
                previewSlot.style.width = 150;
                previewSlot.style.height = 96;
                previewSlot.style.marginRight = 4;
                previewSlot.style.display = DisplayStyle.None;
                previewRow.Add(previewSlot);
            }

            keyContainer.Add(previewRow);
            root.Add(keyContainer);

            expandBtn.clicked += () =>
            {
                var item = root.userData as KeyTreeBuilder.FlatRow;
                if (item == null || item.Type != KeyTreeBuilder.FlatRowType.KeyRow) return;

                if (_expandedRows.Contains(item.Key))
                    _expandedRows.Remove(item.Key);
                else
                    _expandedRows.Add(item.Key);

                _listView.RefreshItem(_flatRows.IndexOf(item));
            };

            groupContainer.RegisterCallback<ClickEvent>(_ =>
            {
                var item = root.userData as KeyTreeBuilder.FlatRow;
                if (item == null || item.Type != KeyTreeBuilder.FlatRowType.GroupHeader) return;

                if (_collapsedGroups.Contains(item.GroupPath))
                    _collapsedGroups.Remove(item.GroupPath);
                else
                    _collapsedGroups.Add(item.GroupPath);

                RebuildRows();
            });

            keyLabel.RegisterCallback<ContextClickEvent>(evt =>
            {
                var item = root.userData as KeyTreeBuilder.FlatRow;
                if (item == null || item.Type != KeyTreeBuilder.FlatRowType.KeyRow) return;
                ShowKeyContextMenu(item.Key);
                evt.StopPropagation();
            });

            return root;
        }

        private void BindListItem(VisualElement element, int index)
        {
            if (index < 0 || index >= _flatRows.Count) return;

            var item = _flatRows[index];
            element.userData = item;

            var groupContainer = element.Q("group-container");
            var keyContainer = element.Q("key-container");

            if (item.Type == KeyTreeBuilder.FlatRowType.GroupHeader)
            {
                groupContainer.style.display = DisplayStyle.Flex;
                keyContainer.style.display = DisplayStyle.None;
                groupContainer.style.paddingLeft = 8 + item.Depth * 16;

                var groupLabel = element.Q<Label>("group-label");
                string arrow = item.IsCollapsed ? "▶" : "▼";
                groupLabel.text = $"{arrow} {item.GroupDisplayName} ({item.GroupKeyCount})";
            }
            else
            {
                groupContainer.style.display = DisplayStyle.None;
                keyContainer.style.display = DisplayStyle.Flex;
                BindKeyRow(element, item);
            }
        }

        private void BindKeyRow(VisualElement element, KeyTreeBuilder.FlatRow item)
        {
            string key = item.Key;
            bool isExpanded = _expandedRows.Contains(key);

            var expandBtn = element.Q<Button>("expand-btn");
            expandBtn.text = isExpanded ? "▼" : "▶";

            var keyLabel = element.Q<Label>("key-label");
            string shortKey = key.Substring(key.LastIndexOf('/') + 1);
            keyLabel.text = shortKey;
            keyLabel.tooltip = key;
            keyLabel.style.paddingLeft = item.Depth * 16;

            var acceptedType = _activeFilter?.AcceptedFieldType ?? typeof(Object);

            if (_activeFilter is AllAssetFilter)
                acceptedType = GetAutoDetectedType(key);

            for (int i = 0; i < 8; i++)
            {
                var field = element.Q<ObjectField>($"asset-field-{i}");

                if (i >= _cachedLanguages.Count)
                {
                    field.style.display = DisplayStyle.None;
                    field.userData = null;
                    continue;
                }

                var lang = _cachedLanguages[i];
                field.style.display = DisplayStyle.Flex;
                field.objectType = acceptedType;
                field.userData = new FieldBinding { Key = key, LanguageCode = lang.Code };

                var currentAsset = GetTable(lang.Code)?.Get(key);
                field.SetValueWithoutNotify(currentAsset);
            }

            var previewRow = element.Q("preview-row");
            previewRow.style.display = isExpanded ? DisplayStyle.Flex : DisplayStyle.None;

            if (isExpanded)
            {
                for (int i = 0; i < 8; i++)
                {
                    var slot = (IMGUIContainer)previewRow[i];

                    if (i >= _cachedLanguages.Count)
                    {
                        slot.style.display = DisplayStyle.None;
                        slot.onGUIHandler = null;
                        continue;
                    }

                    slot.style.display = DisplayStyle.Flex;

                    var lang = _cachedLanguages[i];
                    string capturedLang = lang.Code;
                    string capturedKey = key;

                    slot.onGUIHandler = () =>
                    {
                        var asset = GetTable(capturedLang)?.Get(capturedKey);
                        var rect = new Rect(0, 0, 150, 96);
                        var renderer = AssetPreviewRegistry.GetRendererFor(asset);
                        renderer.DrawPreview(rect, asset);
                    };
                }
            }
        }

        private void UnbindListItem(VisualElement element, int index)
        {
            element.userData = null;

            for (int i = 0; i < 8; i++)
            {
                var field = element.Q<ObjectField>($"asset-field-{i}");
                if (field != null) field.userData = null;

                var slot = element.Q<IMGUIContainer>($"preview-slot-{i}");
                if (slot != null) slot.onGUIHandler = null;
            }
        }

        private class FieldBinding
        {
            public string Key;
            public string LanguageCode;
        }

        // ──────────────────────────────────────────────
        //  Edit handlers
        // ──────────────────────────────────────────────

        private void OnAssetFieldChanged(VisualElement keyContainer, int fieldIdx, Object newAsset)
        {
            var field = keyContainer.Q<ObjectField>($"asset-field-{fieldIdx}");
            var binding = field?.userData as FieldBinding;
            if (binding == null) return;

            EnsureTable(binding.LanguageCode);
            var table = GetTable(binding.LanguageCode);
            if (table == null) return;

            var currentAsset = table.Get(binding.Key);
            if (currentAsset == newAsset) return;

            if (newAsset != null)
                table.Set(binding.Key, newAsset);
            else
                table.Remove(binding.Key);

            EditorUtility.SetDirty(table);

            if (_activeFilter is AllAssetFilter)
                RebuildRows();
            else
                _listView.RefreshItems();
        }

        private System.Type GetAutoDetectedType(string key)
        {
            foreach (var table in _tables.Values)
            {
                if (table == null) continue;
                var asset = table.Get(key);
                if (asset == null) continue;
                return asset.GetType();
            }
            return typeof(Object);
        }

        // ──────────────────────────────────────────────
        //  Table management
        // ──────────────────────────────────────────────

        private void RefreshTables()
        {
            _tables.Clear();
            _allAssetKeys = new List<string>();

            if (_config == null || string.IsNullOrEmpty(_data?.BasePath))
                return;

            foreach (var profile in _config.languages)
            {
                if (profile == null) continue;

                string langPath = Path.Combine(_data.BasePath, profile.Code);
                string assetPath = FindAssetTablePath(langPath);

                if (!string.IsNullOrEmpty(assetPath))
                {
                    string relativePath = "Assets" + assetPath.Substring(Application.dataPath.Length);
                    var table = AssetDatabase.LoadAssetAtPath<LocalizationAssetTable>(relativePath);

                    if (table != null)
                    {
                        _tables[profile.Code] = table;

                        foreach (var entry in table.Entries)
                        {
                            if (!_allAssetKeys.Contains(entry.key))
                                _allAssetKeys.Add(entry.key);
                        }
                    }
                }
            }

            _allAssetKeys.Sort();
        }

        private LocalizationAssetTable GetTable(string langCode)
        {
            return _tables.TryGetValue(langCode, out var table) ? table : null;
        }

        private void EnsureTable(string langCode)
        {
            if (_tables.ContainsKey(langCode) && _tables[langCode] != null) return;

            string langDir = Path.Combine(_data.BasePath, langCode);

            if (!Directory.Exists(langDir))
                Directory.CreateDirectory(langDir);

            var table = ScriptableObject.CreateInstance<LocalizationAssetTable>();
            string relativeLangDir = "Assets" + langDir.Substring(Application.dataPath.Length);
            string tablePath = Path.Combine(relativeLangDir, "AssetTable.asset");
            tablePath = AssetDatabase.GenerateUniqueAssetPath(tablePath);

            AssetDatabase.CreateAsset(table, tablePath);
            AssetDatabase.SaveAssets();

            _tables[langCode] = table;
        }

        private void CreateMissingTables()
        {
            int created = 0;

            foreach (var profile in _config.languages)
            {
                if (profile == null) continue;

                if (!_tables.ContainsKey(profile.Code) || _tables[profile.Code] == null)
                {
                    EnsureTable(profile.Code);
                    created++;
                }
            }

            AssetDatabase.Refresh();

            if (created > 0)
            {
                EditorUtility.DisplayDialog("Tables created",
                    $"Created {created} asset table(s).", "OK");
                RebuildRows();
            }
        }

        private static string FindAssetTablePath(string langDir)
        {
            if (!Directory.Exists(langDir)) return null;

            var files = Directory.GetFiles(langDir, "*.asset");

            foreach (var file in files)
            {
                string relativePath = "Assets" + file.Substring(Application.dataPath.Length);
                var obj = AssetDatabase.LoadAssetAtPath<LocalizationAssetTable>(relativePath);
                if (obj != null) return file;
            }

            return null;
        }

        // ──────────────────────────────────────────────
        //  Key management popups
        // ──────────────────────────────────────────────

        private void ShowAddKeyPopup()
        {
            var popup = ScriptableObject.CreateInstance<AddAssetKeyPopup>();
            popup.Init(_allAssetKeys, key =>
            {
                if (!_allAssetKeys.Contains(key))
                {
                    _allAssetKeys.Add(key);
                    _allAssetKeys.Sort();
                }

                foreach (var table in _tables.Values)
                {
                    if (table != null && !table.HasKey(key))
                    {
                        table.Set(key, null);
                        EditorUtility.SetDirty(table);
                    }
                }

                AssetDatabase.SaveAssets();
                RebuildRows();
            });

            popup.titleContent = new GUIContent("Add asset key");
            popup.ShowUtility();
            popup.position = new Rect(
                Screen.width / 2f - 150, Screen.height / 2f - 30, 300, 70);
        }

        private void ShowKeyContextMenu(string key)
        {
            var menu = new GenericMenu();

            menu.AddItem(new GUIContent("Copy key"), false,
                () => EditorGUIUtility.systemCopyBuffer = key);

            menu.AddSeparator("");

            menu.AddItem(new GUIContent("Delete key from all tables"), false, () =>
            {
                if (!EditorUtility.DisplayDialog("Delete asset key",
                    $"Remove '{key}' from all asset tables?", "Delete", "Cancel"))
                    return;

                foreach (var table in _tables.Values)
                {
                    if (table == null) continue;
                    table.Remove(key);
                    EditorUtility.SetDirty(table);
                }

                _allAssetKeys.Remove(key);
                AssetDatabase.SaveAssets();
                RebuildRows();
            });

            menu.ShowAsContext();
        }
    }

    // ──────────────────────────────────────────────
    //  Popup for adding new asset keys
    // ──────────────────────────────────────────────

    public class AddAssetKeyPopup : EditorWindow
    {
        private List<string> _existing;
        private System.Action<string> _onAdd;
        private string _key = "";

        public void Init(List<string> existing, System.Action<string> onAdd)
        {
            _existing = existing;
            _onAdd = onAdd;
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(4);
            _key = EditorGUILayout.TextField("Key", _key);

            bool dup = _existing != null && _existing.Contains(_key);
            bool empty = string.IsNullOrEmpty(_key);

            if (dup) EditorGUILayout.HelpBox("Key already exists.", MessageType.Error);

            GUI.enabled = !dup && !empty;

            if (GUILayout.Button("Add"))
            {
                _onAdd?.Invoke(_key);
                Close();
            }

            GUI.enabled = true;
        }
    }
}
