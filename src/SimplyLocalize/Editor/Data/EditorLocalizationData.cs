using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SimplyLocalize.Data;
using UnityEngine;

namespace SimplyLocalize.Editor.Data
{
    /// <summary>
    /// Manages all localization data for the editor.
    /// Unlike runtime (which loads one language at a time), the editor loads ALL languages simultaneously.
    /// Reads JSON files directly from disk (not through Resources.Load).
    ///
    /// Tracks which key belongs to which source file so changes are saved back correctly.
    /// </summary>
    public class EditorLocalizationData
    {
        /// <summary>key → sourceFileName (without extension, e.g. "menu", "game")</summary>
        private readonly Dictionary<string, string> _keyToFile = new();

        /// <summary>languageCode → (key → translatedValue)</summary>
        private readonly Dictionary<string, Dictionary<string, string>> _translations = new();

        /// <summary>List of discovered source file names (e.g. "menu", "game")</summary>
        private readonly List<string> _sourceFiles = new();

        /// <summary>key → metadata (description, previousKeys)</summary>
        private readonly Dictionary<string, KeyMeta> _keyMeta = new();
        private string _metaLoadError;
        private string _metaSnapshot;

        // ── Caches (for performance) ──

        /// <summary>Cached list of all keys, sorted alphabetically. Invalidated on add/remove/rename.</summary>
        private List<string> _sortedKeysCache;

        /// <summary>Cached search-index: key → lowercased concatenation of (key + all translations).</summary>
        private readonly Dictionary<string, string> _searchIndex = new();
        private bool _searchIndexValid;

        /// <summary>True if there are pending writes to disk that AssetDatabase hasn't reimported yet.</summary>
        public bool HasPendingAssetRefresh { get; private set; }

        /// <summary>Marks that JSON files were written and need AssetDatabase reimport.</summary>
        public void MarkPendingAssetRefresh() => HasPendingAssetRefresh = true;

        /// <summary>
        /// Flushes all pending AssetDatabase.Refresh() calls accumulated from individual edits.
        /// Deferring Refresh avoids ~90ms freeze per FocusOut.
        /// </summary>
        public void FlushPendingAssetRefresh()
        {
            if (!HasPendingAssetRefresh) return;
            HasPendingAssetRefresh = false;
            UnityEditor.AssetDatabase.Refresh();
        }

        /// <summary>The base path to the Localization folder inside Assets/Resources/</summary>
        private string _basePath;

        /// <summary>Reference language code used to discover files and keys</summary>
        private string _referenceLanguage;

        // ──────────────────────────────────────────────
        //  Key Metadata
        // ──────────────────────────────────────────────

        [Serializable]
        public class KeyMeta
        {
            public string description;
            public List<string> previousKeys;
            internal Newtonsoft.Json.Linq.JObject AdditionalFields;

            public bool IsEmpty =>
                string.IsNullOrEmpty(description)
                && (previousKeys == null || previousKeys.Count == 0)
                && (AdditionalFields == null || !AdditionalFields.HasValues);
        }

        // ──────────────────────────────────────────────
        //  Public accessors
        // ──────────────────────────────────────────────

        public IReadOnlyList<string> SourceFiles => _sourceFiles;
        public IReadOnlyDictionary<string, string> KeyToFile => _keyToFile;
        public IReadOnlyDictionary<string, Dictionary<string, string>> Translations => _translations;
        public string BasePath => _basePath;

        /// <summary>All unique keys across all languages.</summary>
        public IEnumerable<string> AllKeys => _keyToFile.Keys;

        /// <summary>All keys sorted alphabetically. Cached — fast O(1) on repeat calls.</summary>
        public IReadOnlyList<string> AllKeysSorted
        {
            get
            {
                if (_sortedKeysCache == null)
                {
                    _sortedKeysCache = _keyToFile.Keys.ToList();
                    _sortedKeysCache.Sort(System.StringComparer.Ordinal);
                }
                return _sortedKeysCache;
            }
        }

