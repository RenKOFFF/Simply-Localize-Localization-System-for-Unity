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
        private bool _showPreview;

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
                DrawAssetPreview(assetType);
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

        private void DrawAssetPreview(System.Type assetType)
        {
            var config = EditorDataCache.Config;
            if (config == null) return;

            string key = _keyProp.stringValue;
            var basePath = EditorDataCache.Data?.BasePath;
            if (string.IsNullOrEmpty(basePath)) return;

            EditorGUILayout.Space(4);
            _showPreview = EditorGUILayout.Foldout(_showPreview, "Asset Preview", true);

            if (!_showPreview) return;

            DrawLargePreviewRow(config, basePath, key);
        }

        /// <summary>
        /// Draws large per-language previews using the same registry-based rendering
        /// as the Assets tab. Uses AssetPreviewRegistry so custom IAssetPreviewRenderer
        /// implementations work here too.
        /// </summary>
        private void DrawLargePreviewRow(LocalizationConfig config, string basePath, string key)
        {
            const float cellWidth = 140f;
            const float cellHeight = 96f;
            const float labelHeight = 14f;
            const float spacing = 6f;

            EditorGUI.indentLevel++;

            // Compute how many cells fit on one row, then wrap
            float available = EditorGUIUtility.currentViewWidth - 40f;
            int perRow = Mathf.Max(1, Mathf.FloorToInt((available + spacing) / (cellWidth + spacing)));

            int idx = 0;
            EditorGUILayout.BeginHorizontal();

            foreach (var profile in config.languages)
            {
                if (profile == null) continue;

                if (idx > 0 && idx % perRow == 0)
                {
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.BeginHorizontal();
                }

                EditorGUILayout.BeginVertical(GUILayout.Width(cellWidth));

                // Language label above the preview
                var langRect = GUILayoutUtility.GetRect(cellWidth, labelHeight);
                EditorGUI.LabelField(langRect, $"{profile.Code} \u2014 {profile.displayName}",
                    EditorStyles.miniLabel);

                // Preview area dispatched through the registry
                var previewRect = GUILayoutUtility.GetRect(cellWidth, cellHeight);

                string langPath = System.IO.Path.Combine(basePath, profile.Code);
                var table = FindTableAtPath(langPath);
                Object asset = table?.Get(key);

                var renderer = AssetPreviewRegistry.GetRendererFor(asset);
                renderer.DrawPreview(previewRect, asset);

                EditorGUILayout.EndVertical();

                GUILayout.Space(spacing);
                idx++;
            }

            EditorGUILayout.EndHorizontal();
            EditorGUI.indentLevel--;
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
