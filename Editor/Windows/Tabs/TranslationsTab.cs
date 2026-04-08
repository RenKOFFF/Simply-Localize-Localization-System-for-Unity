using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using SimplyLocalize.Editor.Data;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Debug = UnityEngine.Debug;

namespace SimplyLocalize.Editor.Windows.Tabs
{
    public class TranslationsTab : IEditorTab
    {
        private enum ViewMode { Single, Multi, All }

        private readonly EditorLocalizationData _data;
        private readonly LocalizationConfig _config;
        private readonly LocalizationEditorWindow _window;

        private ViewMode _viewMode = ViewMode.All;
        private readonly HashSet<string> _selectedFiles = new();
        private readonly HashSet<string> _collapsedGroups = new();
        private readonly HashSet<string> _selectedKeys = new();
        private string _searchQuery = "";
        private bool _sidebarCollapsed = true;
        private string _lastClickedKey;
        private List<string> _flatVisibleKeys = new();

        // ListView virtualization
        private ListView _listView;
        private List<RowItem> _flatItems = new();

        // Undo/Redo
        private readonly Stack<EditOperation> _undoStack = new();
        private readonly Stack<EditOperation> _redoStack = new();
        private const int MaxUndoHistory = 100;

        // Search debounce
        private IVisualElementScheduledItem _searchDebounce;

        private VisualElement _fileList;
        private VisualElement _sidebarContent;
        private VisualElement _tableBody;
        private Label _statusLabel;

        public TranslationsTab(EditorLocalizationData data, LocalizationConfig config,
            LocalizationEditorWindow window)
        {
            _data = data;
            _config = config;
            _window = window;
        }

        public void Build(VisualElement container)
        {
            var root = new VisualElement();
            root.style.flexDirection = FlexDirection.Row;
            root.style.flexGrow = 1;

            // Left sidebar - file browser
            root.Add(BuildFileSidebar());

            // Right - main content
            root.Add(BuildMainContent());

            container.Add(root);
        }

        // ──────────────────────────────────────────────
        //  File sidebar
        // ──────────────────────────────────────────────

        private VisualElement BuildFileSidebar()
        {
            var sidebar = new VisualElement();
            sidebar.style.borderRightWidth = 1;
            sidebar.style.borderRightColor = new Color(0, 0, 0, 0.15f);

            // Toggle header — always visible
            var toggleRow = new VisualElement();
            toggleRow.style.flexDirection = FlexDirection.Row;
            toggleRow.style.alignItems = Align.Center;
            toggleRow.style.paddingTop = 6;
            toggleRow.style.paddingBottom = 6;
            toggleRow.style.paddingLeft = 6;
            toggleRow.style.cursor = StyleKeyword.Auto;

            var arrow = new Label(_sidebarCollapsed ? "▶" : "▼");
            arrow.style.fontSize = 10;
            arrow.style.width = 14;
            arrow.style.color = new Color(0.5f, 0.5f, 0.5f);
            toggleRow.Add(arrow);

            var headerLabel = new Label(_sidebarCollapsed ? "Files" : "Files");
            headerLabel.style.fontSize = 11;
            headerLabel.style.color = new Color(0.5f, 0.5f, 0.5f);
            headerLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            toggleRow.Add(headerLabel);

            toggleRow.RegisterCallback<ClickEvent>(evt =>
            {
                _sidebarCollapsed = !_sidebarCollapsed;
                RefreshAll();
            });

            sidebar.Add(toggleRow);

            // Collapsible content
            _sidebarContent = new VisualElement();

            if (_sidebarCollapsed)
            {
                sidebar.style.width = 50;
                _sidebarContent.style.display = DisplayStyle.None;
            }
            else
            {
                sidebar.style.width = 180;
                _sidebarContent.style.display = DisplayStyle.Flex;
            }

            // "All files" button
            var allBtn = new Button(() =>
            {
                _viewMode = ViewMode.All;
                _selectedFiles.Clear();
                RefreshAll();
            });
            allBtn.text = $"All files ({_data.KeyCount} keys)";
            allBtn.style.fontSize = 11;
            allBtn.style.marginLeft = 6;
            allBtn.style.marginRight = 6;
            allBtn.style.marginBottom = 4;

            if (_viewMode == ViewMode.All)
                allBtn.style.backgroundColor = new Color(0.2f, 0.5f, 0.9f, 0.15f);

            _sidebarContent.Add(allBtn);

            // File list
            _fileList = new VisualElement();
            _fileList.style.paddingTop = 4;

            foreach (var fileName in _data.SourceFiles)
            {
                int keyCount = _data.GetKeysForFile(fileName).Count();
                bool isSelected = _selectedFiles.Contains(fileName);

                var fileBtn = new Button(() => OnFileClicked(fileName));
                fileBtn.text = $"  {fileName}.json ({keyCount})";
                fileBtn.style.fontSize = 11;
                fileBtn.style.unityTextAlign = TextAnchor.MiddleLeft;
                fileBtn.style.marginLeft = 6;
                fileBtn.style.marginRight = 6;
                fileBtn.style.marginBottom = 1;
                fileBtn.style.borderLeftWidth = 0;
                fileBtn.style.borderRightWidth = 0;
                fileBtn.style.borderTopWidth = 0;
                fileBtn.style.borderBottomWidth = 0;

                if (isSelected || (_viewMode == ViewMode.Single && _selectedFiles.Contains(fileName)))
                    fileBtn.style.backgroundColor = new Color(0.2f, 0.5f, 0.9f, 0.1f);

                string capturedFile = fileName;
                fileBtn.RegisterCallback<ContextClickEvent>(evt =>
                {
                    ShowFileContextMenu(capturedFile);
                    evt.StopPropagation();
                });

                _fileList.Add(fileBtn);
            }

            _sidebarContent.Add(_fileList);

            // Separator
            var sep = new VisualElement();
            sep.style.height = 1;
            sep.style.backgroundColor = new Color(0, 0, 0, 0.1f);
            sep.style.marginTop = 8;
            sep.style.marginBottom = 8;
            sep.style.marginLeft = 10;
            sep.style.marginRight = 10;
            _sidebarContent.Add(sep);

            // View mode selector
            var modeLabel = new Label("View mode");
            modeLabel.style.fontSize = 10;
            modeLabel.style.color = new Color(0.5f, 0.5f, 0.5f);
            modeLabel.style.paddingLeft = 10;
            _sidebarContent.Add(modeLabel);

            var modeRow = new VisualElement();
            modeRow.style.flexDirection = FlexDirection.Row;
            modeRow.style.paddingLeft = 10;
            modeRow.style.paddingTop = 4;
            modeRow.style.paddingBottom = 8;

            foreach (var mode in new[] { ViewMode.Single, ViewMode.Multi, ViewMode.All })
            {
                var m = mode;
                var modeBtn = new Button(() =>
                {
                    _viewMode = m;
                    if (m == ViewMode.All) _selectedFiles.Clear();
                    RefreshAll();
                });
                modeBtn.text = m.ToString();
                modeBtn.style.fontSize = 10;
                modeBtn.style.paddingLeft = 6;
                modeBtn.style.paddingRight = 6;
                modeBtn.style.paddingTop = 2;
                modeBtn.style.paddingBottom = 2;
                modeBtn.style.marginRight = 2;

                if (_viewMode == m)
                    modeBtn.style.backgroundColor = new Color(0.2f, 0.5f, 0.9f, 0.15f);

                modeRow.Add(modeBtn);
            }

            _sidebarContent.Add(modeRow);

            // New file button
            var newFileBtn = new Button(OnNewFileClicked);
            newFileBtn.text = "+ New file";
            newFileBtn.style.fontSize = 11;
            newFileBtn.style.marginLeft = 10;
            newFileBtn.style.marginRight = 10;
            _sidebarContent.Add(newFileBtn);

            sidebar.Add(_sidebarContent);

            return sidebar;
        }