        /// <summary>Returns the search-index entry for a key (lowercased key + all translations concatenated).</summary>
        public string GetSearchHaystack(string key)
        {
            if (!_searchIndexValid) RebuildSearchIndex();
            return _searchIndex.TryGetValue(key, out var v) ? v : "";
        }

        private void RebuildSearchIndex()
        {
            _searchIndex.Clear();
            var sb = new System.Text.StringBuilder();

            foreach (var key in _keyToFile.Keys)
            {
                sb.Clear();
                sb.Append(key.ToLowerInvariant());

                foreach (var langData in _translations.Values)
                {
                    if (langData.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v))
                    {
                        sb.Append(' ');
                        sb.Append(v.ToLowerInvariant());
                    }
                }

                _searchIndex[key] = sb.ToString();
            }

            _searchIndexValid = true;
        }

        private void InvalidateCaches()
        {
            _sortedKeysCache = null;
            _searchIndexValid = false;
        }

        /// <summary>Total number of keys.</summary>
        public int KeyCount => _keyToFile.Count;

        // ──────────────────────────────────────────────
        //  Loading
        // ──────────────────────────────────────────────

        /// <summary>
        /// Loads all localization data from disk.
        /// </summary>
        /// <param name="config">The localization config asset.</param>
        public void Load(LocalizationConfig config)
        {
            Clear();

            if (config == null)
                return;

            _basePath = FindLocalizationBasePath(config.resourcesBasePath);

            if (string.IsNullOrEmpty(_basePath))
            {
                Debug.LogWarning("[SimplyLocalize Editor] Could not find localization folder in Resources");
                return;
            }

            _referenceLanguage = config.DefaultLanguageCode
                ?? (config.languages.Count > 0 ? config.languages[0].Code : null);

            if (string.IsNullOrEmpty(_referenceLanguage))
                return;

            // Discover files from reference language
            DiscoverSourceFiles(_referenceLanguage);

            // Load all languages
            foreach (var profile in config.languages)
            {
                if (profile == null) continue;
                LoadLanguage(profile.Code);
            }

            // Load key metadata
            LoadMeta();
        }

        /// <summary>
        /// Reloads all data from disk.
        /// </summary>
        public void Reload(LocalizationConfig config)
        {
            Load(config);
        }

        public void Clear()
        {
            _keyToFile.Clear();
            _translations.Clear();
            _sourceFiles.Clear();
            _keyMeta.Clear();
            _metaLoadError = null;
            _metaSnapshot = null;
            _basePath = null;
            _referenceLanguage = null;
            InvalidateCaches();
        }

        // ──────────────────────────────────────────────
        //  Queries
        // ──────────────────────────────────────────────

        /// <summary>Returns the translation for a key in a specific language, or null.</summary>
        public string GetTranslation(string key, string languageCode)
        {
            if (_translations.TryGetValue(languageCode, out var langData)
                && langData.TryGetValue(key, out var value))
                return value;

            return null;
        }

        /// <summary>Returns all keys belonging to a specific source file.</summary>
        public IEnumerable<string> GetKeysForFile(string fileName)
        {
            return _keyToFile
                .Where(kvp => kvp.Value == fileName)
                .Select(kvp => kvp.Key);
        }

        /// <summary>Returns all keys matching multiple source files.</summary>
        public IEnumerable<string> GetKeysForFiles(IEnumerable<string> fileNames)
        {
            var set = new HashSet<string>(fileNames);
            return _keyToFile
                .Where(kvp => set.Contains(kvp.Value))
                .Select(kvp => kvp.Key);
        }

        /// <summary>Returns the source file name for a key, or null.</summary>
        public string GetFileForKey(string key)
        {
            return _keyToFile.TryGetValue(key, out var file) ? file : null;
        }

        /// <summary>Returns all language codes that have been loaded.</summary>
        public IEnumerable<string> LoadedLanguages => _translations.Keys;

