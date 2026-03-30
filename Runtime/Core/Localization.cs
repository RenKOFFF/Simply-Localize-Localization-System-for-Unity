using System;
using System.Collections.Generic;
using SimplyLocalize.Data;
using UnityEngine;

namespace SimplyLocalize
{
    /// <summary>
    /// Public static API for the localization system.
    /// This is the main entry point for getting translations, switching languages,
    /// and subscribing to language change events.
    ///
    /// Initialize once at startup with a LocalizationConfig asset, then use anywhere.
    ///
    /// Usage:
    ///   Localization.Initialize(config);
    ///   Localization.SetLanguage("ru");
    ///   string text = Localization.Get("UI/MainMenu/Play");
    ///   string plural = Localization.Get("Game/Loot/Coins", 5);
    /// </summary>
    public static class Localization
    {
        private static LocalizationManager _manager;

        // ──────────────────────────────────────────────
        //  Initialization
        // ──────────────────────────────────────────────

        /// <summary>
        /// Initializes the localization system with the given config.
        /// Automatically sets the default language.
        /// </summary>
        public static void Initialize(LocalizationConfig config)
        {
            if (config == null)
            {
                Debug.LogError("[SimplyLocalize] Cannot initialize: config is null");
                return;
            }

            Shutdown();

            _manager = new LocalizationManager(config);
            _manager.OnLanguageChanged += lang => OnLanguageChanged?.Invoke(lang);
            _manager.OnProfileChanged += profile => OnProfileChanged?.Invoke(profile);

            if (config.defaultLanguage != null)
                _manager.SetLanguage(config.defaultLanguage);

            LocalizationLogger.Log("Localization initialized");
        }

        /// <summary>
        /// Initializes with config and immediately sets a specific language by code.
        /// If the language code is not found, falls back to default or throws error
        /// based on throwOnMissing parameter.
        /// </summary>
        /// <param name="config">The localization config asset.</param>
        /// <param name="languageCode">Language code to set (e.g. "ru", "en").</param>
        /// <param name="throwOnMissing">If true, throws exception when language not found.
        /// If false, falls back to default language.</param>
        public static void Initialize(LocalizationConfig config, string languageCode,
            bool throwOnMissing = false)
        {
            if (config == null)
            {
                Debug.LogError("[SimplyLocalize] Cannot initialize: config is null");
                return;
            }

            Shutdown();

            _manager = new LocalizationManager(config);
            _manager.OnLanguageChanged += lang => OnLanguageChanged?.Invoke(lang);
            _manager.OnProfileChanged += profile => OnProfileChanged?.Invoke(profile);

            var profile = config.GetProfile(languageCode);

            if (profile != null)
            {
                _manager.SetLanguage(profile);
            }
            else if (throwOnMissing)
            {
                throw new ArgumentException(
                    $"[SimplyLocalize] Language '{languageCode}' not found in config. " +
                    $"Available: {string.Join(", ", config.languages.ConvertAll(p => p?.Code ?? "null"))}");
            }
            else
            {
                LocalizationLogger.LogWarning(
                    $"Language '{languageCode}' not found, using default");

                if (config.defaultLanguage != null)
                    _manager.SetLanguage(config.defaultLanguage);
            }

            LocalizationLogger.Log($"Localization initialized with '{_manager.CurrentLanguage}'");
        }

        /// <summary>
        /// Initializes by finding config in Resources automatically.
        /// Sets the default language from config.
        /// Convenience method — no need to pass config reference.
        /// </summary>
        public static void Initialize()
        {
            if (IsInitialized) return;

            var configs = Resources.LoadAll<LocalizationConfig>("");

            if (configs == null || configs.Length == 0)
            {
                Debug.LogError("[SimplyLocalize] No LocalizationConfig found in Resources. " +
                    "Create one and place it in a Resources folder.");
                return;
            }

            Initialize(configs[0]);
        }

        /// <summary>
        /// Shuts down the localization system and releases all cached data.
        /// </summary>
        public static void Shutdown()
        {
            if (_manager == null)
                return;

            _manager.ClearCaches();
            _manager = null;
        }

        /// <summary>
        /// Whether the system has been initialized.
        /// </summary>
        public static bool IsInitialized => _manager != null;

        // ──────────────────────────────────────────────
        //  Language
        // ──────────────────────────────────────────────

        /// <summary>
        /// The currently active language code (e.g. "en", "ru").
        /// </summary>
        public static string CurrentLanguage => _manager?.CurrentLanguage;

        /// <summary>
        /// The display profile for the current language.
        /// Contains font, size, spacing, and direction settings.
        /// </summary>
        public static LanguageProfile CurrentProfile => _manager?.CurrentProfile;

        /// <summary>
        /// The active configuration asset.
        /// </summary>
        public static LocalizationConfig Config => _manager?.Config;

        /// <summary>
        /// Switches to the specified language by code.
        /// Loads translation data if not already cached, then fires OnLanguageChanged.
        /// </summary>
        public static void SetLanguage(string languageCode)
        {
            EnsureInitialized();
            _manager.SetLanguage(languageCode);
        }

        /// <summary>
        /// Switches to the specified language by profile reference.
        /// Preferred over the string overload — no typos, full IntelliSense.
        /// </summary>
        public static void SetLanguage(LanguageProfile profile)
        {
            EnsureInitialized();
            _manager.SetLanguage(profile);
        }

