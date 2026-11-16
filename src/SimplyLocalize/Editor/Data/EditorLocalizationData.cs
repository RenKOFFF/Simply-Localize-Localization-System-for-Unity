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
        private byte[] _metaFileSnapshot;
        private readonly Dictionary<string, byte[]> _fileSnapshots = new(StringComparer.OrdinalIgnoreCase);

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
        internal string LoadedResourcesPath { get; private set; }

        /// <summary>Reference language code used to discover files and keys</summary>
        private string _referenceLanguage;
        private string _loadError;
        private readonly Dictionary<string, string> _discoveredPaths = new(StringComparer.Ordinal);
        private readonly HashSet<string> _discoveredFiles = new(StringComparer.Ordinal);

        /// <summary>Discovery failed; no partial dataset is published and saving is blocked until reload.</summary>
        public string LoadError => _loadError;

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
            if (config == null) return;
            try
            {
                var errors = config.GetValidationErrors();
                if (errors.Count > 0) throw new InvalidOperationException(string.Join("\n", errors));
                LoadedResourcesPath = config.resourcesBasePath;
                _basePath = FindLocalizationBasePath(LoadedResourcesPath);
                if (string.IsNullOrEmpty(_basePath))
                {
                    // Empty projects have no data yet. Pick the same location as Setup without creating files.
                    string configPath = UnityEditor.AssetDatabase.GetAssetPath(config);
                    string parent = configPath.StartsWith("Assets/", StringComparison.Ordinal)
                        ? Path.GetDirectoryName(Path.GetFullPath(configPath)) : Application.dataPath;
                    string resourceRoot = Path.GetFileName(parent) == "Resources" ? parent : Path.Combine(parent, "Resources");
                    _basePath = Path.GetFullPath(Path.Combine(resourceRoot, LoadedResourcesPath));
                }
                var profiles = (config.languages ?? new List<LanguageProfile>()).Where(p => p != null).Distinct().ToList();
                _referenceLanguage = config.DefaultLanguageCode ?? profiles.FirstOrDefault()?.Code;
                foreach (var profile in profiles) DiscoverSourceFiles(profile.Code);
                _sourceFiles.Sort(StringComparer.Ordinal);
                foreach (var profile in profiles) LoadLanguage(profile.Code);
                LoadMeta();
            }
            catch (Exception error)
            {
                Clear();
                LoadedResourcesPath = config.resourcesBasePath;
                _loadError = error.Message;
                Debug.LogWarning("[SimplyLocalize Editor] Cannot load localization data: " + _loadError + ". Saving is blocked; resolve the problem and reload.");
            }
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
            _metaFileSnapshot = null;
            _fileSnapshots.Clear();
            _basePath = null;
            LoadedResourcesPath = null;
            _referenceLanguage = null;
            _loadError = null;
            _discoveredFiles.Clear();
            _discoveredPaths.Clear();
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

        internal void SetSnapshotTranslation(string key, string language, string value, string file)
        {
            _keyToFile[key] = file;
            if (!_sourceFiles.Contains(file)) _sourceFiles.Add(file);
            SetTranslation(key, language, value);
            InvalidateCaches();
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

        internal void EnsureLanguage(string code)
        {
            if (!_translations.ContainsKey(code)) _translations.Add(code, new Dictionary<string, string>());
        }

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
            SaveFiles(new[] { (fileName, languageCode) }, false);
        }

        /// <summary>Saves a source file for all loaded languages as one rollback-capable operation.</summary>
        public void SaveFileAllLanguages(string fileName)
        {
            SaveFiles(_translations.Keys.Select(code => (fileName, code)), false);
        }

        /// <summary>Saves translations and metadata together; failures preserve pending memory edits.</summary>
        public void SaveAll() => SaveAll(null);

        internal void SaveAll(Action afterWrite, Action<EditorFileTransaction> enlist = null)
        {
            SaveFiles(_sourceFiles.SelectMany(file => _translations.Keys.Select(code => (file, code))), true, afterWrite, enlist);
        }

        internal void SaveRenamedFile(string file, Action afterWrite, Action<EditorFileTransaction> enlist) =>
            SaveFiles(_translations.Keys.Select(code => (file, code)), true, afterWrite, enlist);

        private void SaveFiles(IEnumerable<(string file, string language)> files, bool includeMetadata, Action afterWrite = null, Action<EditorFileTransaction> enlist = null)
        {
            if (_loadError != null) throw new InvalidOperationException("Cannot save after a discovery error: " + _loadError + ". Reload first.");
            if (LoadedResourcesPath != null) FindLocalizationBasePath(LoadedResourcesPath); // Recheck ambiguous roots introduced after loading.
            var writes = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var (file, language) in files)
            {
                string path = GetJsonFilePath(language, file);
                if (!_translations.ContainsKey(language)) throw new InvalidOperationException("Language was not loaded: " + language);
                var values = new Dictionary<string, string>();
                foreach (var key in _keyToFile.Where(pair => pair.Value == file).Select(pair => pair.Key))
                    values[key] = GetTranslation(key, language) ?? "";
                string json = EditorJsonWriter.WriteTranslations(values);
                // Keep user-defined top-level fields from a loaded document.
                if (_fileSnapshots.TryGetValue(path, out var original) && original != null)
                {
                    using var reader = new Newtonsoft.Json.JsonTextReader(new StringReader(DecodeFile(original)))
                    { DateParseHandling = Newtonsoft.Json.DateParseHandling.None, FloatParseHandling = Newtonsoft.Json.FloatParseHandling.Decimal };
                    var document = Newtonsoft.Json.Linq.JObject.Load(reader, new Newtonsoft.Json.Linq.JsonLoadSettings
                    { DuplicatePropertyNameHandling = Newtonsoft.Json.Linq.DuplicatePropertyNameHandling.Error });
                    if (reader.Read()) throw new FormatException("Trailing JSON content in " + path);
                    document["translations"] = Newtonsoft.Json.Linq.JObject.Parse(json)["translations"];
                    json = document.ToString(Newtonsoft.Json.Formatting.Indented);
                }
                if (writes.ContainsKey(path)) throw new InvalidOperationException("Conflicting file names: " + path);
                writes.Add(path, EncodeFile(json));
            }
            string metaJson = null, metaPath = null;
            if (includeMetadata)
            {
                metaPath = EditorDataPath.Resolve(_basePath, "_meta.json");
                if (_metaLoadError != null)
                    throw new InvalidOperationException($"Cannot save metadata '{metaPath}' after a load error: {_metaLoadError}. Repair the file and reload.");
                EnsureMetaUnchanged(metaPath);
                metaJson = EditorMetadataJson.Serialize(_keyMeta);
            }
            using var transaction = new EditorFileTransaction();
            foreach (var write in writes)
            {
                _fileSnapshots.TryGetValue(write.Key, out var before);
                transaction.Add(write.Key, before, write.Value);
            }
            if (includeMetadata) transaction.Add(metaPath, _metaFileSnapshot, EncodeFile(metaJson));
            enlist?.Invoke(transaction);
            transaction.Execute(afterWrite);
            foreach (var write in writes) _fileSnapshots[write.Key] = write.Value;
            if (includeMetadata)
            {
                _metaSnapshot = metaJson;
                _metaFileSnapshot = EncodeFile(metaJson);
                foreach (var key in _keyMeta.Where(pair => pair.Value.IsEmpty).Select(pair => pair.Key).ToList()) _keyMeta.Remove(key);
            }
            MarkPendingAssetRefresh();
        }

        internal static byte[] EncodeFile(string text) => text == null ? null : new System.Text.UTF8Encoding(false, true).GetBytes(text);

        private static string DecodeFile(byte[] bytes)
        {
            using var stream = new MemoryStream(bytes);
            using var reader = new StreamReader(stream, new System.Text.UTF8Encoding(false, true), true);
            return reader.ReadToEnd();
        }

        internal EditorLocalizationData Clone()
        {
            var copy = new EditorLocalizationData();
            copy.CopyFrom(this);
            return copy;
        }

        internal void CopyFrom(EditorLocalizationData source)
        {
            _basePath = source._basePath;
            LoadedResourcesPath = source.LoadedResourcesPath;
            _referenceLanguage = source._referenceLanguage;
            _loadError = source._loadError;
            _discoveredFiles.Clear(); _discoveredFiles.UnionWith(source._discoveredFiles);
            _discoveredPaths.Clear(); foreach (var pair in source._discoveredPaths) _discoveredPaths.Add(pair.Key, pair.Value);
            _metaLoadError = source._metaLoadError;
            _metaSnapshot = source._metaSnapshot;
            _metaFileSnapshot = source._metaFileSnapshot;
            HasPendingAssetRefresh = source.HasPendingAssetRefresh;
            _sourceFiles.Clear(); _sourceFiles.AddRange(source._sourceFiles);
            _keyToFile.Clear(); foreach (var pair in source._keyToFile) _keyToFile.Add(pair.Key, pair.Value);
            _translations.Clear(); foreach (var pair in source._translations) _translations.Add(pair.Key, new Dictionary<string, string>(pair.Value));
            _fileSnapshots.Clear(); foreach (var pair in source._fileSnapshots) _fileSnapshots.Add(pair.Key, pair.Value);
            _keyMeta.Clear();
            foreach (var pair in source._keyMeta)
                _keyMeta.Add(pair.Key, new KeyMeta { description = pair.Value.description,
                    previousKeys = pair.Value.previousKeys == null ? null : new List<string>(pair.Value.previousKeys),
                    AdditionalFields = (Newtonsoft.Json.Linq.JObject)pair.Value.AdditionalFields?.DeepClone() });
            InvalidateCaches();
        }

        internal string StateSignature() => Newtonsoft.Json.JsonConvert.SerializeObject(new
        {
            path = _basePath, reference = _referenceLanguage, loadError = _loadError, discoveredFiles = _discoveredFiles, files = _sourceFiles, keys = _keyToFile,
            translations = _translations, metadata = EditorMetadataJson.Serialize(_keyMeta),
            metaError = _metaLoadError, metaSnapshot = _metaSnapshot
        });

        // ──────────────────────────────────────────────
        //  Internal
        // ──────────────────────────────────────────────

        private void DiscoverSourceFiles(string languageCode)
        {
            EditorDataPath.ValidateName(languageCode, "language code");
            string textFolder = EditorDataPath.Resolve(_basePath, languageCode + "/text");
            if (!Directory.Exists(textFolder)) return;
            foreach (var filePath in TextFiles(textFolder)
                .Where(p => string.Equals(Path.GetExtension(p), ".json", StringComparison.OrdinalIgnoreCase)).OrderBy(p => p, StringComparer.Ordinal))
            {
                string fileName = Path.GetRelativePath(textFolder, filePath).Replace('\\', '/');
                fileName = fileName.Substring(0, fileName.Length - 5);
                foreach (string part in fileName.Split('/')) EditorDataPath.ValidateName(part, "source file path segment");
                EditorDataPath.Resolve(_basePath, languageCode + "/text/" + fileName + ".json");
                if (_sourceFiles.Any(f => f != fileName && string.Equals(f, fileName, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException("Source file paths differ only in case: " + fileName);
                if (!_sourceFiles.Contains(fileName)) _sourceFiles.Add(fileName);
                _discoveredFiles.Add(fileName);
                string identity = languageCode + "/" + fileName;
                if (_discoveredPaths.ContainsKey(identity)) throw new InvalidOperationException("Duplicate source file: " + filePath);
                _discoveredPaths.Add(identity, filePath);
            }
        }

        private static IEnumerable<string> TextFiles(string directory)
        {
            foreach (string file in Directory.GetFiles(directory)) yield return file;
            foreach (string child in Directory.GetDirectories(directory))
            {
                if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Localization folders cannot traverse a symbolic link or junction: " + child);
                foreach (string file in TextFiles(child)) yield return file;
            }
        }

        private void LoadLanguage(string languageCode)
        {
            var langData = new Dictionary<string, string>();

            foreach (var fileName in _sourceFiles)
            {
                string filePath = GetJsonFilePath(languageCode, fileName);

                byte[] original = EditorFileTransaction.Read(filePath);
                _fileSnapshots[filePath] = original;
                if (original == null)
                    continue;

                string json = DecodeFile(original);
                Dictionary<string, string> parsed;
                try
                {
                    using var reader = new Newtonsoft.Json.JsonTextReader(new StringReader(json));
                    var document = Newtonsoft.Json.Linq.JObject.Load(reader, new Newtonsoft.Json.Linq.JsonLoadSettings
                    { DuplicatePropertyNameHandling = Newtonsoft.Json.Linq.DuplicatePropertyNameHandling.Error });
                    if (reader.Read()) throw new FormatException("Trailing JSON content.");
                    if (document["translations"] != null && !(document["translations"] is Newtonsoft.Json.Linq.JObject))
                        throw new FormatException("Expected a translations object.");
                    parsed = LocalizationFileParser.Parse(json);
                }
                catch (Exception error) { throw new FormatException("Invalid translation file '" + filePath + "': " + error.Message, error); }

                foreach (var kvp in parsed)
                {
                    langData[kvp.Key] = kvp.Value;

                    if (_keyToFile.TryGetValue(kvp.Key, out string owner) && owner != fileName)
                        throw new InvalidOperationException("Key '" + kvp.Key + "' belongs to both '" + owner + "' and '" + fileName + "'. Use the same source file for a key in every language.");
                    _keyToFile[kvp.Key] = fileName;
                }
            }

            _translations[languageCode] = langData;
        }

        private string GetJsonFilePath(string languageCode, string fileName)
        {
            EditorDataPath.ValidateName(languageCode, "language code");
            ValidateSourceFileName(fileName);
            if (_discoveredPaths.TryGetValue(languageCode + "/" + fileName, out string existing))
                return EditorDataPath.Resolve(_basePath, Path.GetRelativePath(_basePath, existing));
            return EditorDataPath.Resolve(_basePath, Path.Combine(languageCode, "text", fileName + ".json"));
        }

        // Permit existing nested sources without widening the contract for new file names.
        internal void ValidateSourceFileName(string fileName)
        {
            // Existing nested files can round-trip through CSV. New names retain the single-file contract.
            if (_discoveredFiles.Contains(fileName))
                foreach (string part in fileName.Split('/')) EditorDataPath.ValidateName(part, "source file path segment");
            else EditorDataPath.ValidateName(fileName, "source file name");
        }

        private static string FindLocalizationBasePath(string resourcesSubPath)
        {
            var candidates = new SortedSet<string>(StringComparer.Ordinal);
            foreach (string resourceRoot in ResourceDirectories(Application.dataPath))
            {
                string candidate = Path.GetFullPath(Path.Combine(resourceRoot, resourcesSubPath));
                if (Directory.Exists(candidate)) candidates.Add(candidate);
            }
            // Packages use virtual AssetDatabase paths. Never treat them as project-relative disk paths.
            foreach (string root in UnityEditor.AssetDatabase.GetAllAssetPaths().Where(p => p.StartsWith("Packages/") && p.EndsWith("/Resources")
                && !p.Split('/').Contains("Editor") && UnityEditor.AssetDatabase.IsValidFolder(p)))
            {
                var package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(root);
                if (package == null) continue;
                string candidate = Path.GetFullPath(Path.Combine(package.resolvedPath, root.Substring(package.assetPath.Length + 1), resourcesSubPath));
                if (Directory.Exists(candidate)) candidates.Add(candidate);
            }
            if (candidates.Count > 1)
                throw new InvalidOperationException("Several localization folders share Resources/" + resourcesSubPath + ": " + string.Join("; ", candidates) + ". Consolidate this dataset into one folder before editing");
            string result = candidates.SingleOrDefault();
            if (result != null && !result.StartsWith(Path.GetFullPath(Application.dataPath) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Localization data is in a package and cannot be edited here: " + result);
            return result;
        }

        private static IEnumerable<string> ResourceDirectories(string directory)
        {
            foreach (string child in Directory.GetDirectories(directory).OrderBy(p => p, StringComparer.Ordinal))
            {
                if (Path.GetFileName(child) == "Editor" || Path.GetFileName(child).StartsWith(".")
                    || (File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0) continue;
                if (Path.GetFileName(child) == "Resources") yield return child;
                foreach (string nested in ResourceDirectories(child)) yield return nested;
            }
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
            _metaFileSnapshot = null;
            string path = GetMetaFilePath();
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                if (!File.Exists(path)) return;
                byte[] original = File.ReadAllBytes(path);
                string json = DecodeFile(original);
                // Parse and validate the entire document before publishing any entries.
                var parsed = EditorMetadataJson.Parse(json);
                foreach (var pair in parsed) _keyMeta.Add(pair.Key, pair.Value);
                _metaSnapshot = json;
                _metaFileSnapshot = original;
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
        public void SaveMeta() => SaveFiles(Array.Empty<(string, string)>(), true);

        private void EnsureMetaUnchanged(string path)
        {
            if (!EditorFileTransaction.Equal(EditorFileTransaction.Read(path), _metaFileSnapshot))
                throw new InvalidOperationException($"Metadata '{path}' changed on disk. Reload before saving to avoid overwriting external changes.");
        }
    }
}