        // ──────────────────────────────────────────────
        //  Main content
        // ──────────────────────────────────────────────

        private VisualElement BuildMainContent()
        {
            var content = new VisualElement();
            content.style.flexGrow = 1;
            content.style.flexDirection = FlexDirection.Column;

            // Toolbar
            var toolbar = new VisualElement();
            toolbar.style.flexDirection = FlexDirection.Row;
            toolbar.style.alignItems = Align.Center;
            toolbar.style.paddingTop = 6;
            toolbar.style.paddingBottom = 6;
            toolbar.style.paddingLeft = 8;
            toolbar.style.paddingRight = 8;
            toolbar.style.borderBottomWidth = 1;
            toolbar.style.borderBottomColor = new Color(0, 0, 0, 0.1f);

            var searchField = new UnityEditor.UIElements.ToolbarSearchField();
            searchField.value = _searchQuery;
            searchField.style.flexGrow = 1;
            searchField.style.fontSize = 12;

            searchField.RegisterValueChangedCallback(evt =>
            {
                _searchQuery = evt.newValue ?? "";

                // Debounce: wait 150ms after the last keystroke before rebuilding
                _searchDebounce?.Pause();
                _searchDebounce = _listView?.schedule.Execute(RebuildTable).StartingIn(150);
            });
            toolbar.Add(searchField);

            var addKeyBtn = new Button(OnAddKeyClicked);
            addKeyBtn.text = "+ Add key";
            addKeyBtn.style.fontSize = 11;
            addKeyBtn.style.marginLeft = 6;
            toolbar.Add(addKeyBtn);

            content.Add(toolbar);

            // Header row (static, above the ListView)
            _tableBody = new VisualElement();
            content.Add(_tableBody);

            // Virtualized ListView for rows
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
                itemsSource = _flatItems
            };
            _listView.style.flexGrow = 1;

            // Keyboard shortcuts (Ctrl+Z / Ctrl+Y) on the ListView itself
            _listView.RegisterCallback<KeyDownEvent>(OnListKeyDown);

            content.Add(_listView);

            RebuildTable();

            // Status bar
            _statusLabel = new Label();
            _statusLabel.style.fontSize = 11;
            _statusLabel.style.color = new Color(0.5f, 0.5f, 0.5f);
            _statusLabel.style.paddingLeft = 8;
            _statusLabel.style.paddingTop = 4;
            _statusLabel.style.paddingBottom = 4;
            _statusLabel.style.borderTopWidth = 1;
            _statusLabel.style.borderTopColor = new Color(0, 0, 0, 0.1f);
            UpdateStatus();
            content.Add(_statusLabel);

            return content;
        }

        // ──────────────────────────────────────────────
        //  Table (virtualized)
        // ──────────────────────────────────────────────

        private List<LanguageProfile> _cachedLanguages = new();

        /// <summary>
        /// Full rebuild: rebuild the header, the flat row model, and refresh the ListView.
        /// Called on filter/search/collapse changes.
        /// </summary>
        private void RebuildTable()
        {
            if (_tableBody == null || _listView == null) return;

            var swTotal = Stopwatch.StartNew();

            bool langCountChanged = _config != null
                && (_cachedLanguages == null
                    || _cachedLanguages.Count != _config.languages.Count(p => p != null));

            _cachedLanguages = _config != null
                ? _config.languages.Where(p => p != null).ToList()
                : new List<LanguageProfile>();

            // Header row (static, single-instance) — only rebuild on language changes
            var swHeader = Stopwatch.StartNew();
            if (langCountChanged || _tableBody.childCount == 0)
            {
                _tableBody.Clear();
                var headerRow = MakeRow(true);
                headerRow.Add(MakeCell("Key", 200, true));
                headerRow.Add(MakeCell("File", 60, true));

                foreach (var lang in _cachedLanguages)
                    headerRow.Add(MakeCell(lang.Code + " — " + lang.displayName, 180, true));

                _tableBody.Add(headerRow);
            }
            swHeader.Stop();

            // Build the flat row model from the tree
            var swFlat = Stopwatch.StartNew();
            BuildFlatItems();
            swFlat.Stop();

            var swListRebuild = Stopwatch.StartNew();
            // Use RefreshItems instead of Rebuild — only re-binds existing pooled elements
            // (Rebuild() is the slow one because it discards and re-creates the entire row pool)
            if (langCountChanged)
            {
                _listView.itemsSource = _flatItems;
                _listView.Rebuild();
            }
            else
            {
                _listView.itemsSource = _flatItems;
                _listView.RefreshItems();
            }
            swListRebuild.Stop();

            var swStatus = Stopwatch.StartNew();
            UpdateStatus();
            swStatus.Stop();

            swTotal.Stop();
            Debug.Log($"[SL.Perf] RebuildTable: total={swTotal.ElapsedMilliseconds}ms | header={swHeader.ElapsedMilliseconds}ms | BuildFlatItems={swFlat.ElapsedMilliseconds}ms | ListView refresh={swListRebuild.ElapsedMilliseconds}ms | UpdateStatus={swStatus.ElapsedMilliseconds}ms | rows={_flatItems.Count} | langs={_cachedLanguages.Count}");
        }

        /// <summary>
        /// Lightweight refresh: rebuild only visible rows. Used after edits.
        /// </summary>
        private void RefreshVisible()
        {
            if (_listView == null) return;
            _listView.RefreshItems();
            UpdateStatus();
        }

