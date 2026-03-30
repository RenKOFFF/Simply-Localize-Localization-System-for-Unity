using System.Collections.Generic;
using System.Linq;
using SimplyLocalize.Editor.Data;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

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
                RebuildTable();
            });
            toolbar.Add(searchField);

            var addKeyBtn = new Button(OnAddKeyClicked);
            addKeyBtn.text = "+ Add key";
            addKeyBtn.style.fontSize = 11;
            addKeyBtn.style.marginLeft = 6;
            toolbar.Add(addKeyBtn);

            content.Add(toolbar);

            // Scroll view for the table
            var scrollView = new ScrollView(ScrollViewMode.VerticalAndHorizontal);
            scrollView.style.flexGrow = 1;

            _tableBody = new VisualElement();
            RebuildTable();
            scrollView.Add(_tableBody);

            content.Add(scrollView);

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
        //  Table
        // ──────────────────────────────────────────────

        private void RebuildTable()
        {
            if (_tableBody == null) return;
            _tableBody.Clear();

            var languages = _config != null
                ? _config.languages.Where(p => p != null).ToList()
                : new List<LanguageProfile>();

            // Header row
            var headerRow = MakeRow(true);
            headerRow.Add(MakeCell("Key", 200, true));
            headerRow.Add(MakeCell("File", 60, true));

            foreach (var lang in languages)
                headerRow.Add(MakeCell(lang.Code + " — " + lang.displayName, 180, true));

            _tableBody.Add(headerRow);

            // Collect keys
            var keys = GetFilteredKeys().ToList();

            // Group by first path segment
            var groups = keys
                .GroupBy(k =>
                {
                    int lastSlash = k.LastIndexOf('/');
                    return lastSlash > 0 ? k.Substring(0, lastSlash) : "(root)";
                })
                .OrderBy(g => g.Key)
                .ToList();

            foreach (var group in groups)
            {
                string groupName = group.Key;
                int count = group.Count();
                bool collapsed = _collapsedGroups.Contains(groupName);

                // Group header
                var groupRow = new VisualElement();
                groupRow.style.flexDirection = FlexDirection.Row;
                groupRow.style.backgroundColor = new Color(0, 0, 0, 0.04f);
                groupRow.style.borderBottomWidth = 1;
                groupRow.style.borderBottomColor = new Color(0, 0, 0, 0.08f);
                groupRow.style.paddingTop = 4;
                groupRow.style.paddingBottom = 4;
                groupRow.style.paddingLeft = 8;
                groupRow.style.cursor = StyleKeyword.Auto;

                var groupLabel = new Label($"{(collapsed ? "▶" : "▼")} {groupName} ({count})");
                groupLabel.style.fontSize = 11;
                groupLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                groupLabel.style.color = new Color(0.4f, 0.4f, 0.4f);

                groupRow.Add(groupLabel);

                groupRow.RegisterCallback<ClickEvent>(evt =>
                {
                    if (collapsed) _collapsedGroups.Remove(groupName);
                    else _collapsedGroups.Add(groupName);
                    RebuildTable();
                });

                _tableBody.Add(groupRow);

                if (collapsed) continue;

                // Key rows
                foreach (var key in group.OrderBy(k => k))
                {
                    string shortKey = key.Substring(key.LastIndexOf('/') + 1);
                    string sourceFile = _data.GetFileForKey(key) ?? "";
                    bool isSelected = _selectedKeys.Contains(key);

                    var row = MakeRow(false);

                    if (isSelected)
                        row.style.backgroundColor = new Color(0.2f, 0.5f, 0.9f, 0.08f);

                    // Key cell — clickable for selection
                    var keyLabel = new Label(shortKey);
                    keyLabel.style.width = 200;
                    keyLabel.style.fontSize = 11;
                    keyLabel.style.paddingLeft = 20;
                    keyLabel.style.paddingTop = 2;
                    keyLabel.style.paddingBottom = 2;
                    keyLabel.style.color = isSelected
                        ? new Color(0.2f, 0.5f, 0.9f)
                        : new Color(0.4f, 0.4f, 0.4f);
                    keyLabel.tooltip = key;

                    // Click to select/deselect (Ctrl for multi-select)
                    string capturedKey = key;
                    keyLabel.RegisterCallback<ClickEvent>(evt =>
                    {
                        if (evt.ctrlKey || evt.commandKey)
                        {
                            // Toggle selection
                            if (!_selectedKeys.Add(capturedKey))
                                _selectedKeys.Remove(capturedKey);
                        }
                        else
                        {
                            // Single select
                            _selectedKeys.Clear();
                            _selectedKeys.Add(capturedKey);
                        }

                        RebuildTable();
                        evt.StopPropagation();
                    });

                    // Right-click context menu
                    keyLabel.RegisterCallback<ContextClickEvent>(evt =>
                    {
                        // If right-clicked key is not in selection, select it alone
                        if (!_selectedKeys.Contains(capturedKey))
                        {
                            _selectedKeys.Clear();
                            _selectedKeys.Add(capturedKey);
                            RebuildTable();
                        }

                        ShowKeyContextMenu();
                        evt.StopPropagation();
                    });

                    row.Add(keyLabel);

                    // File cell
                    var fileLabel = new Label(sourceFile);
                    fileLabel.style.width = 60;
                    fileLabel.style.fontSize = 10;
                    fileLabel.style.color = new Color(0.6f, 0.6f, 0.6f);
                    fileLabel.style.paddingTop = 2;
                    row.Add(fileLabel);

                    // Translation cells
                    foreach (var lang in languages)
                    {
                        string langCode = lang.Code;
                        string value = _data.GetTranslation(key, langCode) ?? "";

                        var field = new TextField();
                        field.value = value;
                        field.style.width = 180;
                        field.style.fontSize = 11;
                        field.multiline = true;

                        // Auto-grow height based on newlines
                        int lineCount = 1;
                        for (int c = 0; c < value.Length; c++)
                            if (value[c] == '\n') lineCount++;
                        field.style.minHeight = Mathf.Max(20, lineCount * 16);

                        if (string.IsNullOrEmpty(value))
                        {
                            field.style.backgroundColor = new Color(0.9f, 0.3f, 0.3f, 0.1f);
                        }

                        string newCapturedKey = key;
                        string capturedLang = langCode;
                        string capturedFile = sourceFile;

                        field.RegisterCallback<FocusOutEvent>(evt =>
                        {
                            string newValue = field.value;
                            string oldValue = _data.GetTranslation(newCapturedKey, capturedLang) ?? "";

                            if (newValue != oldValue)
                            {
                                _data.SetTranslation(newCapturedKey, capturedLang, newValue);
                                _data.SaveFile(capturedFile, capturedLang);
                                AssetDatabase.Refresh();
                                EditorDataCache.Invalidate();
                                UpdateStatus();
                            }
                        });

                        row.Add(field);
                    }

                    _tableBody.Add(row);
                }
            }

            UpdateStatus();
        }

        // ──────────────────────────────────────────────
        //  Helpers
        // ──────────────────────────────────────────────

        private IEnumerable<string> GetFilteredKeys()
        {
            IEnumerable<string> keys;

            if (_viewMode == ViewMode.All || _selectedFiles.Count == 0)
            {
                keys = _data.AllKeys;
            }
            else
            {
                keys = _data.GetKeysForFiles(_selectedFiles);
            }

            if (!string.IsNullOrEmpty(_searchQuery))
            {
                string q = _searchQuery.ToLowerInvariant();

                keys = keys.Where(k =>
                {
                    if (k.ToLowerInvariant().Contains(q))
                        return true;

                    foreach (var langCode in _data.LoadedLanguages)
                    {
                        string val = _data.GetTranslation(k, langCode);

                        if (val != null && val.ToLowerInvariant().Contains(q))
                            return true;
                    }

                    return false;
                });
            }

            return keys;
        }

        private void UpdateStatus()
        {
            if (_statusLabel == null) return;

            int totalKeys = GetFilteredKeys().Count();
            int fileCount = _viewMode == ViewMode.All
                ? _data.SourceFiles.Count
                : _selectedFiles.Count;

            int missing = 0;

            if (_config != null)
            {
                foreach (var key in GetFilteredKeys())
                {
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
}