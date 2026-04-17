using System.Collections.Generic;
using System.Linq;
using SimplyLocalize.Components;
using SimplyLocalize.Editor.AssetPreviews;
using SimplyLocalize.Editor.Data;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;

namespace SimplyLocalize.Editor.Inspectors
{
    /// <summary>
    /// Custom inspector for all LocalizedAsset-derived components.
    /// Shows a key search popup filtered to asset table keys,
    /// with a preview of assigned assets per language.
    /// </summary>
    [CustomEditor(typeof(LocalizedAsset<>), true)]
    public class LocalizedAssetEditor : UnityEditor.Editor
    {
        private SerializedProperty _keyProp;
        private string _newKeyInput = "";
        private readonly HashSet<string> _expandedLanguages = new();

        private void OnEnable()
        {
            _keyProp = serializedObject.FindProperty("_key");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.Space(2);

            // Determine the expected asset type from the component
            var assetType = GetExpectedAssetType();
            string typeName = assetType != null ? assetType.Name : "Asset";

            // Key selector with search
            DrawAssetKeySelector(assetType, typeName);

            // Preview of assigned assets (collapsed by default)
            if (!string.IsNullOrEmpty(_keyProp.stringValue))
            {
                DrawAssetPreview();
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawAssetKeySelector(System.Type assetType, string typeName)
        {
            var allAssetKeys = GetAssetKeysForType(assetType);
            string currentKey = _keyProp.stringValue;
            bool keyExists = !string.IsNullOrEmpty(currentKey) && allAssetKeys.Contains(currentKey);
            bool isEmpty = string.IsNullOrEmpty(currentKey);

            // Main row
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel($"Key ({typeName})");

            string display = isEmpty ? "<None>"
                : keyExists ? FormatKey(currentKey)
                : $"<Missing> ({currentKey})";

            Color prev = GUI.color;

            if (!isEmpty && !keyExists)
                GUI.color = new Color(1f, 0.35f, 0.35f);

            if (GUILayout.Button(display, EditorStyles.popup))
                OpenAssetKeySearch(allAssetKeys, assetType);

            GUI.color = prev;

            if (!isEmpty && !keyExists)
            {
                if (GUILayout.Button("+", GUILayout.Width(22)))
                {
                    // Add this key to all asset tables
                    AddKeyToAssetTables(currentKey);
                }
            }

            EditorGUILayout.EndHorizontal();

            // New key input
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("Add new key");
            bool dup = !string.IsNullOrEmpty(_newKeyInput) && allAssetKeys.Contains(_newKeyInput);
            prev = GUI.color;
            if (dup) GUI.color = new Color(1f, 0.35f, 0.35f);
            _newKeyInput = EditorGUILayout.TextField(_newKeyInput);
            GUI.color = prev;

            GUI.enabled = !string.IsNullOrEmpty(_newKeyInput) && !dup;

            if (GUILayout.Button("Add", GUILayout.Width(40)))
            {
                AddKeyToAssetTables(_newKeyInput);
                _keyProp.stringValue = _newKeyInput;
                _keyProp.serializedObject.ApplyModifiedProperties();
                _newKeyInput = "";
            }

            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            if (dup)
                EditorGUILayout.HelpBox("This key already exists.", MessageType.Error);
        }

        private void DrawAssetPreview()
        {
            var config = EditorDataCache.Config;
            if (config == null) return;

            string key = _keyProp.stringValue;
            var basePath = EditorDataCache.Data?.BasePath;
            if (string.IsNullOrEmpty(basePath)) return;

            EditorGUILayout.Space(4);

            // Foldout header row: toggle + "Expand/Collapse all" button aligned to the right
            EditorGUILayout.BeginHorizontal();
            
            EditorGUILayout.LabelField("Translations", EditorStyles.boldLabel);
            
            bool anyExpanded = _expandedLanguages.Count > 0;
            string btnLabel = anyExpanded ? "Collapse all" : "Expand all";
            
            if (GUILayout.Button(btnLabel, EditorStyles.miniButton, GUILayout.Width(90)))
            {
                if (anyExpanded)
                {
                    _expandedLanguages.Clear();
                }
                else
                {
                    foreach (var p in config.languages)
                    {
                        if (p != null) _expandedLanguages.Add(p.Code);
                    }
                }
            }

            EditorGUILayout.EndHorizontal();
            
            foreach (var profile in config.languages)
            {
                if (profile == null) continue;

                string langPath = System.IO.Path.Combine(basePath, profile.Code);
                var table = FindTableAtPath(langPath);
                Object asset = table?.Get(key);

                // If missing, try to resolve through the asset fallback chain
                string fallbackLang = null;
                if (asset == null)
                    asset = ResolveFallbackAsset(config, basePath, key, profile, out fallbackLang);

                DrawLanguageRow(profile, asset, fallbackLang);
            }
        }

        /// <summary>
        /// Walks the fallback chain (per-language -> global) to find an asset when the
        /// target language's table has nothing. Returns null if no fallback has the key either.
        /// </summary>
        private static Object ResolveFallbackAsset(
            LocalizationConfig config,
            string basePath,
            string key,
            LanguageProfile targetProfile,
            out string fromLanguage)
        {
            fromLanguage = null;

            var visited = new HashSet<string> { targetProfile.Code };
            var fb = targetProfile.fallbackProfile;

            while (fb != null && visited.Add(fb.Code))
            {
                string langPath = System.IO.Path.Combine(basePath, fb.Code);
                var table = FindTableAtPath(langPath);
                var candidate = table?.Get(key);

                if (candidate != null)
                {
                    fromLanguage = fb.Code;
                    return candidate;
                }

                fb = fb.fallbackProfile;
            }

            // Try global fallback
            string globalCode = config.FallbackLanguageCode;

            if (!string.IsNullOrEmpty(globalCode) && visited.Add(globalCode))
            {
                string langPath = System.IO.Path.Combine(basePath, globalCode);
                var table = FindTableAtPath(langPath);
                var candidate = table?.Get(key);

                if (candidate != null)
                {
                    fromLanguage = globalCode;
                    return candidate;
                }
            }

            return null;
        }

        /// <summary>
        /// Draws a single per-language row in the asset preview foldout:
        ///   [mini-thumb] {asset name}   [▶/▼]
        /// Clicking the thumbnail or name pings the asset in the Project window.
        /// Clicking ▶ expands a large preview rendered through AssetPreviewRegistry,
        /// independently per language so the user can compare multiple expanded at once.
        ///
        /// If <paramref name="fallbackLang"/> is non-null, the asset came from a fallback
        /// language and is rendered dimmed with an origin marker.
        /// </summary>
        private void DrawLanguageRow(LanguageProfile profile, Object asset, string fallbackLang = null)
        {
            bool expanded = _expandedLanguages.Contains(profile.Code);
            bool isFallback = asset != null && !string.IsNullOrEmpty(fallbackLang);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"{profile.displayName} ({profile.Code})",
                GUILayout.Width(EditorGUIUtility.labelWidth));

            if (asset != null)
            {
                // Dim the row if this asset came from a fallback language
                Color prevColor = GUI.color;
                if (isFallback) GUI.color = new Color(1f, 1f, 1f, 0.55f);

                // Clickable thumbnail + name — pings asset in Project window on click
                var thumbnail = AssetPreview.GetMiniThumbnail(asset);
                var rowRect = EditorGUILayout.GetControlRect(GUILayout.Height(20));

                if (thumbnail != null)
                {
                    var iconRect = new Rect(rowRect.x, rowRect.y, 20, 20);
                    GUI.DrawTexture(iconRect, thumbnail, ScaleMode.ScaleToFit);
                }

                var nameRect = new Rect(rowRect.x + 24, rowRect.y, rowRect.width - 24, rowRect.height);
                string nameText = isFallback
                    ? $"{asset.name}  \u2014 from {fallbackLang}"
                    : asset.name;
                EditorGUI.LabelField(nameRect, nameText, EditorStyles.miniLabel);

                // Full row (thumb + name) is clickable to ping
                EditorGUIUtility.AddCursorRect(rowRect, MouseCursor.Link);

                if (Event.current.type == EventType.MouseDown
                    && Event.current.button == 0
                    && rowRect.Contains(Event.current.mousePosition))
                {
                    EditorGUIUtility.PingObject(asset);
                    Selection.activeObject = asset;
                    Event.current.Use();
                }

                GUI.color = prevColor;

                // Preview toggle button (not dimmed)
                string toggleLabel = expanded ? "▼" : "▶";
                if (GUILayout.Button(new GUIContent(toggleLabel, "Show large preview"),
                    EditorStyles.miniButton, GUILayout.Width(22), GUILayout.Height(18)))
                {
                    if (expanded) _expandedLanguages.Remove(profile.Code);
                    else _expandedLanguages.Add(profile.Code);
                }
            }
            else
            {
                Color prev = GUI.color;
                GUI.color = new Color(1f, 0.5f, 0.5f);
                EditorGUILayout.LabelField("(not assigned)", EditorStyles.miniLabel);
                GUI.color = prev;
            }

            EditorGUILayout.EndHorizontal();

            // Large preview — only when this language is expanded and has an asset
            if (expanded && asset != null)
            {
                const float previewWidth = 200f;
                const float previewHeight = 140f;

                var previewRect = GUILayoutUtility.GetRect(
                    previewWidth, previewHeight, GUILayout.ExpandWidth(false));

                previewRect.x += EditorGUIUtility.labelWidth;

                // Dim the large preview too for fallback sources
                var prevGUI = GUI.color;
                if (isFallback) GUI.color = new Color(1f, 1f, 1f, 0.55f);

                var renderer = AssetPreviewRegistry.GetRendererFor(asset);
                renderer.DrawPreview(previewRect, asset);

                GUI.color = prevGUI;
            }
        }

        private void OpenAssetKeySearch(List<string> keys, System.Type assetType)
        {
            var searchWindow = ScriptableObject.CreateInstance<KeySearchWindow>();

            searchWindow.Init(keys, selectedKey =>
            {
                _keyProp.serializedObject.Update();
                _keyProp.stringValue = selectedKey ?? "";
                _keyProp.serializedObject.ApplyModifiedProperties();
                EditorDataCache.Invalidate();
            });

            var mousePos = GUIUtility.GUIToScreenPoint(Event.current.mousePosition);
            SearchWindow.Open(new SearchWindowContext(mousePos), searchWindow);
        }

        /// <summary>
        /// Collects all asset keys from all tables, optionally filtered by type.
        /// </summary>
        private List<string> GetAssetKeysForType(System.Type assetType)
        {
            var keys = new HashSet<string>();
            var config = EditorDataCache.Config;
            var basePath = EditorDataCache.Data?.BasePath;

            if (config == null || string.IsNullOrEmpty(basePath))
                return new List<string>();

            foreach (var profile in config.languages)
            {
                if (profile == null) continue;

                string langPath = System.IO.Path.Combine(basePath, profile.Code);
                var table = FindTableAtPath(langPath);
                if (table == null) continue;

                foreach (var entry in table.Entries)
                {
                    if (string.IsNullOrEmpty(entry.key)) continue;

                    // If type filter is active, only include matching keys
                    if (assetType != null && assetType != typeof(Object) && entry.asset != null)
                    {
                        if (!assetType.IsInstanceOfType(entry.asset))
                            continue;
                    }

                    keys.Add(entry.key);
                }
            }

            var sorted = keys.ToList();
            sorted.Sort();
            return sorted;
        }

        /// <summary>
        /// Determines the expected asset type from the concrete generic component.
        /// e.g. LocalizedSprite → Sprite, LocalizedAudioClip → AudioClip.
        /// </summary>
        private System.Type GetExpectedAssetType()
        {
            var type = target.GetType();

            while (type != null)
            {
                if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(LocalizedAsset<>))
                    return type.GetGenericArguments()[0];

                type = type.BaseType;
            }

            return typeof(Object);
        }