        private void BuildFlatItems()
        {
            var swFilter = Stopwatch.StartNew();
            _flatItems.Clear();
            _flatVisibleKeys = new List<string>();

            // Use the cached pre-sorted keys list (instant)
            var keys = GetFilteredKeysList();
            swFilter.Stop();

            var swTree = Stopwatch.StartNew();
            // Build tree structure. Since input is already sorted, child Keys lists are sorted too.
            var rootNode = new TreeNode("(root)");

            foreach (var key in keys)
            {
                int slashIdx = key.IndexOf('/');
                if (slashIdx < 0)
                {
                    rootNode.Keys.Add(key);
                    continue;
                }

                var current = rootNode;
                int start = 0;

                while (slashIdx >= 0)
                {
                    string segment = key.Substring(start, slashIdx - start);
                    if (!current.Children.TryGetValue(segment, out var child))
                    {
                        child = new TreeNode(segment);
                        current.Children[segment] = child;
                    }

                    current = child;
                    current.TotalKeyCount++; // pre-count during insertion
                    start = slashIdx + 1;
                    slashIdx = key.IndexOf('/', start);
                }

                current.Keys.Add(key);
            }
            swTree.Stop();

            var swFlatten = Stopwatch.StartNew();
            // Flatten tree into _flatItems, respecting collapsed state
            FlattenTreeNode(rootNode, 0, true);
            swFlatten.Stop();

            Debug.Log($"[SL.Perf]   BuildFlatItems internals: GetFilteredKeys={swFilter.ElapsedMilliseconds}ms | BuildTree={swTree.ElapsedMilliseconds}ms | Flatten={swFlatten.ElapsedMilliseconds}ms | keys={keys.Count}");
        }

        private void FlattenTreeNode(TreeNode node, int depth, bool isRoot)
        {
            // Sub-groups (already in insertion order; Dictionary preserves it in .NET Core 3+ and Unity Mono)
            // Sort once into a temp array because Dictionary order is not strictly guaranteed across versions.
            // For perf: only sort if there are children, and use a small allocation.
            if (node.Children.Count > 0)
            {
                // Build a sorted array of children by name
                var sortedChildren = new KeyValuePair<string, TreeNode>[node.Children.Count];
                int idx = 0;
                foreach (var kvp in node.Children) sortedChildren[idx++] = kvp;
                System.Array.Sort(sortedChildren, (a, b) => string.CompareOrdinal(a.Key, b.Key));

                foreach (var childKvp in sortedChildren)
                {
                    string childName = childKvp.Key;
                    var childNode = childKvp.Value;

                    string fullPath = isRoot ? childName : $"{node.FullPath}/{childName}";
                    childNode.FullPath = fullPath;

                    bool collapsed = _collapsedGroups.Contains(fullPath);

                    _flatItems.Add(new RowItem
                    {
                        Type = RowType.GroupHeader,
                        Depth = depth,
                        GroupPath = fullPath,
                        GroupDisplayName = childName,
                        GroupKeyCount = childNode.TotalKeyCount,
                        IsCollapsed = collapsed
                    });

                    if (!collapsed)
                        FlattenTreeNode(childNode, depth + 1, false);
                }
            }

            // Then leaf keys at this level (already sorted because input was sorted)
            foreach (var key in node.Keys)
            {
                _flatItems.Add(new RowItem
                {
                    Type = RowType.KeyRow,
                    Depth = depth,
                    Key = key
                });
                _flatVisibleKeys.Add(key);
            }
        }

        // ──────────────────────────────────────────────
        //  ListView item factory
        // ──────────────────────────────────────────────

        /// <summary>
        /// Creates one reusable row VisualElement. Both row types (group header and key row)
        /// share a single root container. Visibility of inner children is toggled in BindListItem.
        /// </summary>
        private VisualElement MakeListItem()
        {
            var root = new VisualElement();
            root.style.flexDirection = FlexDirection.Row;
            root.style.alignItems = Align.FlexStart;
            root.style.borderBottomWidth = 1;
            root.style.borderBottomColor = new Color(0, 0, 0, 0.08f);
            root.style.paddingTop = 2;
            root.style.paddingBottom = 2;

            // ── Group header container ──
            var groupContainer = new VisualElement();
            groupContainer.name = "group-container";
            groupContainer.style.flexDirection = FlexDirection.Row;
            groupContainer.style.flexGrow = 1;
            groupContainer.style.paddingTop = 4;
            groupContainer.style.paddingBottom = 4;
            groupContainer.style.display = DisplayStyle.None;

            var groupLabel = new Label();
            groupLabel.name = "group-label";
            groupLabel.style.fontSize = 11;
            groupLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            groupLabel.style.color = new Color(0.4f, 0.4f, 0.4f);
            groupContainer.Add(groupLabel);

            root.Add(groupContainer);

            // ── Key row container ──
            var keyContainer = new VisualElement();
            keyContainer.name = "key-container";
            keyContainer.style.flexDirection = FlexDirection.Row;
            keyContainer.style.flexGrow = 1;
            keyContainer.style.display = DisplayStyle.None;

            // Key cell (label + optional description)
            var keyCell = new VisualElement();
            keyCell.name = "key-cell";
            keyCell.style.width = 200;

            var keyLabel = new Label();
            keyLabel.name = "key-label";
            keyLabel.style.fontSize = 11;
            keyLabel.style.paddingTop = 2;
            keyLabel.style.paddingBottom = 2;
            keyCell.Add(keyLabel);

            var descLabel = new Label();
            descLabel.name = "desc-label";
            descLabel.style.fontSize = 9;
            descLabel.style.color = new Color(0.5f, 0.5f, 0.5f, 0.7f);
            descLabel.style.marginTop = -2;
            descLabel.style.whiteSpace = WhiteSpace.Normal;
            descLabel.style.maxWidth = 190;
            descLabel.style.display = DisplayStyle.None;
            keyCell.Add(descLabel);

            keyContainer.Add(keyCell);

            // File cell
            var fileLabel = new Label();
            fileLabel.name = "file-label";
            fileLabel.style.width = 60;
            fileLabel.style.fontSize = 10;
            fileLabel.style.color = new Color(0.6f, 0.6f, 0.6f);
            fileLabel.style.paddingTop = 2;
            keyContainer.Add(fileLabel);

            // Translation fields container
            var fieldsContainer = new VisualElement();
            fieldsContainer.name = "fields-container";
            fieldsContainer.style.flexDirection = FlexDirection.Row;

            // Pre-create translation fields for max possible languages (up to 8)
            // bindItem will hide the unused ones based on _cachedLanguages.Count
            for (int i = 0; i < 8; i++)
            {
                var field = new TextField();
                field.name = $"trans-field-{i}";
                field.style.width = 180;
                field.style.fontSize = 11;
                field.multiline = true;
                field.style.display = DisplayStyle.None;

                int capturedFieldIdx = i;

                field.RegisterCallback<AttachToPanelEvent>(_ =>
                {
                    var textInput = field.Q("unity-text-input");
                    if (textInput != null)
                    {
                        textInput.style.whiteSpace = WhiteSpace.Normal;
                        textInput.style.overflow = Overflow.Hidden;
                    }
                });

                // FocusOut writes the value back to data and pushes an undo entry
                field.RegisterCallback<FocusOutEvent>(_ =>
                {
                    OnFieldFocusOut(field, capturedFieldIdx);
                });

                // TAB / Shift+TAB navigation between fields and rows
                field.RegisterCallback<KeyDownEvent>(evt =>
                {
                    HandleFieldKeyDown(evt, field, capturedFieldIdx);
                });

                fieldsContainer.Add(field);
            }

            keyContainer.Add(fieldsContainer);
            root.Add(keyContainer);

            // Click handlers (selection + context menu) on the key cell only
            keyCell.AddManipulator(new ClickWithModifiers(evt => HandleKeyClick(root, evt)));
            keyCell.RegisterCallback<ContextClickEvent>(evt => OnKeyContextClick(root, evt));

            // Group click handler (collapse/expand)
            groupContainer.AddManipulator(new ClickWithModifiers(_ =>
            {
                var item = root.userData as RowItem;
                if (item == null || item.Type != RowType.GroupHeader) return;

                if (_collapsedGroups.Contains(item.GroupPath))
                    _collapsedGroups.Remove(item.GroupPath);
                else
                    _collapsedGroups.Add(item.GroupPath);

                RebuildTable();
            }));

            return root;
        }

