using System.Collections.Generic;

namespace SimplyLocalize.Editor.Data
{
    /// <summary>Editor data discovery seam for Coverage. Runtime extensions implement ILocalizationSourceProvider.</summary>
    public interface IEditorLocalizationDataSource
    {
        /// <summary>
        /// Loads all text translations for the specified language.
        /// Returns a non-null dictionary of key → translated string (empty if no data exists).
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
        /// Returns a non-null list, empty if none found.
        /// </summary>
        List<LocalizationAssetTable> LoadAssetTables(string languageCode);
    }
}
