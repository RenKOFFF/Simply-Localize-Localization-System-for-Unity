using SimplyLocalize.Data;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SimplyLocalize.Editor.Data
{
    /// <summary>Editor-only folder discovery for the existing authoring model. Runtime loading uses exact source descriptors.</summary>
    public class EditorResourcesDataSource : IEditorLocalizationDataSource
    {
        private readonly string _basePath;

        public EditorResourcesDataSource(string basePath)
        {
            if (!LocalizationResourceAddress.IsValid(basePath, allowFolder: true))
                throw new ArgumentException($"Invalid Resources base path '{basePath}': use a relative path with '/'.", nameof(basePath));
            _basePath = basePath.TrimEnd('/');
        }

        private string LanguagePath(string languageCode)
        {
            if (!LocalizationResourceAddress.IsValid(languageCode) || languageCode.Contains("/"))
                throw new ArgumentException($"Invalid language code '{languageCode}' for Resources/{_basePath}.", nameof(languageCode));
            return string.IsNullOrEmpty(_basePath) ? languageCode : $"{_basePath}/{languageCode}";
        }

        public Dictionary<string, string> LoadTextData(string languageCode)
        {
            string textFolder = $"{LanguagePath(languageCode)}/text";
            var allTextAssets = Resources.LoadAll<TextAsset>(textFolder);

            if (allTextAssets == null || allTextAssets.Length == 0)
            {
                UnityEngine.Debug.LogWarning(
                    $"No text files found at Resources/{textFolder}/ for language '{languageCode}'");
                return new Dictionary<string, string>();
            }

            var merged = new Dictionary<string, string>();
            var sources = new Dictionary<string, string>();

            try
            {
                for (int i = 0; i < allTextAssets.Length; i++)
                {
                    var asset = allTextAssets[i];
                    Dictionary<string, string> parsed;
                    try { parsed = LocalizationFileParser.Parse(asset.text); }
                    catch (Exception error) when (error is FormatException || error is OverflowException || error is ArgumentOutOfRangeException)
                    {
                        throw new FormatException($"Invalid localization JSON at Resources/{textFolder}/{asset.name} (language '{languageCode}'): {error.Message}", error);
                    }

                    foreach (var kvp in parsed)
                    {
                        if (sources.TryGetValue(kvp.Key, out var previous))
                            UnityEngine.Debug.LogWarning($"Duplicate key '{kvp.Key}' for '{languageCode}' in Resources/{textFolder}: '{previous}' and '{asset.name}'. Last loaded value wins; remove the duplicate to avoid load-order dependence.");
                        merged[kvp.Key] = kvp.Value;
                        sources[kvp.Key] = asset.name;
                    }

                    LocalizationLogger.LogInfo(
                        $"Loaded {parsed.Count} keys from '{asset.name}.json' ({languageCode})");
                }
            }
            finally
            {
                // Release loaded text assets even if parsing one of the files fails.
                for (int i = 0; i < allTextAssets.Length; i++)
                    Resources.UnloadAsset(allTextAssets[i]);
            }

            LocalizationLogger.LogInfo(
                $"Total: {merged.Count} keys from {allTextAssets.Length} file(s) for '{languageCode}'");

            return merged;
        }

        public bool HasTextData(string languageCode)
        {
            string textFolder = $"{LanguagePath(languageCode)}/text";
            var assets = Resources.LoadAll<TextAsset>(textFolder);
            bool exists = assets != null && assets.Length > 0;

            if (assets != null)
            {
                for (int i = 0; i < assets.Length; i++)
                    Resources.UnloadAsset(assets[i]);
            }

            return exists;
        }

        public List<LocalizationAssetTable> LoadAssetTables(string languageCode)
        {
            string path = LanguagePath(languageCode);
            var tables = Resources.LoadAll<LocalizationAssetTable>(path);

            var result = new List<LocalizationAssetTable>();

            if (tables != null)
            {
                result.AddRange(tables);
                LocalizationLogger.LogInfo(
                    $"Loaded {tables.Length} asset table(s) for '{languageCode}'");
            }

            return result;
        }
    }
}