        /// <summary>
        /// Binds a RowItem (data) to a reused row VisualElement.
        /// </summary>
        private void BindListItem(VisualElement element, int index)
        {
            if (index < 0 || index >= _flatItems.Count) return;

            var item = _flatItems[index];
            element.userData = item;

            var groupContainer = element.Q("group-container");
            var keyContainer = element.Q("key-container");

            if (item.Type == RowType.GroupHeader)
            {
                groupContainer.style.display = DisplayStyle.Flex;
                keyContainer.style.display = DisplayStyle.None;
                element.style.backgroundColor = new Color(0, 0, 0, 0.04f);
                groupContainer.style.paddingLeft = 8 + item.Depth * 16;

                var groupLabel = element.Q<Label>("group-label");
                string arrow = item.IsCollapsed ? "\u25b6" : "\u25bc";
                groupLabel.text = $"{arrow} {item.GroupDisplayName} ({item.GroupKeyCount})";
            }
            else
            {
                groupContainer.style.display = DisplayStyle.None;
                keyContainer.style.display = DisplayStyle.Flex;
                BindKeyRow(element, item);
            }
        }

        private void BindKeyRow(VisualElement element, RowItem item)
        {
            string key = item.Key;
            string shortKey = key.Substring(key.LastIndexOf('/') + 1);
            string sourceFile = _data.GetFileForKey(key) ?? "";
            bool isSelected = _selectedKeys.Contains(key);

            element.style.backgroundColor = isSelected
                ? new Color(0.2f, 0.5f, 0.9f, 0.08f)
                : new StyleColor(StyleKeyword.Null);

            // Key label
            var keyCell = element.Q("key-cell");
            keyCell.style.width = 200;
            keyCell.style.paddingLeft = 12 + item.Depth * 16;

            var keyLabel = element.Q<Label>("key-label");
            keyLabel.text = shortKey;
            keyLabel.tooltip = key;
            keyLabel.style.color = isSelected
                ? new Color(0.2f, 0.5f, 0.9f)
                : new Color(0.4f, 0.4f, 0.4f);

            // Description
            var descLabel = element.Q<Label>("desc-label");
            string desc = _data.GetDescription(key);

            if (!string.IsNullOrEmpty(desc))
            {
                descLabel.text = desc;
                descLabel.style.display = DisplayStyle.Flex;
            }
            else
            {
                descLabel.style.display = DisplayStyle.None;
            }

            // File label
            var fileLabel = element.Q<Label>("file-label");
            fileLabel.text = sourceFile;

            // Translation fields
            for (int i = 0; i < 8; i++)
            {
                var field = element.Q<TextField>($"trans-field-{i}");

                if (i >= _cachedLanguages.Count)
                {
                    field.style.display = DisplayStyle.None;
                    field.userData = null;
                    continue;
                }

                var lang = _cachedLanguages[i];
                string langCode = lang.Code;
                string value = _data.GetTranslation(key, langCode) ?? "";

                // Bind metadata for this row
                field.userData = new FieldBinding
                {
                    Key = key,
                    LanguageCode = langCode,
                    SourceFile = sourceFile
                };

                field.style.display = DisplayStyle.Flex;
                field.SetValueWithoutNotify(value);

                if (string.IsNullOrEmpty(value))
                    field.style.backgroundColor = new Color(0.9f, 0.3f, 0.3f, 0.1f);
                else
                    field.style.backgroundColor = new StyleColor(StyleKeyword.Null);

                // Estimate height for word wrap
                int charsPerLine = 22;
                int explicitNewlines = 0;
                for (int c = 0; c < value.Length; c++)
                    if (value[c] == '\n') explicitNewlines++;

                int wrappedLines = value.Length > 0
                    ? Mathf.CeilToInt((float)value.Length / charsPerLine)
                    : 1;
                int totalLines = Mathf.Max(1, wrappedLines + explicitNewlines);
                field.style.minHeight = Mathf.Max(20, totalLines * 15);
            }
        }

        /// <summary>
        /// Clears any per-row state when an element leaves the viewport so reused
        /// callbacks won't accidentally write into a stale row.
        /// </summary>
        private void UnbindListItem(VisualElement element, int index)
        {
            element.userData = null;

            for (int i = 0; i < 8; i++)
            {
                var field = element.Q<TextField>($"trans-field-{i}");
                if (field != null) field.userData = null;
            }
        }

        // ──────────────────────────────────────────────
        //  Selection & context menu
        // ──────────────────────────────────────────────

