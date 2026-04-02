using System.Collections.Generic;
using UnityEngine;

namespace SimplyLocalize.Data
{
    /// <summary>
    /// Loads localization data from the Resources folder.
    ///
    /// Expected folder structure inside Resources:
    ///
    ///   {basePath}/
    ///   ├── en/
    ///   │   ├── text/              — all .json files are loaded and merged
    ///   │   │   ├── en.json
    ///   │   │   ├── items.json
    ///   │   │   └── menu.json
    ///   │   ├── images/            — sprites, any subfolder nesting allowed
    ///   │   │   ├── ui/logo
    ///   │   │   ├── items/sword
    ///   │   │   └── banner
    ///   │   └── audio/             — audio clips, any subfolder nesting allowed
    ///   │       ├── greeting
    ///   │       └── narration/intro
    ///   ├── ru/
    ///   │   ├── text/
    ///   │   │   ├── ru.json
    ///   │   │   └── items_ru.json
    ///   │   └── images/
    ///   │       └── ui/logo
    ///   ...
    ///
    /// Text:   All .json files inside {basePath}/{lang}/text/ are loaded and merged.
    ///         Duplicate keys across files: last loaded wins (no guaranteed order).
    ///
    /// Images: Key is the relative path inside the images folder.
    ///         Key "ui/logo" loads from {basePath}/{lang}/images/ui/logo.
    ///         Any depth of subfolder nesting is supported.
    ///
    /// Audio:  Same convention as images.
    ///         Key "narration/intro" loads from {basePath}/{lang}/audio/narration/intro.
    /// </summary>
    public class ResourcesDataProvider : ILocalizationDataProvider
    {
        private readonly string _basePath;

        public ResourcesDataProvider(string basePath)
        {
            _basePath = basePath.TrimEnd('/');
        }

        public Dictionary<string, string> LoadTextData(string languageCode)
        {
            string textFolder = $"{_basePath}/{languageCode}/text";
            var allTextAssets = Resources.LoadAll<TextAsset>(textFolder);

            if (allTextAssets == null || allTextAssets.Length == 0)
            {
                LocalizationLogger.LogWarning(
                    $"No text files found at Resources/{textFolder}/ for language '{languageCode}'");
                return new Dictionary<string, string>();
            }

            var merged = new Dictionary<string, string>();

            for (int i = 0; i < allTextAssets.Length; i++)
            {
                var asset = allTextAssets[i];
                var parsed = LocalizationFileParser.Parse(asset.text);

                foreach (var kvp in parsed)
                    merged[kvp.Key] = kvp.Value;

                LocalizationLogger.Log(
                    $"Loaded {parsed.Count} keys from '{asset.name}.json' ({languageCode})");
            }

            // Unload all text assets
            for (int i = 0; i < allTextAssets.Length; i++)
                Resources.UnloadAsset(allTextAssets[i]);

            LocalizationLogger.Log(
                $"Total: {merged.Count} keys from {allTextAssets.Length} file(s) for '{languageCode}'");

            return merged;
        }

        public Sprite LoadSprite(string key, string languageCode)
        {
            string path = $"{_basePath}/{languageCode}/images/{SanitizeKey(key)}";
            return Resources.Load<Sprite>(path);
        }

        public AudioClip LoadAudioClip(string key, string languageCode)
        {
            string path = $"{_basePath}/{languageCode}/audio/{SanitizeKey(key)}";
            return Resources.Load<AudioClip>(path);
        }

        public bool HasTextData(string languageCode)
        {
            string textFolder = $"{_basePath}/{languageCode}/text";
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
            string path = $"{_basePath}/{languageCode}";
            var tables = Resources.LoadAll<LocalizationAssetTable>(path);

            var result = new List<LocalizationAssetTable>();

            if (tables != null)
            {
                result.AddRange(tables);
                LocalizationLogger.Log(
                    $"Loaded {tables.Length} asset table(s) for '{languageCode}'");
            }

            return result;
        }

        private static string SanitizeKey(string key)
        {
            return key.Replace('\\', '/');
        }
    }
}