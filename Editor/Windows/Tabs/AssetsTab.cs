using System.Collections.Generic;
using System.IO;
using System.Linq;
using SimplyLocalize.Editor.Data;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace SimplyLocalize.Editor.Windows.Tabs
{
    public class AssetsTab : IEditorTab
    {
        private enum AssetTypeFilter { Sprites, AudioClips, All }

        private readonly EditorLocalizationData _data;
        private readonly LocalizationConfig _config;
        private readonly LocalizationEditorWindow _window;

        private AssetTypeFilter _filter = AssetTypeFilter.All;
        private Vector2 _scrollPos;

        // Loaded tables: languageCode → table
        private readonly Dictionary<string, LocalizationAssetTable> _tables = new();
        private List<string> _allAssetKeys;

        public AssetsTab(EditorLocalizationData data, LocalizationConfig config,
            LocalizationEditorWindow window)
        {
            _data = data;
            _config = config;
            _window = window;
            RefreshTables();
        }

        public void Build(VisualElement container)
        {
            var imgui = new IMGUIContainer(DrawIMGUI);
            imgui.style.flexGrow = 1;
            container.Add(imgui);
        }

        private void DrawIMGUI()
        {
            EditorGUILayout.Space(8);

            // Header
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Localized Assets", EditorStyles.boldLabel);

            if (GUILayout.Button("Refresh", GUILayout.Width(60)))
                RefreshTables();

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.HelpBox(
                "Manage localized sprites, audio clips, and custom assets.\n" +
                "Each language has its own AssetTable stored in its folder.\n" +
                "Drag assets into cells to assign them.",
                MessageType.Info);

            EditorGUILayout.Space(4);

            // Filter + Add key
            EditorGUILayout.BeginHorizontal();
            _filter = (AssetTypeFilter)EditorGUILayout.EnumPopup("Show", _filter, GUILayout.Width(200));

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("+ Add asset key", GUILayout.Width(120)))
                ShowAddKeyPopup();

            if (GUILayout.Button("Create missing tables", GUILayout.Width(150)))
                CreateMissingTables();

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(6);

            if (_config == null || _config.languages.Count == 0)
            {
                EditorGUILayout.LabelField("No languages configured.", EditorStyles.centeredGreyMiniLabel);
                return;
            }

            // Table
            DrawAssetTable();
        }

        // ──────────────────────────────────────────────
        //  Asset table display
        // ──────────────────────────────────────────────

        private void DrawAssetTable()
        {
            var languages = _config.languages.Where(p => p != null).ToList();
            var keys = GetFilteredKeys();

            if (keys.Count == 0)
            {
                EditorGUILayout.LabelField("No asset keys found. Add keys using the button above.",
                    EditorStyles.centeredGreyMiniLabel);
                return;
            }

            // Header
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            EditorGUILayout.LabelField("Key", EditorStyles.miniLabel, GUILayout.Width(180));

            foreach (var lang in languages)
                EditorGUILayout.LabelField(lang.Code, EditorStyles.miniLabel, GUILayout.Width(140));

            EditorGUILayout.EndHorizontal();

            // Rows
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            foreach (var key in keys)
            {
                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);

                // Key label with context menu
                var keyRect = EditorGUILayout.GetControlRect(GUILayout.Width(180));
                EditorGUI.LabelField(keyRect, key, EditorStyles.miniLabel);

                if (Event.current.type == EventType.ContextClick && keyRect.Contains(Event.current.mousePosition))
                {
                    ShowKeyContextMenu(key);
                    Event.current.Use();
                }

                // Asset cells per language
                foreach (var lang in languages)
                {
                    var table = GetTable(lang.Code);
                    Object currentAsset = table?.Get(key);

                    var newAsset = EditorGUILayout.ObjectField(
                        currentAsset, GetObjectType(), false, GUILayout.Width(140));

                    if (newAsset != currentAsset)
                    {
                        EnsureTable(lang.Code);
                        var t = GetTable(lang.Code);

                        if (t != null)
                        {
                            if (newAsset != null)
                                t.Set(key, newAsset);
                            else
                                t.Remove(key);

                            EditorUtility.SetDirty(t);
                        }
                    }
                }

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();

            // Status
            EditorGUILayout.Space(4);

            int totalCells = keys.Count * languages.Count;
            int filledCells = 0;

            foreach (var key in keys)
                foreach (var lang in languages)
                    if (GetTable(lang.Code)?.Get(key) != null) filledCells++;

            EditorGUILayout.LabelField(
                $"{keys.Count} keys | {filledCells}/{totalCells} assigned",
                EditorStyles.centeredGreyMiniLabel);
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
                    // Convert to Assets-relative path
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
            if (_tables.ContainsKey(langCode)) return;

            string langDir = Path.Combine(_data.BasePath, langCode);

            if (!Directory.Exists(langDir))
                Directory.CreateDirectory(langDir);

            // Create asset table SO
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

                if (!_tables.ContainsKey(profile.Code))
                {
                    EnsureTable(profile.Code);
                    created++;
                }
            }

            AssetDatabase.Refresh();

            if (created > 0)
                EditorUtility.DisplayDialog("Tables created",
                    $"Created {created} asset table(s).", "OK");
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
        //  Key management
        // ──────────────────────────────────────────────

        private List<string> GetFilteredKeys()
        {
            if (_allAssetKeys == null) return new List<string>();

            if (_filter == AssetTypeFilter.All)
                return _allAssetKeys;

            // Filter by checking actual asset types in tables
            return _allAssetKeys.Where(key =>
            {
                foreach (var table in _tables.Values)
                {
                    var asset = table.Get(key);
                    if (asset == null) continue;

                    if (_filter == AssetTypeFilter.Sprites && asset is Sprite)
                        return true;
                    if (_filter == AssetTypeFilter.AudioClips && asset is AudioClip)
                        return true;
                }

                // Include keys with no assets assigned yet
                return !_tables.Values.Any(t => t.HasKey(key));
            }).ToList();
        }

        private System.Type GetObjectType()
        {
            return _filter switch
            {
                AssetTypeFilter.Sprites => typeof(Sprite),
                AssetTypeFilter.AudioClips => typeof(AudioClip),
                _ => typeof(Object)
            };
        }

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

                // Add empty entry to all existing tables
                foreach (var table in _tables.Values)
                {
                    if (!table.HasKey(key))
                    {
                        table.Set(key, null);
                        EditorUtility.SetDirty(table);
                    }
                }

                AssetDatabase.SaveAssets();
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
                    table.Remove(key);
                    EditorUtility.SetDirty(table);
                }

                _allAssetKeys.Remove(key);
                AssetDatabase.SaveAssets();
            });

            menu.ShowAsContext();
        }
    }

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