        private void HandleKeyClick(VisualElement rowElement, ClickWithModifiersEvent evt)
        {
            var item = rowElement.userData as RowItem;
            if (item == null || item.Type != RowType.KeyRow) return;

            string key = item.Key;

            if (evt.Shift && !string.IsNullOrEmpty(_lastClickedKey))
            {
                int idxA = _flatVisibleKeys.IndexOf(_lastClickedKey);
                int idxB = _flatVisibleKeys.IndexOf(key);

                if (idxA >= 0 && idxB >= 0)
                {
                    int from = Mathf.Min(idxA, idxB);
                    int to = Mathf.Max(idxA, idxB);

                    if (!evt.Ctrl)
                        _selectedKeys.Clear();

                    for (int i = from; i <= to; i++)
                        _selectedKeys.Add(_flatVisibleKeys[i]);
                }
            }
            else if (evt.Ctrl)
            {
                if (!_selectedKeys.Add(key))
                    _selectedKeys.Remove(key);
            }
            else
            {
                _selectedKeys.Clear();
                _selectedKeys.Add(key);
            }

            _lastClickedKey = key;
            RefreshVisible();
        }

        private void OnKeyContextClick(VisualElement rowElement, ContextClickEvent evt)
        {
            var item = rowElement.userData as RowItem;
            if (item == null || item.Type != RowType.KeyRow) return;

            string key = item.Key;

            if (!_selectedKeys.Contains(key))
            {
                _selectedKeys.Clear();
                _selectedKeys.Add(key);
                RefreshVisible();
            }

            ShowKeyContextMenu();
            evt.StopPropagation();
        }

        // ──────────────────────────────────────────────
        //  Edit + Undo/Redo
        // ──────────────────────────────────────────────

        private void OnFieldFocusOut(TextField field, int fieldIndex)
        {
            var binding = field.userData as FieldBinding;
            if (binding == null) return;

            string newValue = field.value;
            string oldValue = _data.GetTranslation(binding.Key, binding.LanguageCode) ?? "";

            if (newValue == oldValue) return;

            // Push undo entry
            PushUndo(new EditOperation
            {
                Key = binding.Key,
                LanguageCode = binding.LanguageCode,
                SourceFile = binding.SourceFile,
                OldValue = oldValue,
                NewValue = newValue
            });

            ApplyEdit(binding.Key, binding.LanguageCode, binding.SourceFile, newValue);
        }

        private void ApplyEdit(string key, string languageCode, string sourceFile, string value)
        {
            var swTotal = Stopwatch.StartNew();

            var swSet = Stopwatch.StartNew();
            _data.SetTranslation(key, languageCode, value);
            swSet.Stop();

            var swSave = Stopwatch.StartNew();
            _data.SaveFile(sourceFile, languageCode);
            swSave.Stop();

            var swRefresh = Stopwatch.StartNew();
            AssetDatabase.Refresh();
            swRefresh.Stop();

            var swInvalidate = Stopwatch.StartNew();
            EditorDataCache.Invalidate();
            swInvalidate.Stop();

            var swVisible = Stopwatch.StartNew();
            RefreshVisible();
            swVisible.Stop();

            swTotal.Stop();
            Debug.Log($"[SL.Perf] ApplyEdit: total={swTotal.ElapsedMilliseconds}ms | SetTranslation={swSet.ElapsedMilliseconds}ms | SaveFile={swSave.ElapsedMilliseconds}ms | AssetDatabase.Refresh={swRefresh.ElapsedMilliseconds}ms | Cache.Invalidate={swInvalidate.ElapsedMilliseconds}ms | RefreshVisible={swVisible.ElapsedMilliseconds}ms");
        }

        private void PushUndo(EditOperation op)
        {
            _undoStack.Push(op);
            _redoStack.Clear();

            // Trim history
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
            ApplyEdit(op.Key, op.LanguageCode, op.SourceFile, op.OldValue);
        }

        private void PerformRedo()
        {
            if (_redoStack.Count == 0) return;
            var op = _redoStack.Pop();
            _undoStack.Push(op);
            ApplyEdit(op.Key, op.LanguageCode, op.SourceFile, op.NewValue);
        }

        // ──────────────────────────────────────────────
        //  TAB navigation
        // ──────────────────────────────────────────────

        private void HandleFieldKeyDown(KeyDownEvent evt, TextField field, int fieldIndex)
        {
            if (evt.keyCode != KeyCode.Tab && evt.character != '\t')
                return;

            // Don't intercept TAB inside a multiline edit when Alt is held (let user insert literal tab)
            if (evt.altKey) return;

            bool reverse = evt.shiftKey;
            int langCount = _cachedLanguages.Count;
            if (langCount == 0) return;

            // Find current row in flat items via field's binding
            var binding = field.userData as FieldBinding;
            if (binding == null) return;

            int currentRowIdx = -1;
            for (int i = 0; i < _flatItems.Count; i++)
            {
                if (_flatItems[i].Type == RowType.KeyRow && _flatItems[i].Key == binding.Key)
                {
                    currentRowIdx = i;
                    break;
                }
            }

            if (currentRowIdx < 0) return;

            int nextField = reverse ? fieldIndex - 1 : fieldIndex + 1;

            if (nextField >= 0 && nextField < langCount)
            {
                // Same row, neighboring field
                var rowElement = field.GetFirstAncestorOfType<VisualElement>();
                // Walk up until we find an element whose userData is a RowItem
                while (rowElement != null && !(rowElement.userData is RowItem))
                    rowElement = rowElement.parent;

                if (rowElement != null)
                {
                    var target = rowElement.Q<TextField>($"trans-field-{nextField}");
                    if (target != null)
                    {
                        target.Focus();
                        evt.StopPropagation();
                    }
                }
            }
            else
            {
                // Move to neighboring row — first or last field
                int targetFieldIdx = reverse ? langCount - 1 : 0;
                FindFieldInAdjacentRow(currentRowIdx, reverse, targetFieldIdx);
                evt.StopPropagation();
            }
        }

        private void FindFieldInAdjacentRow(int currentRowIdx, bool reverse, int targetFieldIdx)
        {
            int step = reverse ? -1 : 1;
            int idx = currentRowIdx + step;

            // Skip group headers
            while (idx >= 0 && idx < _flatItems.Count && _flatItems[idx].Type != RowType.KeyRow)
                idx += step;

            if (idx < 0 || idx >= _flatItems.Count) return;

            // Scroll the target row into view, then focus its field on the next frame
            _listView.ScrollToItem(idx);

            int targetIdx = idx;
            _listView.schedule.Execute(() =>
            {
                var rowElement = FindRowElementByIndex(targetIdx);
                if (rowElement == null) return;

                var target = rowElement.Q<TextField>($"trans-field-{targetFieldIdx}");
                if (target != null && target.style.display.value == DisplayStyle.Flex)
                    target.Focus();
            }).StartingIn(20);
        }

