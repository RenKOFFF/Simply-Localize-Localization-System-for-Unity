using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using SimplyLocalize.Components;
using SimplyLocalize.Editor.Data;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.UIElements;

namespace SimplyLocalize.Editor.Windows.Tabs
{
    public class AutoLocalizeTab : IEditorTab
    {
        private enum ScopeMode { ActiveScene, SelectedObjects }
        private enum KeyPattern { SceneParentName, ParentName, ObjectName, Custom }

        private readonly EditorLocalizationData _data;
        private readonly LocalizationConfig _config;
        private readonly LocalizationEditorWindow _window;

        private ScopeMode _scope = ScopeMode.ActiveScene;
        private KeyPattern _keyPattern = KeyPattern.ParentName;
        private string _customPattern = "{parent}/{name}";
        private string _keyPrefix = "";
        private bool _skipNumericOnly = true;
        private bool _skipAlreadyLocalized = true;
        private int _minTextLength = 2;
        private bool _enterPrefabInstances;
        private bool _modifyPrefabAssets;
        private string _targetFile = "global";

        private List<ScanEntry> _scanResults;
        private Vector2 _scrollPos;

        public AutoLocalizeTab(EditorLocalizationData data, LocalizationConfig config,
            LocalizationEditorWindow window)
        {
            _data = data;
            _config = config;
            _window = window;
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
            EditorGUILayout.LabelField("Auto-Localize", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Scan your scene or selected objects for text components without localization.\n" +
                "Preview the results, then apply to bulk-add LocalizedText components and create translation keys.",
                MessageType.Info);

            EditorGUILayout.Space(8);
            DrawScopeSettings();
            EditorGUILayout.Space(6);
            DrawFilterSettings();
            EditorGUILayout.Space(6);
            DrawKeySettings();
            EditorGUILayout.Space(8);

            if (GUILayout.Button("Scan", GUILayout.Height(28)))
                RunScan();

            if (_scanResults != null)
            {
                EditorGUILayout.Space(8);
                DrawResults();
            }
        }

        // ──────────────────────────────────────────────
        //  Settings
        // ──────────────────────────────────────────────

        private void DrawScopeSettings()
        {
            EditorGUILayout.LabelField("Scope", EditorStyles.boldLabel);
            _scope = (ScopeMode)EditorGUILayout.EnumPopup("Scan target", _scope);

            if (_scope == ScopeMode.SelectedObjects)
            {
                var selected = Selection.gameObjects;

                if (selected.Length == 0)
                {
                    EditorGUILayout.HelpBox(
                        "Select one or more GameObjects in the Hierarchy or Project.",
                        MessageType.Warning);
                }
                else if (selected.Length == 1)
                {
                    EditorGUILayout.HelpBox(
                        $"Selected: {selected[0].name} (with all children)",
                        MessageType.Info);
                }
                else
                {
                    EditorGUILayout.HelpBox(
                        $"Selected: {selected.Length} objects (each with all children)",
                        MessageType.Info);
                }
            }
        }

        private void DrawFilterSettings()
        {
            EditorGUILayout.LabelField("Filters", EditorStyles.boldLabel);

            _skipAlreadyLocalized = EditorGUILayout.Toggle(
                new GUIContent("Skip already localized",
                    "Skip GameObjects that already have a LocalizedText component"),
                _skipAlreadyLocalized);

            _skipNumericOnly = EditorGUILayout.Toggle(
                new GUIContent("Skip numeric-only text",
                    "Skip text containing only digits, punctuation and whitespace"),
                _skipNumericOnly);

            _minTextLength = Mathf.Max(1, EditorGUILayout.IntField(
                new GUIContent("Min text length", "Skip text shorter than N characters"),
                _minTextLength));

            _enterPrefabInstances = EditorGUILayout.Toggle(
                new GUIContent("Enter prefab instances",
                    "Scan inside prefab instances"),
                _enterPrefabInstances);

            if (_enterPrefabInstances)
            {
                EditorGUI.indentLevel++;
                _modifyPrefabAssets = EditorGUILayout.Toggle(
                    new GUIContent("Modify prefab assets directly",
                        "Add LocalizedText inside the prefab asset itself.\n" +
                        "If off, adds as instance overrides."),
                    _modifyPrefabAssets);
                EditorGUI.indentLevel--;
            }
        }