        // ──────────────────────────────────────────────
        //  Metadata queries
        // ──────────────────────────────────────────────

        /// <summary>Returns the description for a key, or null.</summary>
        public string GetDescription(string key)
        {
            return _keyMeta.TryGetValue(key, out var meta) ? meta.description : null;
        }

        /// <summary>Sets the description for a key.</summary>
        public void SetDescription(string key, string description)
        {
            if (!_keyMeta.TryGetValue(key, out var meta))
            {
                meta = new KeyMeta();
                _keyMeta[key] = meta;
            }

            meta.description = description;

            if (meta.IsEmpty)
                _keyMeta.Remove(key);
        }

        /// <summary>Returns previous key names (from renames), or empty.</summary>
        public IReadOnlyList<string> GetPreviousKeys(string key)
        {
            if (_keyMeta.TryGetValue(key, out var meta) && meta.previousKeys != null)
                return meta.previousKeys;

            return Array.Empty<string>();
        }

        /// <summary>Returns full metadata for a key, or null.</summary>
        public KeyMeta GetMeta(string key)
        {
            return _keyMeta.TryGetValue(key, out var meta) ? meta : null;
        }

        /// <summary>
        /// Finds a key that has the given oldKey in its previousKeys list.
        /// Used for detecting renamed keys on components with stale references.
        /// </summary>
        public string FindKeyByPreviousName(string oldKey)
        {
            foreach (var kvp in _keyMeta)
            {
                if (kvp.Value.previousKeys != null && kvp.Value.previousKeys.Contains(oldKey))
                    return kvp.Key;
            }

            return null;
        }

        // ──────────────────────────────────────────────
        //  Mutations (called by the editor UI)
        // ──────────────────────────────────────────────

        /// <summary>
        /// Sets a translation value. Does NOT save to disk — call SaveFile() after.
        /// </summary>
        public void SetTranslation(string key, string languageCode, string value)
        {
            if (!_translations.TryGetValue(languageCode, out var langData))
            {
                langData = new Dictionary<string, string>();
                _translations[languageCode] = langData;
            }

            langData[key] = value;
            _searchIndexValid = false; // translation changed → search index stale
        }

        /// <summary>
        /// Adds a new key assigned to the specified source file.
        /// Auto-registers the source file if it doesn't exist yet.
        /// </summary>
        public void AddKey(string key, string sourceFile)
        {
            if (_keyToFile.ContainsKey(key))
                return;

            // Auto-register source file
            if (!_sourceFiles.Contains(sourceFile))
                _sourceFiles.Add(sourceFile);

            _keyToFile[key] = sourceFile;

            // Add empty entries for all languages
            foreach (var langData in _translations.Values)
            {
                if (!langData.ContainsKey(key))
                    langData[key] = "";
            }

            InvalidateCaches();
        }

        /// <summary>
        /// Removes a key from all languages and its metadata.
        /// </summary>
        public void RemoveKey(string key)
        {
            _keyToFile.Remove(key);
            _keyMeta.Remove(key);

            foreach (var langData in _translations.Values)
                langData.Remove(key);

            InvalidateCaches();
        }

        /// <summary>
        /// Renames a key across all languages.
        /// Stores the old key in metadata.previousKeys for reference tracking.
        /// </summary>
        public void RenameKey(string oldKey, string newKey)
        {
            if (!_keyToFile.ContainsKey(oldKey) || _keyToFile.ContainsKey(newKey))
                return;

            _keyToFile[newKey] = _keyToFile[oldKey];
            _keyToFile.Remove(oldKey);

            foreach (var langData in _translations.Values)
            {
                if (langData.TryGetValue(oldKey, out var value))
                {
                    langData[newKey] = value;
                    langData.Remove(oldKey);
                }
            }

            // Transfer meta and record old key name
            if (!_keyMeta.TryGetValue(newKey, out var meta))
            {
                meta = _keyMeta.TryGetValue(oldKey, out var oldMeta) ? oldMeta : new KeyMeta();
                _keyMeta[newKey] = meta;
            }

            _keyMeta.Remove(oldKey);

            meta.previousKeys ??= new List<string>();

            if (!meta.previousKeys.Contains(oldKey))
                meta.previousKeys.Add(oldKey);

            InvalidateCaches();
        }

