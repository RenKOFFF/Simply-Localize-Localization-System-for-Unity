using System.Collections.Generic;

namespace SimplyLocalize.Data
{
    /// <summary>
    /// Abstraction for loading localization data.
    /// Implement this interface to support different data sources (Resources, Addressables, etc.).
    ///
    /// Assets (sprites, audio, materials, custom types) are loaded via asset tables:
    /// call LoadAssetTables() to get the language's tables, then use LocalizationAssetTable.Get&lt;T&gt;(key).
    /// </summary>
    public interface ILocalizationDataProvider
    {
        /// <summary>
        /// Loads all text translations for the specified language.
        /// Returns a dictionary of key → translated string.
        /// </summary>
        Dictionary<string, string> LoadTextData(string languageCode);

        /// <summary>
        /// Returns true if text data exists for the specified language.
        /// Used by diagnostics and the editor without triggering a full load.
        /// </summary>
        bool HasTextData(string languageCode);

        /// <summary>
        /// Loads all asset tables for the specified language.
        /// Each table is a LocalizationAssetTable SO in the language folder.
        /// Returns empty list if none found.
        /// </summary>
        List<LocalizationAssetTable> LoadAssetTables(string languageCode);
    }
}