        private void DrawKeySettings()
        {
            EditorGUILayout.LabelField("Key generation", EditorStyles.boldLabel);

            _keyPattern = (KeyPattern)EditorGUILayout.EnumPopup("Pattern", _keyPattern);

            if (_keyPattern == KeyPattern.Custom)
            {
                _customPattern = EditorGUILayout.TextField(
                    new GUIContent("Custom pattern", "Tokens: {scene}, {parent}, {name}, {text}"),
                    _customPattern);
            }

            _keyPrefix = EditorGUILayout.TextField(
                new GUIContent("Prefix", "Prepended to every key, e.g. 'UI/'"),
                _keyPrefix);

            if (_data.SourceFiles.Count > 0)
            {
                var files = _data.SourceFiles.ToArray();
                int idx = Mathf.Max(0, System.Array.IndexOf(files, _targetFile));
                idx = EditorGUILayout.Popup("Target file", idx, files);
                _targetFile = files[idx];
            }
            else
            {
                _targetFile = EditorGUILayout.TextField("Target file", _targetFile);
            }

            if (_config?.defaultLanguage != null)
            {
                EditorGUILayout.LabelField(
                    $"  Translations → {_config.defaultLanguage.displayName} ({_config.defaultLanguage.Code})",
                    EditorStyles.miniLabel);
            }
        }

        // ──────────────────────────────────────────────
        //  Scanning
        // ──────────────────────────────────────────────

        private void RunScan()
        {
            _scanResults = new List<ScanEntry>();

            foreach (var (go, text, isTMP) in CollectTextComponents())
            {
                if (_skipAlreadyLocalized && go.GetComponent<LocalizedComponentBase>() != null)
                    continue;

                if (string.IsNullOrEmpty(text) || text.Length < _minTextLength)
                    continue;

                if (_skipNumericOnly && IsNumericOnly(text))
                    continue;

                string key = GenerateKey(go);
                bool keyExists = _data.GetFileForKey(key) != null;

                _scanResults.Add(new ScanEntry
                {
                    GameObject = go,
                    Text = text,
                    IsTMP = isTMP,
                    GeneratedKey = key,
                    Selected = !keyExists,
                    KeyExists = keyExists,
                    Path = GetHierarchyPath(go)
                });
            }
        }

        private List<(GameObject go, string text, bool isTMP)> CollectTextComponents()
        {
            var results = new List<(GameObject, string, bool)>();

            switch (_scope)
            {
                case ScopeMode.ActiveScene:
                    foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
                        CollectFromHierarchy(root, results);
                    break;

                case ScopeMode.SelectedObjects:
                    // Support multiple selection — each with all children
                    foreach (var selected in Selection.gameObjects)
                        CollectFromHierarchy(selected, results);
                    break;
            }

            return results;
        }

        private void CollectFromHierarchy(GameObject root,
            List<(GameObject, string, bool)> results)
        {
            CollectFromGameObject(root, results);

            for (int i = 0; i < root.transform.childCount; i++)
            {
                var child = root.transform.GetChild(i).gameObject;

                if (!_enterPrefabInstances && PrefabUtility.IsAnyPrefabInstanceRoot(child))
                    continue;

                CollectFromHierarchy(child, results);
            }
        }

        private static void CollectFromGameObject(GameObject go,
            List<(GameObject, string, bool)> results)
        {
            var tmp = go.GetComponent<TMP_Text>();
            if (tmp != null) { results.Add((go, tmp.text, true)); return; }

            var legacy = go.GetComponent<Text>();
            if (legacy != null) results.Add((go, legacy.text, false));
        }

        // ──────────────────────────────────────────────
        //  Results display
        // ──────────────────────────────────────────────

        private void DrawResults()
        {
            int selectedCount = _scanResults.Count(e => e.Selected);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(
                $"Found {_scanResults.Count} text components, {selectedCount} selected",
                EditorStyles.boldLabel);

            if (GUILayout.Button("All", GUILayout.Width(40)))
                foreach (var e in _scanResults) e.Selected = true;
            if (GUILayout.Button("None", GUILayout.Width(45)))
                foreach (var e in _scanResults) e.Selected = false;
            if (GUILayout.Button("Invert", GUILayout.Width(50)))
                foreach (var e in _scanResults) e.Selected = !e.Selected;

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(4);

            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            EditorGUILayout.LabelField("", GUILayout.Width(20));
            EditorGUILayout.LabelField("Object", EditorStyles.miniLabel, GUILayout.Width(160));
            EditorGUILayout.LabelField("Text", EditorStyles.miniLabel, GUILayout.Width(130));
            EditorGUILayout.LabelField("Generated key", EditorStyles.miniLabel, GUILayout.MinWidth(160));
            EditorGUILayout.LabelField("Status", EditorStyles.miniLabel, GUILayout.Width(70));
            EditorGUILayout.EndHorizontal();

            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            foreach (var entry in _scanResults)
                DrawScanEntry(entry);

            EditorGUILayout.EndScrollView();
            EditorGUILayout.Space(8);

            GUI.enabled = selectedCount > 0;

            if (GUILayout.Button($"Apply ({selectedCount} components)", GUILayout.Height(28)))
                ApplySelected();

            GUI.enabled = true;
        }

