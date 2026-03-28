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

        /// <summary>The base path to the Localization folder inside Assets/Resources/</summary>
        private string _basePath;

        /// <summary>Reference language code used to discover files and keys</summary>
        private string _referenceLanguage;

        // ──────────────────────────────────────────────
        //  Public accessors
        // ──────────────────────────────────────────────

        public IReadOnlyList<string> SourceFiles => _sourceFiles;
        public IReadOnlyDictionary<string, string> KeyToFile => _keyToFile;
        public IReadOnlyDictionary<string, Dictionary<string, string>> Translations => _translations;
        public string BasePath => _basePath;

        /// <summary>All unique keys across all languages.</summary>
        public IEnumerable<string> AllKeys => _keyToFile.Keys;

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
            _basePath = null;
            _referenceLanguage = null;
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
        }

        /// <summary>
        /// Adds a new key assigned to the specified source file.
        /// </summary>
        public void AddKey(string key, string sourceFile)
        {
            if (_keyToFile.ContainsKey(key))
                return;

            _keyToFile[key] = sourceFile;

            // Add empty entries for all languages
            foreach (var langData in _translations.Values)
            {
                if (!langData.ContainsKey(key))
                    langData[key] = "";
            }
        }

        /// <summary>
        /// Removes a key from all languages.
        /// </summary>
        public void RemoveKey(string key)
        {
            _keyToFile.Remove(key);

            foreach (var langData in _translations.Values)
                langData.Remove(key);
        }

        /// <summary>
        /// Renames a key across all languages.
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
    }
}
