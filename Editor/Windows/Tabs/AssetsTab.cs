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

        // Keys whose preview row is currently expanded
        private readonly HashSet<string> _expandedRows = new();

        public AssetsTab(EditorLocalizationData data, LocalizationConfig config,
            LocalizationEditorWindow window)
        {
            _data = data;
            _config = config;
            _window = window;
            RefreshTables();

            // Auto-refresh when asset tables are created/deleted externally
            AssetDatabase.importPackageCompleted += OnPackageImported;
            EditorApplication.projectChanged += OnProjectChanged;
        }

        ~AssetsTab()
        {
            AssetDatabase.importPackageCompleted -= OnPackageImported;
            EditorApplication.projectChanged -= OnProjectChanged;
        }

        private void OnPackageImported(string _) => RefreshTables();
        private void OnProjectChanged() => RefreshTables();

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
            EditorGUILayout.LabelField("Localized Assets", EditorStyles.boldLabel);

            EditorGUILayout.HelpBox(
                "Manage localized sprites, audio clips, and custom assets.\n" +
                "Each language has its own AssetTable stored in its folder.\n" +
                "Drag assets into cells to assign them. Click ▶ to preview.",
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
            GUILayout.Space(22); // space for the expand arrow
            EditorGUILayout.LabelField("Key", EditorStyles.miniLabel, GUILayout.Width(180));

            foreach (var lang in languages)
                EditorGUILayout.LabelField(lang.Code, EditorStyles.miniLabel, GUILayout.Width(140));

            EditorGUILayout.EndHorizontal();

            // Rows
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            foreach (var key in keys)
            {
                bool isExpanded = _expandedRows.Contains(key);
                bool hasPreviewableAsset = KeyHasPreviewableAsset(key);

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                // ── Main row ──
                EditorGUILayout.BeginHorizontal();

                // Expand toggle (only if preview is useful)
                if (hasPreviewableAsset)
                {
                    string arrow = isExpanded ? "\u25bc" : "\u25b6";
                    if (GUILayout.Button(arrow, EditorStyles.label, GUILayout.Width(18)))
                    {
                        if (isExpanded) _expandedRows.Remove(key);
                        else _expandedRows.Add(key);
                    }
                }
                else
                {
                    GUILayout.Space(22);
                }

                // Key label with context menu
                var keyRect = EditorGUILayout.GetControlRect(GUILayout.Width(180));
                EditorGUI.LabelField(keyRect, key, EditorStyles.miniLabel);

                if (Event.current.type == EventType.ContextClick && keyRect.Contains(Event.current.mousePosition))
                {
                    ShowKeyContextMenu(key);
                    Event.current.Use();
                }

                // Determine the accepted type for this key
                var acceptedType = GetObjectTypeForKey(key);

                // Asset cells per language
                foreach (var lang in languages)
                {
                    var table = GetTable(lang.Code);
                    Object currentAsset = table?.Get(key);

                    var newAsset = EditorGUILayout.ObjectField(
                        currentAsset, acceptedType, false, GUILayout.Width(140));

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

                // ── Expanded preview row ──
                if (isExpanded && hasPreviewableAsset)
                {
                    DrawExpandedPreviewRow(key, languages);
                }

                EditorGUILayout.EndVertical();
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

        /// <summary>
        /// Draws a secondary row below the main row showing large previews of the assigned assets.
        /// One preview per language, aligned with the ObjectField above it.
        /// </summary>
        private void DrawExpandedPreviewRow(string key, List<LanguageProfile> languages)
        {
            const float previewSize = 96f;

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(22 + 180 + 4); // align with first language column

            foreach (var lang in languages)
            {
                var table = GetTable(lang.Code);
                var asset = table?.Get(key);

                var box = EditorGUILayout.BeginVertical(GUILayout.Width(140));

                if (asset == null)
                {
                    var rect = GUILayoutUtility.GetRect(140, previewSize);
                    EditorGUI.DrawRect(rect, new Color(0, 0, 0, 0.05f));
                    EditorGUI.LabelField(rect, "(not assigned)", new GUIStyle(EditorStyles.centeredGreyMiniLabel)
                    {
                        alignment = TextAnchor.MiddleCenter
                    });
                }
                else if (asset is Sprite sprite)
                {
                    var rect = GUILayoutUtility.GetRect(140, previewSize);
                    DrawSpritePreview(rect, sprite);
                }
                else if (asset is Texture2D tex2d)
                {
                    var rect = GUILayoutUtility.GetRect(140, previewSize);
                    GUI.DrawTexture(rect, tex2d, ScaleMode.ScaleToFit);
                }
                else if (asset is AudioClip clip)
                {
                    var rect = GUILayoutUtility.GetRect(140, previewSize);
                    DrawAudioPreview(rect, clip);
                }
                else
                {
                    // Generic fallback — mini thumbnail + type name
                    var rect = GUILayoutUtility.GetRect(140, previewSize);
                    var thumb = AssetPreview.GetMiniThumbnail(asset);
                    if (thumb != null)
                    {
                        var iconRect = new Rect(rect.x + rect.width / 2f - 24, rect.y + 8, 48, 48);
                        GUI.DrawTexture(iconRect, thumb, ScaleMode.ScaleToFit);
                    }
                    var labelRect = new Rect(rect.x, rect.y + 60, rect.width, 16);
                    EditorGUI.LabelField(labelRect, asset.GetType().Name,
                        new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter });
                }

                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(2);
        }

        private static void DrawSpritePreview(Rect rect, Sprite sprite)
        {
            if (sprite == null) return;

            // Checkerboard background to show transparency
            EditorGUI.DrawRect(rect, new Color(0.2f, 0.2f, 0.2f, 0.3f));

            // Draw the sprite respecting its rect within the texture atlas
            var tex = sprite.texture;
            if (tex == null) return;

            var spriteRect = sprite.rect;
            var uv = new Rect(
                spriteRect.x / tex.width,
                spriteRect.y / tex.height,
                spriteRect.width / tex.width,
                spriteRect.height / tex.height);

            // Preserve aspect ratio inside the target rect
            float aspect = spriteRect.width / spriteRect.height;
            float drawW = rect.width;
            float drawH = rect.width / aspect;

            if (drawH > rect.height)
            {
                drawH = rect.height;
                drawW = rect.height * aspect;
            }

            var drawRect = new Rect(
                rect.x + (rect.width - drawW) / 2f,
                rect.y + (rect.height - drawH) / 2f,
                drawW, drawH);

            GUI.DrawTextureWithTexCoords(drawRect, tex, uv);
        }

        private static void DrawAudioPreview(Rect rect, AudioClip clip)
        {
            if (clip == null) return;

            EditorGUI.DrawRect(rect, new Color(0, 0, 0, 0.08f));

            // Icon
            var iconRect = new Rect(rect.x + 8, rect.y + 8, 32, 32);
            var thumb = AssetPreview.GetMiniThumbnail(clip);
            if (thumb != null)
                GUI.DrawTexture(iconRect, thumb, ScaleMode.ScaleToFit);

            // Metadata
            var infoRect = new Rect(rect.x + 48, rect.y + 8, rect.width - 56, 16);
            EditorGUI.LabelField(infoRect, clip.name, EditorStyles.miniLabel);

            var metaRect = new Rect(rect.x + 48, rect.y + 26, rect.width - 56, 14);
            string meta = $"{clip.length:F2}s | {clip.frequency / 1000}kHz | {clip.channels}ch";
            EditorGUI.LabelField(metaRect, meta, EditorStyles.miniLabel);

            // Play button
            var btnRect = new Rect(rect.x + 8, rect.y + rect.height - 24, rect.width - 16, 18);
            if (GUI.Button(btnRect, "▶ Play", EditorStyles.miniButton))
            {
                PlayClipPreview(clip);
            }
        }

        private static void PlayClipPreview(AudioClip clip)
        {
            // Use Unity's internal AudioUtil via reflection — public API doesn't expose editor preview
            var audioUtil = System.Type.GetType("UnityEditor.AudioUtil, UnityEditor");
            if (audioUtil == null) return;

            // Unity 2020+: PlayPreviewClip(AudioClip, int, bool)
            var playMethod = audioUtil.GetMethod("PlayPreviewClip",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);

            if (playMethod != null)
            {
                playMethod.Invoke(null, new object[] { clip, 0, false });
                return;
            }

            // Fallback to older API name
            var fallback = audioUtil.GetMethod("PlayClip",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public,
                null, new[] { typeof(AudioClip) }, null);
            fallback?.Invoke(null, new object[] { clip });
        }

        /// <summary>
        /// Returns true if any language has an asset for this key that we know how to preview
        /// (Sprite, Texture2D, AudioClip). Other types fall back to a generic icon preview.
        /// </summary>
        private bool KeyHasPreviewableAsset(string key)
        {
            foreach (var table in _tables.Values)
            {
                var asset = table?.Get(key);
                if (asset != null) return true;
            }
            return false;
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

        /// <summary>
        /// Determines the accepted asset type for a key based on:
        /// 1. Current filter (if not All, use filter type)
        /// 2. Existing assets (if key already has a Sprite, only accept Sprites)
        /// 3. Falls back to Object if nothing is assigned yet
        /// </summary>
        private System.Type GetObjectTypeForKey(string key)
        {
            // If filter is set, use it
            if (_filter != AssetTypeFilter.All)
                return GetObjectType();

            // Auto-detect from existing assets for this key
            foreach (var table in _tables.Values)
            {
                var asset = table.Get(key);
                if (asset == null) continue;

                if (asset is Sprite) return typeof(Sprite);
                if (asset is AudioClip) return typeof(AudioClip);
                return asset.GetType();
            }

            return typeof(Object);
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