        private void DrawScanEntry(ScanEntry entry)
        {
            Color prev = GUI.backgroundColor;
            if (entry.KeyExists) GUI.backgroundColor = new Color(1f, 0.92f, 0.7f);

            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);

            entry.Selected = EditorGUILayout.Toggle(entry.Selected, GUILayout.Width(20));

            var pathStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleLeft };
            if (GUILayout.Button(entry.Path, pathStyle, GUILayout.Width(160)))
            {
                if (entry.GameObject != null)
                {
                    Selection.activeGameObject = entry.GameObject;
                    EditorGUIUtility.PingObject(entry.GameObject);
                }
            }

            string preview = entry.Text.Replace("\n", " ");
            if (preview.Length > 25) preview = preview.Substring(0, 25) + "...";
            EditorGUILayout.LabelField(preview, EditorStyles.miniLabel, GUILayout.Width(130));

            entry.GeneratedKey = EditorGUILayout.TextField(entry.GeneratedKey, GUILayout.MinWidth(160));

            string status = entry.KeyExists ? "key exists"
                : (entry.GameObject != null && entry.GameObject.GetComponent<LocalizedComponentBase>() != null)
                    ? "localized" : "new";

            Color prevC = GUI.color;
            if (status == "new") GUI.color = new Color(0.3f, 0.8f, 0.3f);
            else if (status == "key exists") GUI.color = new Color(0.9f, 0.7f, 0.2f);
            EditorGUILayout.LabelField(status, EditorStyles.miniLabel, GUILayout.Width(70));
            GUI.color = prevC;

