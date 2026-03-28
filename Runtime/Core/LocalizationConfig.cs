using System.Collections.Generic;
using UnityEngine;

namespace SimplyLocalize
{
    /// <summary>
    /// Central configuration for the localization system.
    /// All settings are drag-and-drop references — no manual string entry.
    ///
    /// Create via: Create → SimplyLocalize → Localization Config.
    /// </summary>
    [CreateAssetMenu(
        fileName = "LocalizationConfig",
        menuName = "SimplyLocalize/Localization Config")]
    public class LocalizationConfig : ScriptableObject
    {
        [Header("Languages")]
        [Tooltip("All language profiles supported by the project. Drag LanguageProfile assets here.")]
        public List<LanguageProfile> languages = new();

        [Tooltip("Language used when no language has been set. Drag a profile from the list above.")]
        public LanguageProfile defaultLanguage;

        [Tooltip("Language used when a key is missing in the current language. Drag a profile from the list above.")]
        public LanguageProfile fallbackLanguage;

        [Header("Data")]
        [Tooltip("Base path inside Resources for localization data")]
        public string resourcesBasePath = "Localization";

        [Header("Editor")]
        [Tooltip("How spaces in keys are converted")]
        public KeyConversionMode keyConversionMode = KeyConversionMode.ReplaceSpacesWithSlashes;

        [Tooltip("Enable debug logging")]
        public bool enableLogging;

        // ──────────────────────────────────────────────
        //  Computed helpers
        // ──────────────────────────────────────────────

        /// <summary>Language code of the default language, or null.</summary>
        public string DefaultLanguageCode =>
            defaultLanguage != null ? defaultLanguage.Code : null;

        /// <summary>Language code of the fallback language, or null.</summary>
        public string FallbackLanguageCode =>
            fallbackLanguage != null ? fallbackLanguage.Code : null;

        // ──────────────────────────────────────────────
        //  Queries
        // ──────────────────────────────────────────────

        /// <summary>
        /// Returns the LanguageProfile for the given language code, or null if not found.
        /// </summary>
        public LanguageProfile GetProfile(string languageCode)
        {
            if (string.IsNullOrEmpty(languageCode))
                return null;

            for (int i = 0; i < languages.Count; i++)
            {
                if (languages[i] != null && languages[i].Code == languageCode)
                    return languages[i];
            }

            return null;
        }

        /// <summary>
        /// Returns the LanguageProfile matching the given SystemLanguage, or null.
        /// Useful for auto-detecting language on startup.
        /// </summary>
        public LanguageProfile GetProfileBySystemLanguage(SystemLanguage systemLanguage)
        {
            for (int i = 0; i < languages.Count; i++)
            {
                if (languages[i] != null && languages[i].systemLanguage == systemLanguage)
                    return languages[i];
            }

            return null;
        }

        /// <summary>
        /// Checks whether a language with the given code is defined.
        /// </summary>
        public bool HasLanguage(string languageCode)
        {
            return GetProfile(languageCode) != null;
        }

        /// <summary>
        /// Returns all defined language codes.
        /// </summary>
        public List<string> GetLanguageCodes()
        {
            var codes = new List<string>(languages.Count);

            for (int i = 0; i < languages.Count; i++)
            {
                if (languages[i] != null)
                    codes.Add(languages[i].Code);
            }

            return codes;
        }
    }
}