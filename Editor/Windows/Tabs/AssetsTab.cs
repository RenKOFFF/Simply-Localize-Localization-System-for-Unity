using System.Collections.Generic;
using System.IO;
using System.Linq;
using SimplyLocalize.Editor.AssetFilters;
using SimplyLocalize.Editor.AssetPreviews;
using SimplyLocalize.Editor.Data;
using SimplyLocalize.Editor.Utilities;
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
    /// - Search with debounce, Undo/Redo (Ctrl+Z/Y), header row with language names,
    ///   rename with KeyReferenceUpdater — matching the TranslationsTab polish level.
    /// </summary>
    public class AssetsTab : IEditorTab
    {
        private const int KeyColumnWidth = 200;
        private const int FieldColumnWidth = 150;
        private const int ExpandButtonWidth = 26; // 18 + 4 left + 4 right

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
        private string _searchQuery = "";

        // UI elements
        private VisualElement _root;
        private VisualElement _headerRow;
        private ScrollView _listScrollView;
        private ListView _listView;
        private Label _statusLabel;
        private PopupField<string> _filterPopup;
        private List<KeyTreeBuilder.FlatRow> _flatRows = new();
        private List<LanguageProfile> _cachedLanguages = new();

        // Search debounce
        private IVisualElementScheduledItem _searchDebounce;

        // Undo/Redo
        private readonly Stack<AssetEditOp> _undoStack = new();
        private readonly Stack<AssetEditOp> _redoStack = new();
        private const int MaxUndoHistory = 100;

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

            var filters = AssetTypeFilterRegistry.RefreshFromTables(_tables);
            if (_activeFilter == null && filters.Count > 0)
                _activeFilter = filters[0];

            _root.Add(BuildHeader());
            _root.Add(BuildToolbar());
            _root.Add(BuildColumnHeaderRow());
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

            // Search field
            var searchField = new ToolbarSearchField();
            searchField.value = _searchQuery;
            searchField.style.flexGrow = 1;
            searchField.style.fontSize = 12;
            searchField.style.marginRight = 8;
            searchField.RegisterValueChangedCallback(evt =>
            {
                _searchQuery = evt.newValue ?? "";

                // Debounce: wait 150ms after the last keystroke before rebuilding
                _searchDebounce?.Pause();
                _searchDebounce = _listView?.schedule.Execute(RebuildRows).StartingIn(150);
            });
            toolbar.Add(searchField);

            // Filter dropdown
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
            _filterPopup.style.marginRight = 8;
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

        /// <summary>
        /// Builds a sticky column-header row with language names aligned above the
        /// ObjectField columns in each data row.
        /// </summary>
        private VisualElement BuildColumnHeaderRow()
        {
            // Outer wrapper clips overflow when the header is translated horizontally
            var wrapper = new VisualElement();
            wrapper.style.overflow = Overflow.Hidden;
            wrapper.style.borderBottomWidth = 1;
            wrapper.style.borderBottomColor = new Color(0, 0, 0, 0.1f);
            wrapper.style.backgroundColor = new Color(0, 0, 0, 0.04f);

            _headerRow = new VisualElement();
            _headerRow.style.flexDirection = FlexDirection.Row;
            _headerRow.style.alignItems = Align.Center;
            _headerRow.style.paddingTop = 4;
            _headerRow.style.paddingBottom = 4;

            wrapper.Add(_headerRow);
            return wrapper;
        }

        private void RefreshColumnHeader()
        {
            if (_headerRow == null) return;
            _headerRow.Clear();

            // Spacer matching expand button + key column
            var keySpacer = new Label("Key");
            keySpacer.style.width = ExpandButtonWidth + KeyColumnWidth;
            keySpacer.style.marginLeft = 0;
            keySpacer.style.marginRight = 0;
            keySpacer.style.marginTop = 0;
            keySpacer.style.marginBottom = 0;
            keySpacer.style.fontSize = 11;
            keySpacer.style.unityFontStyleAndWeight = FontStyle.Bold;
            keySpacer.style.color = new Color(0.5f, 0.5f, 0.5f);
            keySpacer.style.paddingLeft = ExpandButtonWidth;
            _headerRow.Add(keySpacer);

            // One wrapper per language — identical stride to data-row slots.
            // The Label inside just fills the wrapper; its own margins/paddings
            // no longer affect column positions.
            foreach (var lang in _cachedLanguages)
            {
                var slot = new VisualElement();
                slot.style.width = FieldColumnWidth;
                slot.style.marginLeft = 0;
                slot.style.marginRight = 4;
                slot.style.marginTop = 0;
                slot.style.marginBottom = 0;
                slot.style.flexDirection = FlexDirection.Row;
                slot.style.overflow = Overflow.Hidden;

                var cell = new Label(lang.Code + " — " + lang.displayName);
                cell.style.flexGrow = 1;
                cell.style.marginLeft = 0;
                cell.style.marginRight = 0;
                cell.style.marginTop = 0;
                cell.style.marginBottom = 0;
                cell.style.paddingLeft = 0;
                cell.style.paddingRight = 0;
                cell.style.fontSize = 11;
                cell.style.unityFontStyleAndWeight = FontStyle.Bold;
                cell.style.color = new Color(0.5f, 0.5f, 0.5f);
                cell.style.overflow = Overflow.Hidden;
                cell.style.textOverflow = TextOverflow.Ellipsis;

                slot.Add(cell);
                _headerRow.Add(slot);
            }
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

            // Undo/Redo shortcuts
            _listView.RegisterCallback<KeyDownEvent>(OnListKeyDown);

            // Hook into ListView's internal ScrollView so the header row can mirror
            // the horizontal scroll position. Deferred until after attach.
            _listView.schedule.Execute(AttachHeaderScrollSync).StartingIn(0);

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

            RefreshColumnHeader();

            var freshFilters = AssetTypeFilterRegistry.RefreshFromTables(_tables);
            RefreshFilterPopup(freshFilters);

            if (_activeFilter != null && !freshFilters.Contains(_activeFilter))
            {
                var match = freshFilters.FirstOrDefault(f => f.DisplayName == _activeFilter.DisplayName);
                _activeFilter = match ?? (freshFilters.Count > 0 ? freshFilters[0] : null);
            }

            // Apply filter
            var filtered = new List<string>();
            IReadOnlyDictionary<string, LocalizationAssetTable> tablesRo = _tables;

            foreach (var key in _allAssetKeys)
            {
                if (_activeFilter == null || _activeFilter.MatchesKey(key, tablesRo))
                    filtered.Add(key);
            }

            // Apply search
            if (!string.IsNullOrEmpty(_searchQuery))
            {
                string q = _searchQuery.ToLowerInvariant();
                filtered = filtered.Where(k => k.ToLowerInvariant().Contains(q)).ToList();
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

            // Rebuild may recreate the internal ScrollView — re-attach the scroll sync
            _listView.schedule.Execute(AttachHeaderScrollSync).StartingIn(0);

            UpdateStatus(filtered.Count);
        }

        private string GetTypeGroupForKey(string key)
        {
            foreach (var table in _tables.Values)
            {
                if (table == null) continue;
                var asset = table.Get(key);
                if (asset == null) continue;
                return ObjectNames.NicifyVariableName(asset.GetType().Name);
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

            string searchText = !string.IsNullOrEmpty(_searchQuery)
                ? $"  |  search: \"{_searchQuery}\""
                : "";

            _statusLabel.text = $"{visibleKeyCount} keys  |  {filledCells}/{totalCells} assigned  |  filter: {_activeFilter?.DisplayName ?? "None"}{searchText}";
        }

        private void RefreshFilterPopup(IReadOnlyList<IAssetTypeFilter> filters)
        {
            if (_filterPopup == null) return;

            var newNames = filters.Select(f => f.DisplayName).ToList();
            var currentNames = _filterPopup.choices;

            if (currentNames != null && currentNames.Count == newNames.Count
                && currentNames.SequenceEqual(newNames))
                return;

            string currentValue = _filterPopup.value;
            _filterPopup.choices = newNames;

            if (newNames.Contains(currentValue))
                _filterPopup.SetValueWithoutNotify(currentValue);
            else if (newNames.Count > 0)
                _filterPopup.SetValueWithoutNotify(newNames[0]);
        }

        // ────────────────────────────────────────────
        //  Header / ListView horizontal scroll sync
        // ────────────────────────────────────────────

        /// <summary>
        /// Hooks the header row to the ListView's internal horizontal scrollbar so the
        /// header columns stay aligned with the data columns when scrolling sideways.
        /// </summary>
        private void AttachHeaderScrollSync()
        {
            if (_listView == null || _headerRow == null) return;

            var newScrollView = _listView.Q<ScrollView>();
            if (newScrollView == null) return;

            // Already subscribed to this exact ScrollView — nothing to do
            if (newScrollView == _listScrollView) return;

            // Detach previous subscription if the ScrollView was recreated
            if (_listScrollView?.horizontalScroller != null)
                _listScrollView.horizontalScroller.valueChanged -= OnHorizontalScrollChanged;

            _listScrollView = newScrollView;
            _listScrollView.horizontalScroller.valueChanged += OnHorizontalScrollChanged;

            // Apply current value immediately so header stays in sync after rebuilds
            OnHorizontalScrollChanged(_listScrollView.horizontalScroller.value);
        }

        private void OnHorizontalScrollChanged(float value)
        {
            if (_headerRow == null || _listScrollView == null) return;

            // Use the actual content container transform instead of the raw scroll value.
            // This automatically accounts for ScrollView's internal padding/margins so the
            // header stays pixel-perfect aligned with data columns regardless of language count.
            var contentX = _listScrollView.contentContainer.transform.position.x;
            _headerRow.style.translate = new Translate(contentX, 0);
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
            keyLabel.style.width = KeyColumnWidth;
            keyLabel.style.fontSize = 11;
            keyLabel.style.paddingTop = 2;
            mainRow.Add(keyLabel);

            var fieldsContainer = new VisualElement();
            fieldsContainer.name = "fields-container";
            fieldsContainer.style.flexDirection = FlexDirection.Row;
            mainRow.Add(fieldsContainer);

            for (int i = 0; i < 8; i++)
            {
                // Wrapper slot — FIXED stride of FieldColumnWidth+4px.
                // The ObjectField inside flexGrows to fill it, so its own
                // borders/paddings don't affect column position.
                var slot = new VisualElement();
                slot.name = $"asset-slot-{i}";
                slot.style.width = FieldColumnWidth;
                slot.style.marginLeft = 0;
                slot.style.marginRight = 4;
                slot.style.marginTop = 0;
                slot.style.marginBottom = 0;
                slot.style.flexDirection = FlexDirection.Row;
                slot.style.display = DisplayStyle.None;

                var field = new ObjectField();
                field.name = $"asset-field-{i}";
                field.style.flexGrow = 1;
                field.style.marginLeft = 0;
                field.style.marginRight = 0;
                field.style.marginTop = 0;
                field.style.marginBottom = 0;
                field.allowSceneObjects = false;

                int capturedIdx = i;
                field.RegisterValueChangedCallback(evt =>
                {
                    OnAssetFieldChanged(keyContainer, capturedIdx, evt.previousValue, evt.newValue);
                });

                slot.Add(field);
                fieldsContainer.Add(slot);
            }

            keyContainer.Add(mainRow);

            var previewRow = new VisualElement();
            previewRow.name = "preview-row";
            previewRow.style.flexDirection = FlexDirection.Row;
            previewRow.style.display = DisplayStyle.None;
            previewRow.style.paddingLeft = ExpandButtonWidth + KeyColumnWidth;
            previewRow.style.paddingBottom = 4;

            for (int i = 0; i < 8; i++)
            {
                var previewSlot = new IMGUIContainer();
                previewSlot.name = $"preview-slot-{i}";
                previewSlot.style.width = FieldColumnWidth;
                previewSlot.style.height = 96;
                previewSlot.style.marginLeft = 0;
                previewSlot.style.marginRight = 4;
                previewSlot.style.marginTop = 0;
                previewSlot.style.marginBottom = 0;
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
                var slot = element.Q($"asset-slot-{i}");
                var field = element.Q<ObjectField>($"asset-field-{i}");

                if (i >= _cachedLanguages.Count)
                {
                    slot.style.display = DisplayStyle.None;
                    field.userData = null;
                    continue;
                }

                var lang = _cachedLanguages[i];
                slot.style.display = DisplayStyle.Flex;
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
                    var capturedProfile = lang;

                    slot.onGUIHandler = () =>
                    {
                        var asset = GetTable(capturedLang)?.Get(capturedKey);

                        // Fallback chain resolution when language has no asset
                        bool isFallback = false;
                        string fromLang = null;

                        if (asset == null && capturedProfile != null)
                        {
                            asset = ResolvePreviewFallbackAsset(
                                capturedProfile, capturedKey, out fromLang);
                            isFallback = asset != null;
                        }

                        var rect = new Rect(0, 0, FieldColumnWidth, 96);

                        var prevColor = GUI.color;
                        if (isFallback) GUI.color = new Color(1f, 1f, 1f, 0.55f);

                        var renderer = AssetPreviewRegistry.GetRendererFor(asset);
                        renderer.DrawPreview(rect, asset);

                        GUI.color = prevColor;

                        // Origin marker at bottom-left for fallback-sourced previews
                        if (isFallback && !string.IsNullOrEmpty(fromLang))
                        {
                            var labelRect = new Rect(2, 96 - 14, FieldColumnWidth - 4, 12);
                            var labelStyle = new GUIStyle(EditorStyles.miniLabel)
                            {
                                fontStyle = FontStyle.Italic,
                                alignment = TextAnchor.LowerLeft
                            };
                            labelStyle.normal.textColor = new Color(1f, 0.85f, 0.7f);
                            GUI.Label(labelRect, $"\u2014 from {fromLang}", labelStyle);
                        }
                    };
                }
            }
        }

        /// <summary>
        /// Walks the fallback chain for an asset when the target language's table has nothing.
        /// Mirrors runtime behaviour (per-language chain -> global fallback) with cycle guard.
        /// </summary>
        private Object ResolvePreviewFallbackAsset(LanguageProfile target, string key,
            out string fromLanguage)
        {
            fromLanguage = null;

            if (_config == null || target == null) return null;

            var visited = new HashSet<string> { target.Code };
            var fb = target.fallbackProfile;

            while (fb != null && visited.Add(fb.Code))
            {
                var candidate = GetTable(fb.Code)?.Get(key);
                if (candidate != null)
                {
                    fromLanguage = fb.Code;
                    return candidate;
                }
                fb = fb.fallbackProfile;
            }

            // Global fallback
            string globalCode = _config.FallbackLanguageCode;
            if (!string.IsNullOrEmpty(globalCode) && visited.Add(globalCode))
            {
                var candidate = GetTable(globalCode)?.Get(key);
                if (candidate != null)
                {
                    fromLanguage = globalCode;
                    return candidate;
                }
            }

            return null;
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
        //  Edit handlers + Undo/Redo
        // ──────────────────────────────────────────────

        private class AssetEditOp
        {
            public string Key;
            public string LanguageCode;
            public Object OldAsset;
            public Object NewAsset;
        }

        private void OnAssetFieldChanged(VisualElement keyContainer, int fieldIdx,
            Object oldAsset, Object newAsset)
        {
            var field = keyContainer.Q<ObjectField>($"asset-field-{fieldIdx}");
            var binding = field?.userData as FieldBinding;
            if (binding == null) return;

            if (oldAsset == newAsset) return;

            PushUndo(new AssetEditOp
            {
                Key = binding.Key,
                LanguageCode = binding.LanguageCode,
                OldAsset = oldAsset,
                NewAsset = newAsset
            });

            ApplyAssetEdit(binding.Key, binding.LanguageCode, newAsset);
        }

        private void ApplyAssetEdit(string key, string languageCode, Object newAsset)
        {
            EnsureTable(languageCode);
            var table = GetTable(languageCode);
            if (table == null) return;

            if (newAsset != null)
                table.Set(key, newAsset);
            else
                table.Remove(key);

            EditorUtility.SetDirty(table);
            AssetDatabase.SaveAssets();

            // Defer AssetDatabase.Refresh via the shared flag
            _data?.MarkPendingAssetRefresh();

            if (_activeFilter is AllAssetFilter)
                RebuildRows();
            else
                _listView.RefreshItems();
        }

        private void PushUndo(AssetEditOp op)
        {
            _undoStack.Push(op);
            _redoStack.Clear();

            while (_undoStack.Count > MaxUndoHistory)
            {
                var arr = _undoStack.ToArray();
                _undoStack.Clear();
                for (int i = arr.Length - 2; i >= 0; i--) _undoStack.Push(arr[i]);
            }
        }

        private void OnListKeyDown(KeyDownEvent evt)
        {
            bool ctrl = evt.ctrlKey || evt.commandKey;
            if (!ctrl) return;

            if (evt.keyCode == KeyCode.Z && !evt.shiftKey)
            {
                PerformUndo();
                evt.StopPropagation();
            }
            else if (evt.keyCode == KeyCode.Y || (evt.keyCode == KeyCode.Z && evt.shiftKey))
            {
                PerformRedo();
                evt.StopPropagation();
            }
        }

        private void PerformUndo()
        {
            if (_undoStack.Count == 0) return;
            var op = _undoStack.Pop();
            _redoStack.Push(op);
            ApplyAssetEdit(op.Key, op.LanguageCode, op.OldAsset);
        }

        private void PerformRedo()
        {
            if (_redoStack.Count == 0) return;
            var op = _redoStack.Pop();
            _undoStack.Push(op);
            ApplyAssetEdit(op.Key, op.LanguageCode, op.NewAsset);
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

            menu.AddItem(new GUIContent("Rename key"), false, () => ShowRenameKeyPopup(key));

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

        private void ShowRenameKeyPopup(string oldKey)
        {
            var popup = ScriptableObject.CreateInstance<RenameAssetKeyPopup>();
            popup.Init(oldKey, _allAssetKeys, (newKey, updateRefs) =>
            {
                foreach (var table in _tables.Values)
                {
                    if (table == null) continue;
                    if (table.HasKey(oldKey))
                    {
                        table.RenameKey(oldKey, newKey);
                        EditorUtility.SetDirty(table);
                    }
                }

                _allAssetKeys.Remove(oldKey);
                if (!_allAssetKeys.Contains(newKey))
                {
                    _allAssetKeys.Add(newKey);
                    _allAssetKeys.Sort();
                }

                // Preserve expanded-row state across rename
                if (_expandedRows.Remove(oldKey))
                    _expandedRows.Add(newKey);

                int updatedRefs = 0;
                if (updateRefs)
                    updatedRefs = KeyReferenceUpdater.UpdateReferences(oldKey, newKey);

                AssetDatabase.SaveAssets();

                if (updateRefs && updatedRefs > 0)
                {
                    EditorUtility.DisplayDialog("Key renamed",
                        $"Renamed '{oldKey}' → '{newKey}'\n" +
                        $"Updated {updatedRefs} component reference(s) in scenes/prefabs.",
                        "OK");
                }

                RebuildRows();
            });

            popup.titleContent = new GUIContent("Rename asset key");
            popup.ShowUtility();
            popup.position = new Rect(
                Screen.width / 2f - 175, Screen.height / 2f - 75, 350, 150);
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

    // ──────────────────────────────────────────────
    //  Popup for renaming asset keys
    // ──────────────────────────────────────────────

    public class RenameAssetKeyPopup : EditorWindow
    {
        private string _oldKey;
        private string _newKey;
        private List<string> _existing;
        private System.Action<string, bool> _onRenamed;
        private bool _updateReferences = true;

        public void Init(string oldKey, List<string> existing, System.Action<string, bool> onRenamed)
        {
            _oldKey = oldKey;
            _newKey = oldKey;
            _existing = existing;
            _onRenamed = onRenamed;
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(4);

            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.TextField("Current key", _oldKey);

            _newKey = EditorGUILayout.TextField("New key", _newKey);

            bool isDuplicate = _newKey != _oldKey
                && _existing != null
                && _existing.Contains(_newKey);
            bool unchanged = _newKey == _oldKey;
            bool empty = string.IsNullOrEmpty(_newKey);

            if (isDuplicate)
                EditorGUILayout.HelpBox("A key with this name already exists.", MessageType.Error);

            EditorGUILayout.Space(4);

            _updateReferences = EditorGUILayout.Toggle(
                new GUIContent("Update references in project",
                    "Scan all scenes and prefabs and replace the old key with the new one"),
                _updateReferences);

            EditorGUILayout.Space(4);

            GUI.enabled = !isDuplicate && !unchanged && !empty;

            if (GUILayout.Button("Rename"))
            {
                _onRenamed?.Invoke(_newKey, _updateReferences);
                Close();
            }

            GUI.enabled = true;
        }
    }
}