            EditorGUILayout.EndHorizontal();
            GUI.backgroundColor = prev;
        }

        // ──────────────────────────────────────────────
        //  Apply
        // ──────────────────────────────────────────────

        private void ApplySelected()
        {
            var toApply = _scanResults.Where(e => e.Selected).ToList();
            if (toApply.Count == 0) return;

            string defaultLang = _config?.DefaultLanguageCode;
            int keysCreated = 0;
            int componentsAdded = 0;
            int prefabsModified = 0;

            Undo.SetCurrentGroupName("Auto-Localize");
            int undoGroup = Undo.GetCurrentGroup();

            var prefabGroups = new Dictionary<string, List<(GameObject sceneObj, string key)>>();

            foreach (var entry in toApply)
            {
                if (entry.GameObject == null) continue;

                string key = entry.GeneratedKey;

                // Create key
                if (_data.GetFileForKey(key) == null)
                {
                    _data.AddKey(key, _targetFile);

                    if (!string.IsNullOrEmpty(defaultLang) && !string.IsNullOrEmpty(entry.Text))
                        _data.SetTranslation(key, defaultLang, entry.Text);

                    keysCreated++;
                }

                var existing = entry.GameObject.GetComponent<LocalizedText>();

                if (existing != null)
                {
                    // Already has component — just update key
                    SetKey(existing, key);
                    continue;
                }

                // Should we modify the prefab asset?
                if (_modifyPrefabAssets && PrefabUtility.IsPartOfPrefabInstance(entry.GameObject))
                {
                    // Get the source prefab asset path
                    GameObject nearestRoot = PrefabUtility.GetNearestPrefabInstanceRoot(entry.GameObject);
                    string prefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(nearestRoot);
                    
                    if (!string.IsNullOrEmpty(prefabPath))
                    {
                        if (!prefabGroups.ContainsKey(prefabPath))
                            prefabGroups[prefabPath] = new List<(GameObject, string)>();
                
                        prefabGroups[prefabPath].Add((entry.GameObject, key));
                        continue;
                    }
                }

                // Regular: add as instance override or scene object
                AddOrUpdateComponent(entry.GameObject, key);
                componentsAdded++;
            }

            // Process prefab groups — open each prefab once, modify all targets, save
            foreach (var kvp in prefabGroups)
            {
                string prefabPath = kvp.Key;
                var entries = kvp.Value;

                GameObject prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);
                bool modified = false;
                
                foreach (var (sceneObj, key) in entries)
                {
                    GameObject nearestRoot = PrefabUtility.GetNearestPrefabInstanceRoot(sceneObj);
                    string relativePath = GetRelativePath(sceneObj.transform, nearestRoot.transform);
            
                    Transform targetInContent = string.IsNullOrEmpty(relativePath) 
                        ? prefabRoot.transform 
                        : prefabRoot.transform.Find(relativePath);

                    if (targetInContent != null)
                    {
                        var comp = targetInContent.GetComponent<LocalizedText>();
                        if (comp == null) comp = targetInContent.gameObject.AddComponent<LocalizedText>();
                        SetKey(comp, key);
                
                        modified = true;
                        componentsAdded++;
                    }
                }

                if (modified)
                {
                    PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
                    prefabsModified++;
                }

                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }

            _data.SaveFileAllLanguages(_targetFile);
            _data.SaveMeta();

            Undo.CollapseUndoOperations(undoGroup);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorDataCache.Invalidate();

            string prefabInfo = prefabsModified > 0
                ? $"\nModified {prefabsModified} prefab asset(s)"
                : "";

            EditorUtility.DisplayDialog("Auto-Localize complete",
                $"Created {keysCreated} translation key(s)\n" +
                $"Added {componentsAdded} LocalizedText component(s){prefabInfo}\n" +
                $"File: {_targetFile}.json | Language: {defaultLang ?? "(not set)"}",
                "OK");

            RunScan();
        }
        
        private void AddOrUpdateComponent(GameObject go, string key)
        {
            var comp = go.GetComponent<LocalizedText>();
            if (comp == null) comp = Undo.AddComponent<LocalizedText>(go);
            SetKey(comp, key);
        }

        private string GetRelativePath(Transform child, Transform root)
        {
            if (child == root) return "";
    
            var pathParts = new List<string>();
            Transform current = child;

            while (current != null && current != root)
            {
                pathParts.Add(current.name);
                current = current.parent;
            }

            pathParts.Reverse();
            return string.Join("/", pathParts);
        }

        private static void SetKey(LocalizedText comp, string key)
        {
            var so = new SerializedObject(comp);
            var prop = so.FindProperty("_key");
            if (prop != null) { prop.stringValue = key; so.ApplyModifiedProperties(); }
        }

        // ──────────────────────────────────────────────
        //  Key generation
        // ──────────────────────────────────────────────

        private string GenerateKey(GameObject go)
        {
            string raw;

            switch (_keyPattern)
            {
                case KeyPattern.SceneParentName:
                    string sceneName = go.scene.IsValid() ? go.scene.name : "Prefab";
                    raw = $"{sceneName}/{GetParentName(go)}/{go.name}";
                    break;
                case KeyPattern.ParentName:
                    raw = $"{GetParentName(go)}/{go.name}";
                    break;
                case KeyPattern.ObjectName:
                    raw = go.name;
                    break;
                case KeyPattern.Custom:
                    raw = _customPattern
                        .Replace("{scene}", go.scene.IsValid() ? go.scene.name : "Prefab")
                        .Replace("{parent}", GetParentName(go))
                        .Replace("{name}", go.name)
                        .Replace("{text}", Truncate(GetText(go), 20));
                    break;
                default:
                    raw = go.name;
                    break;
            }

            if (!string.IsNullOrEmpty(_keyPrefix))
                raw = _keyPrefix.TrimEnd('/') + "/" + raw;

            raw = Regex.Replace(raw, @"[^\w/\-]", "_");
            raw = Regex.Replace(raw, @"/+", "/").Trim('/');

            string key = raw;
            int suffix = 1;

            while (_data.GetFileForKey(key) != null
                || (_scanResults != null && _scanResults.Any(
                    e => e.GeneratedKey == key && e.GameObject != go)))
            {
                key = $"{raw}_{suffix++}";
            }

            return key;
        }

        private static string GetParentName(GameObject go) =>
            go.transform.parent != null ? go.transform.parent.name : "Root";

        private static string GetText(GameObject go)
        {
            var tmp = go.GetComponent<TMP_Text>();
            if (tmp != null) return tmp.text;
            var legacy = go.GetComponent<Text>();
            return legacy != null ? legacy.text : "";
        }

        private static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "empty";
            s = Regex.Replace(s, @"\s+", "_");
            return s.Length > max ? s.Substring(0, max) : s;
        }

        private static bool IsNumericOnly(string text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (!char.IsDigit(c) && !char.IsWhiteSpace(c)
                    && !char.IsPunctuation(c) && !char.IsSymbol(c))
                    return false;
            }
            return true;
        }

        private static string GetHierarchyPath(GameObject go)
        {
            var parts = new List<string>();
            var current = go.transform;
            while (current != null) { parts.Add(current.name); current = current.parent; }
            parts.Reverse();
            string path = string.Join("/", parts);
            return path.Length > 35 ? "..." + path.Substring(path.Length - 32) : path;
        }

        private class ScanEntry
        {
            public GameObject GameObject;
            public string Text;
            public bool IsTMP;
            public string GeneratedKey;
            public bool Selected;
            public bool KeyExists;
            public string Path;
        }
    }
}