        private static LocalizationAssetTable FindTableAtPath(string langDir)
        {
            if (!System.IO.Directory.Exists(langDir)) return null;

            var files = System.IO.Directory.GetFiles(langDir, "*.asset");

            foreach (var file in files)
            {
                string relativePath = "Assets" + file.Substring(Application.dataPath.Length);
                var obj = AssetDatabase.LoadAssetAtPath<LocalizationAssetTable>(relativePath);
                if (obj != null) return obj;
            }

            return null;
        }

        private static void AddKeyToAssetTables(string key)
        {
            var config = EditorDataCache.Config;
            var basePath = EditorDataCache.Data?.BasePath;

            if (config == null || string.IsNullOrEmpty(basePath)) return;

            foreach (var profile in config.languages)
            {
                if (profile == null) continue;

                string langPath = System.IO.Path.Combine(basePath, profile.Code);
                var table = FindTableAtPath(langPath);

                if (table != null && !table.HasKey(key))
                {
                    table.Set(key, null);
                    EditorUtility.SetDirty(table);
                }
            }

            AssetDatabase.SaveAssets();
            EditorDataCache.Invalidate();
        }

        private static string FormatKey(string key) =>
            key.Contains('/') ? $"{key.Split('/')[^1]}    ({key})" : key;
    }
}
