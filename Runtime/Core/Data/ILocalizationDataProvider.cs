using System.Collections.Generic;
using UnityEngine;

namespace SimplyLocalize.Data
{
    /// <summary>
    /// Abstraction for loading localization data.
    /// Implement this interface to support different data sources (Resources, Addressables, etc.).
    /// </summary>
    public interface ILocalizationDataProvider
    {
        /// <summary>
        /// Loads all text translations for the specified language.
        /// Returns a dictionary of key → translated string.
        /// </summary>
        Dictionary<string, string> LoadTextData(string languageCode);

        /// <summary>
        /// Loads a localized sprite by key and language.
        /// Returns null if the sprite doesn't exist for this language.
        /// </summary>
        Sprite LoadSprite(string key, string languageCode);

        /// <summary>
        /// Loads a localized audio clip by key and language.
        /// Returns null if the clip doesn't exist for this language.
        /// </summary>
        AudioClip LoadAudioClip(string key, string languageCode);
        bool HasTextData(string languageCode);

        /// <summary>
        /// Loads all asset tables for the specified language.
        /// Each table is a LocalizationAssetTable SO in the language folder.
        /// Returns empty list if none found.
        /// </summary>
        List<LocalizationAssetTable> LoadAssetTables(string languageCode);
    }
}