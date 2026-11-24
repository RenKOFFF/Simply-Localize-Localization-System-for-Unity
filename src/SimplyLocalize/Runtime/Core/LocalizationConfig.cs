using System;
using System.Collections.Generic;
using UnityEngine;

namespace SimplyLocalize
{
    /// <summary>
    /// Legacy serialized data retained until the separate project-data cleanup stage.
    /// New projects use LocalizationProjectConfig; the channel runtime does not read this type.
    /// </summary>
    public class LocalizationConfig : ScriptableObject
    {
        /// <summary>Reserved Resources address used by automatic and parameterless initialization.</summary>
        public const string StartupResourcePath = "SimplyLocalizeSettings";

        [Header("Initialization")]
        [Tooltip("Automatically initialize on game start from Resources/SimplyLocalizeSettings.asset. Use Simply Localize > Settings to move an existing config. Explicit config references work at any path.")]
        public bool autoInitialize;

        [Tooltip("Auto-detect language from device SystemLanguage on first launch")]
        public bool autoDetectLanguage;

        [Header("Languages")]
        [Tooltip("All language profiles supported by the project.")]
        public List<LanguageProfile> languages = new();

        [Tooltip("Language used when no language has been set or auto-detect fails.")]
        public LanguageProfile defaultLanguage;

        [Tooltip("Language used when a key is missing in the current language.")]
        public LanguageProfile fallbackLanguage;

        [Header("Data")]
        [Tooltip("Base path inside Resources for localization data")]
        public string resourcesBasePath = "Localization";

        [Tooltip("Load text translations when changing language. Disable for contexts containing only localized audio, images or other assets. Existing configurations keep text loading enabled.")]
        public bool loadTextData = true;

        [Header("Editor")]
        public KeyConversionMode keyConversionMode = KeyConversionMode.ReplaceSpacesWithSlashes;
        public bool enableLogging;
        public bool enableLoggingInBuild;

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
            if (string.IsNullOrEmpty(languageCode) || languages == null)
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
            if (languages == null || systemLanguage == SystemLanguage.Unknown) return null;
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
            var codes = new List<string>(languages?.Count ?? 0);
            if (languages == null) return codes;

            for (int i = 0; i < languages.Count; i++)
            {
                if (languages[i] != null)
                    codes.Add(languages[i].Code);
            }

            return codes;
        }

        /// <summary>Reports invalid settings without changing this asset. Empty language lists are allowed for setup.</summary>
        public List<string> GetValidationErrors()
        {
            var errors = new List<string>();
            string source = $"LocalizationConfig '{name}'";
            if (!IsResourcePathValid(resourcesBasePath))
                errors.Add($"{source}: resourcesBasePath '{resourcesBasePath}' must be a relative Resources path using '/'; traversal and absolute paths are not supported.");
            var codes = new Dictionary<string, LanguageProfile>(StringComparer.OrdinalIgnoreCase);
            if (languages != null)
            {
                for (int i = 0; i < languages.Count; i++)
                {
                    var profile = languages[i];
                    if (profile == null) continue; // Existing lists can contain unused slots.
                    if (!IsLanguageCodeValid(profile.Code))
                        errors.Add($"{source}: languages[{i}] ('{profile.name}') has an invalid language code '{profile.Code}'.");
                    else if (codes.TryGetValue(profile.Code, out var other) && other != profile)
                        errors.Add($"{source}: duplicate code '{profile.Code}' in profiles '{other.name}' and '{profile.name}'.");
                    else codes[profile.Code] = profile;
                }
            }
            if (defaultLanguage != null && GetProfile(defaultLanguage.Code) != defaultLanguage)
                errors.Add($"{source}: defaultLanguage '{defaultLanguage.name}' must be included in languages with a unique code.");

            // External fallback profiles remain supported. Validate their paths and detect
            // conflicting identities without rejecting the existing cycle-safe fallback contract.
            var visited = new HashSet<LanguageProfile>();
            var pending = new Queue<LanguageProfile>();
            if (languages != null) foreach (var profile in languages) if (profile != null) pending.Enqueue(profile);
            if (fallbackLanguage != null) pending.Enqueue(fallbackLanguage);
            while (pending.Count > 0)
            {
                var profile = pending.Dequeue();
                if (!visited.Add(profile)) continue;
                if (!IsLanguageCodeValid(profile.Code))
                    errors.Add($"{source}: profile/fallback '{profile.name}' has an invalid code '{profile.Code}'.");
                else if (codes.TryGetValue(profile.Code, out var other) && other != profile)
                    errors.Add($"{source}: conflicting profile/fallback references for code '{profile.Code}'.");
                else codes[profile.Code] = profile;
                if (profile.fallbackProfile != null) pending.Enqueue(profile.fallbackProfile);
            }
            return errors;
        }

        internal void ValidateOrThrow()
        {
            var errors = GetValidationErrors();
            if (errors.Count > 0) throw new ArgumentException(string.Join("\n", errors), "config");
            if (languages == null || languages.Count == 0)
                LocalizationLogger.LogWarning($"LocalizationConfig '{name}' has no languages; select a configured language before requesting translations.");
            else if (languages.Contains(null))
                LocalizationLogger.LogWarning($"LocalizationConfig '{name}' contains empty language slots; they will be ignored.");
        }

        internal static bool IsLanguageCodeValid(string code) =>
            !string.IsNullOrWhiteSpace(code) && code == code.Trim() && !code.Contains("/") && IsResourcePathValid(code);

        internal static bool IsResourcePathValid(string path)
            => LocalizationResourceAddress.IsValid(path, allowFolder: true);
    }
}
