using System.Collections.Generic;
using System.Linq;
using SimplyLocalize.Editor.Data;
using SimplyLocalize.Editor.Utilities;
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
        private string _searchQuery = "";

        private VisualElement _fileList;
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
            sidebar.style.width = 180;
            sidebar.style.borderRightWidth = 1;
            sidebar.style.borderRightColor = new Color(0, 0, 0, 0.15f);
            sidebar.style.paddingTop = 8;

            // Header
            var header = new Label("Files");
            header.style.fontSize = 11;
            header.style.color = new Color(0.5f, 0.5f, 0.5f);
            header.style.paddingLeft = 10;
            header.style.paddingBottom = 6;
            header.style.unityFontStyleAndWeight = FontStyle.Bold;
            sidebar.Add(header);

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

            sidebar.Add(allBtn);

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

                _fileList.Add(fileBtn);
            }

            sidebar.Add(_fileList);

            // Separator
            var sep = new VisualElement();
            sep.style.height = 1;
            sep.style.backgroundColor = new Color(0, 0, 0, 0.1f);
            sep.style.marginTop = 8;
            sep.style.marginBottom = 8;
            sep.style.marginLeft = 10;
            sep.style.marginRight = 10;
            sidebar.Add(sep);

            // View mode selector
            var modeLabel = new Label("View mode");
            modeLabel.style.fontSize = 10;
            modeLabel.style.color = new Color(0.5f, 0.5f, 0.5f);
            modeLabel.style.paddingLeft = 10;
            sidebar.Add(modeLabel);

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

            sidebar.Add(modeRow);

            // New file button
            var newFileBtn = new Button(OnNewFileClicked);
            newFileBtn.text = "+ New file";
            newFileBtn.style.fontSize = 11;
            newFileBtn.style.marginLeft = 10;
            newFileBtn.style.marginRight = 10;
            sidebar.Add(newFileBtn);

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

            var searchField = new TextField();
            searchField.value = _searchQuery;
            searchField.style.flexGrow = 1;
            searchField.style.fontSize = 12;

            var placeholder = searchField.Q<TextElement>();
            if (placeholder != null)
                searchField.SetPlaceholderText("Search keys and translations...");

            searchField.RegisterValueChangedCallback(evt =>
            {
                _searchQuery = evt.newValue;
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

                    var row = MakeRow(false);

                    // Key cell
                    var keyLabel = new Label(shortKey);
                    keyLabel.style.width = 200;
                    keyLabel.style.fontSize = 11;
                    keyLabel.style.paddingLeft = 20;
                    keyLabel.style.paddingTop = 2;
                    keyLabel.style.paddingBottom = 2;
                    keyLabel.style.color = new Color(0.4f, 0.4f, 0.4f);
                    keyLabel.tooltip = key;

                    keyLabel.RegisterCallback<ContextClickEvent>(evt =>
                    {
                        ShowKeyContextMenu(key);
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
                        field.multiline = false;

                        if (string.IsNullOrEmpty(value))
                        {
                            field.style.backgroundColor = new Color(0.9f, 0.3f, 0.3f, 0.1f);
                        }

                        string capturedKey = key;
                        string capturedLang = langCode;
                        string capturedFile = sourceFile;

                        field.RegisterCallback<FocusOutEvent>(evt =>
                        {
                            string newValue = field.value;
                            string oldValue = _data.GetTranslation(capturedKey, capturedLang) ?? "";

                            if (newValue != oldValue)
                            {
                                _data.SetTranslation(capturedKey, capturedLang, newValue);
                                _data.SaveFile(capturedFile, capturedLang);
                                AssetDatabase.Refresh();
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
            _statusLabel.text = $"{totalKeys} keys from {fileCount} file(s){missingText}";
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
                : (_data.SourceFiles.Count > 0 ? _data.SourceFiles[0] : "default");

            popup.Init(_data.SourceFiles.ToList(), defaultFile, (key, file) =>
            {
                _data.AddKey(key, file);
                _data.SaveFileAllLanguages(file);
                AssetDatabase.Refresh();
                RebuildTable();
            });

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

        private void ShowKeyContextMenu(string key)
        {
            var menu = new GenericMenu();

            menu.AddItem(new GUIContent("Copy key"), false, () =>
            {
                EditorGUIUtility.systemCopyBuffer = key;
            });

            menu.AddSeparator("");

            foreach (var file in _data.SourceFiles)
            {
                string f = file;
                bool isCurrent = _data.GetFileForKey(key) == f;
                menu.AddItem(new GUIContent($"Move to/{f}.json"), isCurrent, () =>
                {
                    if (isCurrent) return;
                    string oldFile = _data.GetFileForKey(key);
                    _data.MoveKeyToFile(key, f);

                    if (!string.IsNullOrEmpty(oldFile))
                        _data.SaveFileAllLanguages(oldFile);

                    _data.SaveFileAllLanguages(f);
                    AssetDatabase.Refresh();
                    RebuildTable();
                });
            }

            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Delete key"), false, () =>
            {
                if (EditorUtility.DisplayDialog("Delete key",
                    $"Delete '{key}' from all languages?", "Delete", "Cancel"))
                {
                    string file = _data.GetFileForKey(key);
                    _data.RemoveKey(key);

                    if (!string.IsNullOrEmpty(file))
                        _data.SaveFileAllLanguages(file);

                    AssetDatabase.Refresh();
                    RebuildTable();
                }
            });

            menu.ShowAsContext();
        }

        private void RefreshAll()
        {
            _window.RefreshCurrentTab();
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

        public void Init(List<string> files, string defaultFile, System.Action<string, string> onAdd)
        {
            _files = files;
            _selectedFile = defaultFile;
            _onAdd = onAdd;
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(4);
            _keyName = EditorGUILayout.TextField("Key", _keyName);

            if (_files != null && _files.Count > 0)
            {
                int idx = Mathf.Max(0, _files.IndexOf(_selectedFile));
                idx = EditorGUILayout.Popup("File", idx, _files.ToArray());
                _selectedFile = _files[idx];
            }

            EditorGUILayout.Space(4);

            if (GUILayout.Button("Add") && !string.IsNullOrEmpty(_keyName))
            {
                _onAdd?.Invoke(_keyName, _selectedFile);
                Close();
            }
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
            EditorGUILayout.Space(4);

            if (GUILayout.Button("Create") && !string.IsNullOrEmpty(_fileName))
            {
                _data.CreateSourceFile(_fileName);

                // Create empty JSON for all languages
                foreach (var profile in _config.languages)
                {
                    if (profile == null) continue;
                    _data.SaveFile(_fileName, profile.Code);
                }

                AssetDatabase.Refresh();
                _onCreated?.Invoke();
                Close();
            }
        }
    }
}