        /// <summary>
        /// Moves a key to a different source file.
        /// </summary>
        public void MoveKeyToFile(string key, string newFile)
        {
            if (_keyToFile.ContainsKey(key))
                _keyToFile[key] = newFile;
        }

        /// <summary>
        /// Creates a new empty source file across all languages.
        /// </summary>
        public void CreateSourceFile(string fileName)
        {
            if (_sourceFiles.Contains(fileName))
                return;

            _sourceFiles.Add(fileName);
        }

        // ──────────────────────────────────────────────
        //  Persistence
        // ──────────────────────────────────────────────

        /// <summary>
        /// Saves a specific source file for a specific language to disk.
        /// </summary>
        public void SaveFile(string fileName, string languageCode)
        {
            string filePath = GetJsonFilePath(languageCode, fileName);

            if (string.IsNullOrEmpty(filePath))
                return;

            var keysInFile = _keyToFile
                .Where(kvp => kvp.Value == fileName)
                .Select(kvp => kvp.Key)
                .OrderBy(k => k);

            var translations = new Dictionary<string, string>();

            if (_translations.TryGetValue(languageCode, out var langData))
            {
                foreach (var key in keysInFile)
                {
                    if (langData.TryGetValue(key, out var value))
                        translations[key] = value;
                    else
                        translations[key] = "";
                }
            }

            string json = EditorJsonWriter.WriteTranslations(translations);

            string directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(filePath, json);
        }

        /// <summary>
        /// Saves a source file for ALL languages.
        /// </summary>
        public void SaveFileAllLanguages(string fileName)
        {
            foreach (var langCode in _translations.Keys)
                SaveFile(fileName, langCode);
        }

        /// <summary>
        /// Saves everything to disk.
        /// </summary>
        public void SaveAll()
        {
            foreach (var fileName in _sourceFiles)
                SaveFileAllLanguages(fileName);

            SaveMeta();
        }

        // ──────────────────────────────────────────────
        //  Internal
        // ──────────────────────────────────────────────

        private void DiscoverSourceFiles(string languageCode)
        {
            string textFolder = Path.Combine(_basePath, languageCode, "text");

            if (!Directory.Exists(textFolder))
            {
                Debug.LogWarning($"[SimplyLocalize Editor] Text folder not found: {textFolder}");
                return;
            }

            var jsonFiles = Directory.GetFiles(textFolder, "*.json");

            foreach (var filePath in jsonFiles)
            {
                string fileName = Path.GetFileNameWithoutExtension(filePath);

                if (!_sourceFiles.Contains(fileName))
                    _sourceFiles.Add(fileName);
            }

            _sourceFiles.Sort();
        }

        private void LoadLanguage(string languageCode)
        {
            var langData = new Dictionary<string, string>();

            foreach (var fileName in _sourceFiles)
            {
                string filePath = GetJsonFilePath(languageCode, fileName);

                if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                    continue;

                string json = File.ReadAllText(filePath);
                var parsed = LocalizationFileParser.Parse(json);

                foreach (var kvp in parsed)
                {
                    langData[kvp.Key] = kvp.Value;

                    // Track key → file mapping (first language to define it wins)
                    if (!_keyToFile.ContainsKey(kvp.Key))
                        _keyToFile[kvp.Key] = fileName;
                }
            }

            _translations[languageCode] = langData;
        }

        private string GetJsonFilePath(string languageCode, string fileName)
        {
            if (string.IsNullOrEmpty(_basePath))
                return null;

            return Path.Combine(_basePath, languageCode, "text", fileName + ".json");
        }