        /// <summary>
        /// Attempts to set the language matching the device's system language.
        /// Falls back to the config's default language if no match is found.
        /// Returns the profile that was set, or null if nothing matched.
        /// </summary>
        public static LanguageProfile SetLanguageAuto()
        {
            EnsureInitialized();

            var systemLang = Application.systemLanguage;
            var profile = _manager.Config.GetProfileBySystemLanguage(systemLang);

            if (profile != null)
            {
                _manager.SetLanguage(profile);
                return profile;
            }

            // Fall back to default
            if (_manager.Config.defaultLanguage != null)
            {
                _manager.SetLanguage(_manager.Config.defaultLanguage);
                return _manager.Config.defaultLanguage;
            }

            LocalizationLogger.LogWarning(
                $"No language profile matches SystemLanguage.{systemLang}, and no default is set");
            return null;
        }

        /// <summary>
        /// Returns all language profiles defined in the config.
        /// </summary>
        public static IReadOnlyList<LanguageProfile> AvailableLanguages =>
            _manager?.Config?.languages;

        // ──────────────────────────────────────────────
        //  Text
        // ──────────────────────────────────────────────

        /// <summary>
        /// Gets the translated string for the given key.
        /// Fallback chain: current language → fallback language → key.
        /// </summary>
        public static string Get(string key)
        {
            if (_manager == null) return key ?? string.Empty;
            return _manager.Get(key);
        }

        /// <summary>
        /// Gets the translated string with indexed parameter substitution and pluralization.
        ///
        /// Example:
        ///   JSON: "You found {0} {0|coin|coins}"
        ///   Code: Localization.Get("loot_coins", 5) → "You found 5 coins"
        /// </summary>
        public static string Get(string key, params object[] args)
        {
            if (_manager == null) return key ?? string.Empty;
            return _manager.Get(key, args);
        }

        /// <summary>
        /// Gets the translated string with named parameter substitution and pluralization.
        ///
        /// Example:
        ///   JSON: "{playerName} found {count} {count|coin|coins}"
        ///   Code: Localization.Get("loot", new Dictionary&lt;string, object&gt; {
        ///       { "playerName", "Alex" }, { "count", 5 }
        ///   });
        ///   Result: "Alex found 5 coins"
        /// </summary>
        public static string Get(string key, Dictionary<string, object> namedArgs)
        {
            if (_manager == null) return key ?? string.Empty;
            return _manager.Get(key, namedArgs);
        }

        /// <summary>
        /// Gets the translated string with both indexed and named parameters.
        ///
        /// Example:
        ///   JSON: "{playerName} has {0} {0|life|lives} left"
        ///   Code: Localization.Get("lives", new object[] { 3 },
        ///       new Dictionary&lt;string, object&gt; { { "playerName", "Alex" } });
        ///   Result: "Alex has 3 lives left"
        /// </summary>
        public static string Get(string key, object[] args, Dictionary<string, object> namedArgs)
        {
            if (_manager == null) return key ?? string.Empty;
            return _manager.Get(key, args, namedArgs);
        }

        // ──────────────────────────────────────────────
        //  Assets
        // ──────────────────────────────────────────────

        /// <summary>
        /// Loads a localized sprite for the current language.
        /// Falls back to the fallback language if not found.
        /// </summary>
        public static Sprite GetSprite(string key)
        {
            return _manager?.GetSprite(key);
        }

        /// <summary>
        /// Loads a localized audio clip for the current language.
        /// Falls back to the fallback language if not found.
        /// </summary>
        public static AudioClip GetAudio(string key)
        {
            return _manager?.GetAudio(key);
        }

        // ──────────────────────────────────────────────
        //  Queries
        // ──────────────────────────────────────────────

        /// <summary>
        /// Checks whether a translation exists for the key in the current or fallback language.
        /// </summary>
        public static bool HasKey(string key)
        {
            return _manager != null && _manager.HasKey(key);
        }

        /// <summary>
        /// Checks whether a specific language has a translation for the given key.
        /// Loads the language data if not already cached.
        /// </summary>
        public static bool HasTranslation(string key, string languageCode)
        {
            return _manager != null && _manager.HasTranslation(key, languageCode);
        }

        /// <summary>
        /// Returns all translation keys loaded for the current language.
        /// </summary>
        public static IEnumerable<string> GetAllKeys()
        {
            return _manager?.GetAllKeys() ?? Array.Empty<string>();
        }

        // ──────────────────────────────────────────────
        //  Data Provider
        // ──────────────────────────────────────────────

        /// <summary>
        /// Replaces the data provider at runtime (e.g. to switch to Addressables).
        /// Clears all cached translation data.
        /// </summary>
        public static void SetDataProvider(ILocalizationDataProvider provider)
        {
            EnsureInitialized();
            _manager.SetDataProvider(provider);
        }

        /// <summary>
        /// Clears all cached data and reloads the current language.
        /// </summary>
        public static void Reload()
        {
            if (_manager == null)
                return;

            string lang = _manager.CurrentLanguage;
            _manager.ClearCaches();

            if (!string.IsNullOrEmpty(lang))
                _manager.SetLanguage(lang);
        }

        // ──────────────────────────────────────────────
        //  Events
        // ──────────────────────────────────────────────

        /// <summary>
        /// Fired when the active language changes. Passes the new language code.
        /// All localized components subscribe to this automatically.
        /// </summary>
        public static event Action<string> OnLanguageChanged;

        /// <summary>
        /// Fired when the language profile changes (font, sizing, direction, etc.).
        /// Components use this to apply visual settings.
        /// </summary>
        public static event Action<LanguageProfile> OnProfileChanged;

        // ──────────────────────────────────────────────
        //  Internal
        // ──────────────────────────────────────────────

        private static void EnsureInitialized()
        {
            if (_manager == null)
                throw new InvalidOperationException(
                    "[SimplyLocalize] Not initialized. Call Localization.Initialize(config) first.");
        }
    }
}