        private VisualElement FindRowElementByIndex(int index)
        {
            // ListView's content container holds rendered rows; we walk it to find one whose userData matches
            if (index < 0 || index >= _flatItems.Count) return null;
            var target = _flatItems[index];

            var scrollView = _listView.Q<ScrollView>();
            if (scrollView == null) return null;

            foreach (var child in scrollView.contentContainer.Children())
            {
                if (ReferenceEquals(child.userData, target))
                    return child;
            }

            return null;
        }

        // ──────────────────────────────────────────────
        //  Tree/row support types
        // ──────────────────────────────────────────────

        private static int CountKeysRecursive(TreeNode node)
        {
            int count = node.Keys.Count;

            foreach (var child in node.Children.Values)
                count += CountKeysRecursive(child);

            return count;
        }

        private enum RowType { GroupHeader, KeyRow }

        private class RowItem
        {
            public RowType Type;
            public int Depth;

            // KeyRow data
            public string Key;

            // GroupHeader data
            public string GroupPath;
            public string GroupDisplayName;
            public int GroupKeyCount;
            public bool IsCollapsed;
        }

        private class FieldBinding
        {
            public string Key;
            public string LanguageCode;
            public string SourceFile;
        }

        private class EditOperation
        {
            public string Key;
            public string LanguageCode;
            public string SourceFile;
            public string OldValue;
            public string NewValue;
        }

        private class TreeNode
        {
            public string Name;
            public string FullPath;
            public Dictionary<string, TreeNode> Children = new();
            public List<string> Keys = new();
            public int TotalKeyCount;

            public TreeNode(string name) { Name = name; FullPath = name; }
        }

        // Custom event that captures modifier keys at click time
        private class ClickWithModifiersEvent
        {
            public bool Ctrl;
            public bool Shift;
            public bool Alt;
        }

        private class ClickWithModifiers : Manipulator
        {
            private readonly System.Action<ClickWithModifiersEvent> _onClick;

            public ClickWithModifiers(System.Action<ClickWithModifiersEvent> onClick)
            {
                _onClick = onClick;
            }

            protected override void RegisterCallbacksOnTarget()
            {
                target.RegisterCallback<MouseDownEvent>(OnMouseDown);
            }

            protected override void UnregisterCallbacksFromTarget()
            {
                target.UnregisterCallback<MouseDownEvent>(OnMouseDown);
            }

            private void OnMouseDown(MouseDownEvent evt)
            {
                if (evt.button != 0) return;

                _onClick?.Invoke(new ClickWithModifiersEvent
                {
                    Ctrl = evt.ctrlKey || evt.commandKey,
                    Shift = evt.shiftKey,
                    Alt = evt.altKey
                });

                evt.StopPropagation();
            }
        }

        /// <summary>
        /// Returns a fully-materialized List of keys after applying file filter and search.
        /// Uses the cached sorted-keys list and pre-built search index for speed.
        /// </summary>
        private List<string> GetFilteredKeysList()
        {
            // Start from the pre-sorted, cached key list
            IReadOnlyList<string> source;

            if (_viewMode == ViewMode.All || _selectedFiles.Count == 0)
            {
                source = _data.AllKeysSorted;
            }
            else
            {
                // File filter — build sorted subset
                var subset = new List<string>();
                foreach (var k in _data.AllKeysSorted)
                {
                    var f = _data.GetFileForKey(k);
                    if (f != null && _selectedFiles.Contains(f)) subset.Add(k);
                }
                source = subset;
            }

            // Apply search using the pre-built haystack index
            if (string.IsNullOrEmpty(_searchQuery))
            {
                // Avoid extra allocation when source IS the cache
                return source is List<string> list ? list : new List<string>(source);
            }

            string q = _searchQuery.ToLowerInvariant();
            var result = new List<string>(source.Count);

            for (int i = 0; i < source.Count; i++)
            {
                var k = source[i];
                if (_data.GetSearchHaystack(k).Contains(q))
                    result.Add(k);
            }

            return result;
        }

        private IEnumerable<string> GetFilteredKeys()
        {
            return GetFilteredKeysList();
        }

        private void UpdateStatus()
        {
            if (_statusLabel == null) return;

            var sw = Stopwatch.StartNew();

            // Materialize ONCE — reuse the same list for total count and missing iteration
            var filteredKeys = GetFilteredKeysList();
            int totalKeys = filteredKeys.Count;
            int fileCount = _viewMode == ViewMode.All
                ? _data.SourceFiles.Count
                : _selectedFiles.Count;

            int missing = 0;

            if (_config != null)
            {
                for (int i = 0; i < filteredKeys.Count; i++)
                {
                    var key = filteredKeys[i];
                    foreach (var profile in _config.languages)
                    {
                        if (profile == null) continue;
                        string val = _data.GetTranslation(key, profile.Code);
                        if (string.IsNullOrEmpty(val)) missing++;
                    }
                }
            }

            string missingText = missing > 0 ? $"  |  {missing} missing" : "";
            string selText = _selectedKeys.Count > 0 ? $"  |  {_selectedKeys.Count} selected" : "";
            _statusLabel.text = $"{totalKeys} keys from {fileCount} file(s){missingText}{selText}";

            sw.Stop();
            if (sw.ElapsedMilliseconds > 5)
                Debug.Log($"[SL.Perf] UpdateStatus: {sw.ElapsedMilliseconds}ms | totalKeys={totalKeys} | missing={missing}");
        }

        private VisualElement MakeRow(bool isHeader)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.borderBottomWidth = 1;
            row.style.borderBottomColor = new Color(0, 0, 0, 0.08f);
            row.style.paddingTop = 2;
            row.style.paddingBottom = 2;

            if (isHeader)
                row.style.backgroundColor = new Color(0, 0, 0, 0.04f);