        /// <summary>
        /// Finds the absolute path to the localization base folder.
        /// Searches all Resources folders in the project.
        /// </summary>
        private static string FindLocalizationBasePath(string resourcesSubPath)
        {
            string[] resourcesDirs = Directory.GetDirectories(
                Application.dataPath, "Resources", SearchOption.AllDirectories);

            foreach (var resDir in resourcesDirs)
            {
                string candidate = Path.Combine(resDir, resourcesSubPath);

                if (Directory.Exists(candidate))
                    return candidate;
            }

            // Try direct path
            string directPath = Path.Combine(Application.dataPath, "Resources", resourcesSubPath);
            if (Directory.Exists(directPath))
                return directPath;

            return null;
        }

        // ──────────────────────────────────────────────
        //  Metadata persistence
        // ──────────────────────────────────────────────

        private string GetMetaFilePath()
        {
            return string.IsNullOrEmpty(_basePath) ? null : Path.Combine(_basePath, "_meta.json");
        }

        private void LoadMeta()
        {
            _keyMeta.Clear();
            _metaLoadError = null;
            _metaSnapshot = null;
            string path = GetMetaFilePath();
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                if (!File.Exists(path)) return;
                string json = File.ReadAllText(path, new System.Text.UTF8Encoding(false, true));
                // Parse and validate the entire document before publishing any entries.
                var parsed = EditorMetadataJson.Parse(json);
                foreach (var pair in parsed) _keyMeta.Add(pair.Key, pair.Value);
                _metaSnapshot = json;
            }
            catch (Exception error)
            {
                _metaLoadError = error.Message;
                Debug.LogWarning($"[SimplyLocalize Editor] Failed to load meta '{path}': {error.Message}. Metadata saving is blocked; repair the file and reload.");
            }
        }

        /// <summary>
        /// Saves key metadata to _meta.json. Throws if loading failed, the file changed externally,
        /// or writing failed. A failed write preserves the original file and pending metadata edits.
        /// </summary>
        public void SaveMeta()
        {
            string path = GetMetaFilePath();
            if (string.IsNullOrEmpty(path))
                throw new InvalidOperationException("Cannot save _meta.json without a localization base path. Load a configured localization folder first.");
            if (_metaLoadError != null)
                throw new InvalidOperationException($"Cannot save metadata '{path}' after a load error: {_metaLoadError}. Repair the file and reload before saving.");

            EnsureMetaUnchanged(path);
            // Serialize before touching the file or pruning mutable public KeyMeta entries.
            string json = EditorMetadataJson.Serialize(_keyMeta);
            bool changed = json != _metaSnapshot;
            if (changed)
            {
                if (json == null)
                {
                    EnsureMetaUnchanged(path);
                    File.Delete(path);
                }
                else
                {
                    string temporary = Path.Combine(Path.GetDirectoryName(path),
                        "." + Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
                    try
                    {
                        using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        {
                            var bytes = new System.Text.UTF8Encoding(false, true).GetBytes(json);
                            stream.Write(bytes, 0, bytes.Length);
                            stream.Flush(true);
                        }
                        EnsureMetaUnchanged(path);
                        // Same-directory replacement is atomic. Never delete the destination first.
                        if (_metaSnapshot != null) File.Replace(temporary, path, null);
                        else File.Move(temporary, path);
                    }
                    finally
                    {
                        if (File.Exists(temporary)) File.Delete(temporary);
                    }
                }
                MarkPendingAssetRefresh();
            }
            _metaSnapshot = json;
            foreach (var key in _keyMeta.Where(pair => pair.Value.IsEmpty).Select(pair => pair.Key).ToList())
                _keyMeta.Remove(key);
        }

        private void EnsureMetaUnchanged(string path)
        {
            string current = File.Exists(path) ? File.ReadAllText(path, new System.Text.UTF8Encoding(false, true)) : null;
            if (!string.Equals(current, _metaSnapshot, StringComparison.Ordinal))
                throw new InvalidOperationException($"Metadata '{path}' changed on disk. Reload before saving to avoid overwriting external changes.");
        }
    }
}