            return row;
        }

        private VisualElement MakeCell(string text, int width, bool isHeader)
        {
            var label = new Label(text);
            label.style.width = width;
            label.style.fontSize = isHeader ? 11 : 12;
            label.style.paddingLeft = 8;
            label.style.paddingTop = 4;
            label.style.paddingBottom = 4;

            if (isHeader)
            {
                label.style.unityFontStyleAndWeight = FontStyle.Bold;
                label.style.color = new Color(0.5f, 0.5f, 0.5f);
            }

            return label;
        }

        // ──────────────────────────────────────────────
        //  Actions
        // ──────────────────────────────────────────────

        private void OnFileClicked(string fileName)
        {
            if (_viewMode == ViewMode.Single)
            {
                _selectedFiles.Clear();
                _selectedFiles.Add(fileName);
            }
            else if (_viewMode == ViewMode.Multi)
            {
                if (_selectedFiles.Contains(fileName))
                    _selectedFiles.Remove(fileName);
                else
                    _selectedFiles.Add(fileName);
            }
            else
            {
                _viewMode = ViewMode.Single;
                _selectedFiles.Clear();
                _selectedFiles.Add(fileName);
            }

            RefreshAll();
        }

        private void OnAddKeyClicked()
        {
            var popup = ScriptableObject.CreateInstance<AddKeyPopup>();

            string defaultFile = _selectedFiles.Count == 1
                ? _selectedFiles.First()
                : (_data.SourceFiles.Count > 0 ? _data.SourceFiles[0] : "global");

            popup.Init(_data.SourceFiles.ToList(), defaultFile, (key, file) =>
            {
                _data.AddKey(key, file);
                _data.SaveFileAllLanguages(file);
                AssetDatabase.Refresh();
                EditorDataCache.Invalidate();
                RebuildTable();
            }, _data);

            var rect = GUIUtility.GUIToScreenRect(new Rect(Event.current?.mousePosition ?? Vector2.zero, Vector2.zero));
            popup.ShowAsDropDown(rect, new Vector2(350, 100));
        }

        private void OnNewFileClicked()
        {
            var popup = ScriptableObject.CreateInstance<NewFilePopup>();

            popup.Init(_data, _config, () =>
            {
                _window.FullRefresh();
            });

            var rect = GUIUtility.GUIToScreenRect(new Rect(Vector2.zero, Vector2.zero));
            popup.ShowAsDropDown(rect, new Vector2(300, 80));
        }

        private void ShowKeyContextMenu()
        {
            var menu = new GenericMenu();
            var keys = _selectedKeys.ToList();
            bool isSingle = keys.Count == 1;
            string label = isSingle ? keys[0] : $"{keys.Count} keys";

            // Copy
            if (isSingle)
            {
                menu.AddItem(new GUIContent("Copy key"), false, () =>
                {
                    EditorGUIUtility.systemCopyBuffer = keys[0];
                });
            }
            else
            {
                menu.AddItem(new GUIContent($"Copy {keys.Count} keys"), false, () =>
                {
                    EditorGUIUtility.systemCopyBuffer = string.Join("\n", keys);
                });
            }

            menu.AddSeparator("");

            // Move to file
            foreach (var file in _data.SourceFiles)
            {
                string f = file;

                menu.AddItem(new GUIContent($"Move to/{f}.json"), false, () =>
                {
                    var affectedFiles = new HashSet<string>();

                    foreach (var key in keys)
                    {
                        string oldFile = _data.GetFileForKey(key);

                        if (oldFile == f) continue;

                        _data.MoveKeyToFile(key, f);

                        if (!string.IsNullOrEmpty(oldFile))
                            affectedFiles.Add(oldFile);
                    }

                    // Save all affected source files + target
                    affectedFiles.Add(f);

                    foreach (var af in affectedFiles)
                        _data.SaveFileAllLanguages(af);

                    AssetDatabase.Refresh();
                    EditorDataCache.Invalidate();
                    _selectedKeys.Clear();
                    RebuildTable();
                });
            }

            menu.AddSeparator("");

            // Delete
            menu.AddItem(new GUIContent(isSingle
                ? "Delete key"
                : $"Delete {keys.Count} keys"), false, () =>
            {
                if (!EditorUtility.DisplayDialog("Delete keys",
                    $"Delete {label} from all languages?", "Delete", "Cancel"))
                    return;

                var affectedFiles = new HashSet<string>();

                foreach (var key in keys)
                {
                    string file = _data.GetFileForKey(key);
                    _data.RemoveKey(key);

                    if (!string.IsNullOrEmpty(file))
                        affectedFiles.Add(file);
                }

                foreach (var af in affectedFiles)
                    _data.SaveFileAllLanguages(af);

                AssetDatabase.Refresh();
                EditorDataCache.Invalidate();
                _selectedKeys.Clear();
                RebuildTable();
            });

            // Single-key operations
            if (isSingle)
            {
                menu.AddSeparator("");

                // Rename key
                menu.AddItem(new GUIContent("Rename key"), false, () =>
                {
                    var popup = ScriptableObject.CreateInstance<RenameKeyPopup>();
                    popup.Init(keys[0], _data, _config, () =>
                    {
                        _selectedKeys.Clear();
                        EditorDataCache.Invalidate();
                        // Force all Inspector windows to repaint with updated key
                        UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
                        _window.FullRefresh();
                    });
                    popup.titleContent = new GUIContent("Rename key");
                    popup.ShowUtility();
                    popup.position = new Rect(
                        Screen.width / 2f - 175, Screen.height / 2f - 60, 350, 150);
                });

                // Edit description
                string currentDesc = _data.GetDescription(keys[0]) ?? "";

                menu.AddItem(new GUIContent(
                    string.IsNullOrEmpty(currentDesc) ? "Add description" : "Edit description"),
                    false, () =>
                {
                    var popup = ScriptableObject.CreateInstance<EditDescriptionPopup>();
                    popup.Init(keys[0], currentDesc, _data, () =>
                    {
                        _data.SaveMeta();
                        EditorDataCache.Invalidate();
                        RebuildTable();
                    });
                    popup.titleContent = new GUIContent("Key description");
                    popup.ShowUtility();
                    popup.position = new Rect(
                        Screen.width / 2f - 175, Screen.height / 2f - 80, 350, 200);
                });
            }

            menu.ShowAsContext();
        }

        private void RefreshAll()
        {
            EditorDataCache.Invalidate();
            _window.RefreshCurrentTab();
        }

        private void ShowFileContextMenu(string fileName)
        {
            var menu = new GenericMenu();

            // Delete file — migrate keys to global
            bool isOnlyFile = _data.SourceFiles.Count <= 1;

            if (isOnlyFile)
            {
                menu.AddDisabledItem(new GUIContent("Cannot delete the only file"));
            }
            else
            {
                menu.AddItem(new GUIContent("Delete file (move keys to global)"), false, () =>
                {
                    int keyCount = _data.GetKeysForFile(fileName).Count();

                    if (!EditorUtility.DisplayDialog("Delete file",
                        $"Delete '{fileName}.json' and move {keyCount} key(s) to 'global'?\n" +
                        "JSON files will be deleted from all language folders.",
                        "Delete & migrate", "Cancel"))
                        return;

                    // Ensure global exists
                    if (!_data.SourceFiles.Contains("global"))
                        _data.CreateSourceFile("global");

                    // Move all keys to global
                    var keysToMove = _data.GetKeysForFile(fileName).ToList();

                    foreach (var key in keysToMove)
                        _data.MoveKeyToFile(key, "global");

                    // Save global for all languages
                    _data.SaveFileAllLanguages("global");

                    // Delete the file from all language folders
                    if (!string.IsNullOrEmpty(_data.BasePath))
                    {
                        foreach (var langCode in _data.LoadedLanguages)
                        {
                            string filePath = System.IO.Path.Combine(
                                _data.BasePath, langCode, "text", fileName + ".json");

                            if (System.IO.File.Exists(filePath))
                                System.IO.File.Delete(filePath);

                            string metaPath = filePath + ".meta";

                            if (System.IO.File.Exists(metaPath))
                                System.IO.File.Delete(metaPath);
                        }
                    }

                    _selectedFiles.Remove(fileName);
                    AssetDatabase.Refresh();
                    _window.FullRefresh();
                });
            }

            menu.ShowAsContext();
        }
    }

    // ──────────────────────────────────────────────
    //  Small popup windows
    // ──────────────────────────────────────────────

    public class AddKeyPopup : EditorWindow
    {
        private List<string> _files;
        private string _selectedFile;
        private string _keyName = "";
        private System.Action<string, string> _onAdd;
        private EditorLocalizationData _data;

        public void Init(List<string> files, string defaultFile, System.Action<string, string> onAdd,
            EditorLocalizationData data = null)
        {
            _files = files;
            _selectedFile = defaultFile;
            _onAdd = onAdd;
            _data = data;
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(4);

            bool isDuplicate = _data != null
                && !string.IsNullOrEmpty(_keyName)
                && _data.GetFileForKey(_keyName) != null;

            Color prev = GUI.color;

            if (isDuplicate)
                GUI.color = new Color(1f, 0.35f, 0.35f);

            _keyName = EditorGUILayout.TextField("Key", _keyName);
            GUI.color = prev;

            if (isDuplicate)
            {
                EditorGUILayout.HelpBox(
                    $"Key '{_keyName}' already exists in {_data.GetFileForKey(_keyName)}.json",
                    MessageType.Error);
            }

            if (_files != null && _files.Count > 0)
            {
                int idx = Mathf.Max(0, _files.IndexOf(_selectedFile));
                idx = EditorGUILayout.Popup("File", idx, _files.ToArray());
                _selectedFile = _files[idx];
            }

            EditorGUILayout.Space(4);

            bool canAdd = !string.IsNullOrEmpty(_keyName) && !isDuplicate;
            GUI.enabled = canAdd;

            if (GUILayout.Button("Add"))
            {
                _onAdd?.Invoke(_keyName, _selectedFile);
                Close();
            }

            GUI.enabled = true;
        }
    }

    public class NewFilePopup : EditorWindow
    {
        private EditorLocalizationData _data;
        private LocalizationConfig _config;
        private System.Action _onCreated;
        private string _fileName = "";

        public void Init(EditorLocalizationData data, LocalizationConfig config, System.Action onCreated)
        {
            _data = data;
            _config = config;
            _onCreated = onCreated;
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(4);
            _fileName = EditorGUILayout.TextField("File name", _fileName);

            bool exists = _data.SourceFiles.Contains(_fileName);

            if (exists)
                EditorGUILayout.HelpBox("File already exists.", MessageType.Error);

            EditorGUILayout.Space(4);

            GUI.enabled = !string.IsNullOrEmpty(_fileName) && !exists;

            if (GUILayout.Button("Create"))
            {
                _data.CreateSourceFile(_fileName);

                foreach (var profile in _config.languages)
                {
                    if (profile == null) continue;
                    _data.SaveFile(_fileName, profile.Code);
                }

                AssetDatabase.Refresh();
                _onCreated?.Invoke();
                Close();
            }

            GUI.enabled = true;
        }
    }

    public class RenameKeyPopup : EditorWindow
    {
        private string _oldKey;
        private string _newKey;
        private EditorLocalizationData _data;
        private LocalizationConfig _config;
        private System.Action _onRenamed;
        private bool _updateReferences = true;

        public void Init(string oldKey, EditorLocalizationData data, LocalizationConfig config,
            System.Action onRenamed)
        {
            _oldKey = oldKey;
            _newKey = oldKey;
            _data = data;
            _config = config;
            _onRenamed = onRenamed;
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(4);

            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.TextField("Current key", _oldKey);

            _newKey = EditorGUILayout.TextField("New key", _newKey);

            bool isDuplicate = _newKey != _oldKey && _data.GetFileForKey(_newKey) != null;
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
                string file = _data.GetFileForKey(_oldKey);
                _data.RenameKey(_oldKey, _newKey);

                if (!string.IsNullOrEmpty(file))
                    _data.SaveFileAllLanguages(file);

                _data.SaveMeta();

                int updatedRefs = 0;

                if (_updateReferences)
                {
                    updatedRefs = Utilities.KeyReferenceUpdater.UpdateReferences(_oldKey, _newKey);
                }

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                if (_updateReferences && updatedRefs > 0)
                {
                    EditorUtility.DisplayDialog("Key renamed",
                        $"Renamed '{_oldKey}' → '{_newKey}'\n" +
                        $"Updated {updatedRefs} component reference(s) in scenes/prefabs.",
                        "OK");
                }

                _onRenamed?.Invoke();
                Close();
            }

            GUI.enabled = true;
        }
    }

    public class EditDescriptionPopup : EditorWindow
    {
        private string _key;
        private string _description;
        private EditorLocalizationData _data;
        private System.Action _onSaved;
        private Vector2 _scroll;

        public void Init(string key, string currentDescription, EditorLocalizationData data,
            System.Action onSaved)
        {
            _key = key;
            _description = currentDescription ?? "";
            _data = data;
            _onSaved = onSaved;
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(4);

            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.TextField("Key", _key);

            EditorGUILayout.LabelField("Description");

            var textAreaStyle = new GUIStyle(EditorStyles.textArea)
            {
                wordWrap = true,
                fontSize = 12
            };

            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.MinHeight(80));
            _description = EditorGUILayout.TextArea(_description, textAreaStyle,
                GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(4);

            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Save"))
            {
                _data.SetDescription(_key, _description);
                _onSaved?.Invoke();
                Close();
            }

            if (GUILayout.Button("Clear"))
            {
                _data.SetDescription(_key, null);
                _onSaved?.Invoke();
                Close();
            }

            EditorGUILayout.EndHorizontal();
        }
    }
}