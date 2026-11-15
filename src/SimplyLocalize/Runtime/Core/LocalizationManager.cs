using System;
using System.Collections.Generic;
using SimplyLocalize.Data;
using SimplyLocalize.TextProcessing;
using UnityEngine;

namespace SimplyLocalize
{
    /// <summary>
    /// Core localization runtime engine.
    /// Manages loaded translation data, template caching, language switching,
    /// and asset loading through the configured data provider.
    ///
    /// Not a MonoBehaviour — lifetime is managed by the static Localization class
    /// or by your DI container of choice.
    ///
    /// For DI usage: register an instance bound to your LocalizationConfig and inject
    /// LocalizationManager directly into consumers instead of using the static facade.
    /// </summary>
    public class LocalizationManager
    {
        private readonly LocalizationConfig _config;
        private ILocalizationDataProvider _dataProvider;

        private string _currentLanguage;
        private LanguageProfile _currentProfile;

        // Translation data: languageCode → (key → value)
        private Dictionary<string, Dictionary<string, string>> _textCache = new();

        // Parsed template cache: raw string → parsed template (shared across languages)
        private readonly Dictionary<string, ParsedTemplate> _templateCache = new();

        // Asset tables: languageCode → list of loaded tables
        private Dictionary<string, List<LocalizationAssetTable>> _assetTableCache = new();
        private bool _usesAssetTables;

        public event Action<string> OnLanguageChanged;
        public event Action<LanguageProfile> OnProfileChanged;

        public string CurrentLanguage => _currentLanguage;
        public LanguageProfile CurrentProfile => _currentProfile;
        public LocalizationConfig Config => _config;

        public LocalizationManager(LocalizationConfig config)
        {
            _config = config != null
                ? config
                : throw new ArgumentNullException(nameof(config));

            config.ValidateOrThrow();

            LocalizationLogger.Enabled = config.enableLogging;
            LocalizationLogger.EnabledInBuild = config.enableLoggingInBuild;
            _dataProvider = new ResourcesDataProvider(config.resourcesBasePath);
        }

        /// <summary>
        /// Replaces the data provider (e.g. to switch from Resources to Addressables).
        /// Reloads the active language and its fallbacks before notifying consumers.
        /// If loading fails, the previous provider and caches remain active.
        /// Providers and their Unity assets remain owned by the caller.
        /// </summary>
        public void SetDataProvider(ILocalizationDataProvider provider)
        {
            if (provider == null) throw new ArgumentNullException(nameof(provider));
            ReloadFrom(provider);
            LocalizationLogger.Log("Data provider changed");
        }

        /// <summary>
        /// Reloads the current language and fallback data, even when the language is unchanged.
        /// Publishes both change events once after successful loading. On failure, keeps
        /// the last successful state and propagates the exception without publishing events.
        /// Asset tables are preloaded only after this manager has been used to request assets.
        /// </summary>
        public void Reload()
        {
            ReloadFrom(_dataProvider);
        }

        private void ReloadFrom(ILocalizationDataProvider provider)
        {
            var text = new Dictionary<string, Dictionary<string, string>>();
            var assets = new Dictionary<string, List<LocalizationAssetTable>>();
            if (!string.IsNullOrEmpty(_currentLanguage))
                PrepareData(provider, _currentLanguage, _currentProfile, text, assets);

            _dataProvider = provider;
            _textCache = text;
            _assetTableCache = assets;
            _templateCache.Clear();
            NotifyCurrentState();
        }

        /// <summary>
        /// Sets the active language by profile. Loads data if not already cached.
        /// Fires OnLanguageChanged and OnProfileChanged events.
        /// </summary>
        public void SetLanguage(LanguageProfile profile)
        {
            if (profile == null)
            {
                LocalizationLogger.LogWarning("Cannot set language: profile is null");
                return;
            }

            SetLanguage(profile.Code);
        }

        /// <summary>
        /// Sets the active language by code. Loads data if not already cached.
        /// Fires OnLanguageChanged and OnProfileChanged events.
        /// </summary>
        public void SetLanguage(string languageCode)
        {
            if (string.IsNullOrEmpty(languageCode))
            {
                LocalizationLogger.LogWarning("Cannot set language: code is null or empty");
                return;
            }

            if (_currentLanguage == languageCode)
                return;

            var profile = _config.GetProfile(languageCode);

            if (profile == null)
            {
                LocalizationLogger.LogWarning(
                    $"Language '{languageCode}' is not defined in LocalizationConfig '{_config.name}'. Available: {string.Join(", ", _config.GetLanguageCodes())}");
                return;
            }

            // Stage missing data separately: a failed fallback must not leave a partial load.
            var text = new Dictionary<string, Dictionary<string, string>>(_textCache);
            var assets = new Dictionary<string, List<LocalizationAssetTable>>(_assetTableCache);
            PrepareData(_dataProvider, languageCode, profile, text, assets);

            _textCache = text;
            _assetTableCache = assets;
            _currentLanguage = languageCode;
            _currentProfile = profile;

            LocalizationLogger.Log($"Language set to '{languageCode}' ({profile.displayName})");

            NotifyCurrentState();
        }

        /// <summary>
        /// Gets a translated string by key, with no parameter substitution.
        /// Falls back to fallback language, then to the raw key.
        /// </summary>
        public string Get(string key)
        {
            if (string.IsNullOrEmpty(key))
                return string.Empty;

            string raw = GetRaw(key, out var sourceLanguage);
            var template = GetOrParseTemplate(raw);

            if (template == null)
                return raw;

            // Text tokens may already contain decoded {{ / }} escapes.
            return TextFormatter.Format(template, sourceLanguage, (object[])null);
        }

        /// <summary>
        /// Gets a translated string by key with indexed parameter substitution and pluralization.
        /// </summary>
        public string Get(string key, object[] args)
        {
            if (string.IsNullOrEmpty(key))
                return string.Empty;

            string raw = GetRaw(key, out var sourceLanguage);
            var template = GetOrParseTemplate(raw);

            if (template == null)
                return raw;

            return TextFormatter.Format(template, sourceLanguage, args);
        }

        /// <summary>
        /// Gets a translated string with named parameter substitution and pluralization.
        /// </summary>
        public string Get(string key, Dictionary<string, object> namedArgs)
        {
            if (string.IsNullOrEmpty(key))
                return string.Empty;

            string raw = GetRaw(key, out var sourceLanguage);
            var template = GetOrParseTemplate(raw);

            if (template == null)
                return raw;

            return TextFormatter.Format(template, sourceLanguage, namedArgs);
        }

        /// <summary>
        /// Gets a translated string with both indexed and named parameters.
        /// </summary>
        public string Get(string key, object[] args, Dictionary<string, object> namedArgs)
        {
            if (string.IsNullOrEmpty(key))
                return string.Empty;

            string raw = GetRaw(key, out var sourceLanguage);
            var template = GetOrParseTemplate(raw);

            if (template == null)
                return raw;

            return TextFormatter.Format(template, sourceLanguage, args, namedArgs);
        }

        /// <summary>
        /// Gets a localized asset of type T from asset tables.
        /// Follows the full fallback chain: current → per-language → global.
        /// </summary>
        public T GetAsset<T>(string key) where T : UnityEngine.Object
        {
            if (string.IsNullOrEmpty(key)) return null;

            _usesAssetTables = true;
            var current = GetAssetFromTables<T>(key, _currentLanguage);
            if (current != null) return current;
            foreach (var code in GetLanguageChain(_currentLanguage, _currentProfile))
            {
                if (code == _currentLanguage) continue;
                var result = GetAssetFromTables<T>(key, code);
                if (result != null) return result;
            }
            return null;
        }

        private T GetAssetFromTables<T>(string key, string languageCode) where T : UnityEngine.Object
        {
            if (string.IsNullOrEmpty(languageCode)) return null;

            EnsureAssetTablesLoaded(languageCode);

            if (!_assetTableCache.TryGetValue(languageCode, out var tables))
                return null;

            for (int i = 0; i < tables.Count; i++)
            {
                if (tables[i] == null) continue;
                var asset = tables[i].Get<T>(key);
                if (asset != null) return asset;
            }

            return null;
        }

        /// <summary>
        /// Checks whether a key exists in the current language or fallback, including empty entries.
        /// Get skips null/empty translations, but this query checks entry presence for compatibility.
        /// </summary>
        public bool HasKey(string key)
        {
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(_currentLanguage))
                return false;

            if (TryGetText(key, _currentLanguage, out _)) return true;
            foreach (var code in GetLanguageChain(_currentLanguage, _currentProfile))
                if (code != _currentLanguage && TryGetText(key, code, out _)) return true;
            return false;
        }

        /// <summary>
        /// Checks whether a specific language has an entry for the key, including null/empty values.
        /// </summary>
        public bool HasTranslation(string key, string languageCode)
        {
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(languageCode))
                return false;

            EnsureTextDataLoaded(languageCode);

            return _textCache.TryGetValue(languageCode, out var data) && data.ContainsKey(key);
        }

        /// <summary>
        /// Returns all keys loaded for the current language.
        /// </summary>
        public IEnumerable<string> GetAllKeys()
        {
            if (!string.IsNullOrEmpty(_currentLanguage)
                && _textCache.TryGetValue(_currentLanguage, out var data))
            {
                return data.Keys;
            }

            return Array.Empty<string>();
        }

        /// <summary>
        /// Clears all caches without notifying consumers or changing the selected language.
        /// Subsequent lookups load data on demand. Use Reload to refresh active consumers atomically.
        /// </summary>
        public void ClearCaches()
        {
            _textCache.Clear();
            _templateCache.Clear();
            _assetTableCache.Clear();
            LocalizationLogger.Log("All caches cleared");
        }

        /// <summary>
        /// Returns the raw translation string before any formatting.
        /// Fallback chain: current → per-language fallback → ... → global fallback → key.
        /// Example: uk → ru (per-language) → en (global) → key as-is.
        /// </summary>
        private string GetRaw(string key, out string sourceLanguage)
        {
            // Keep the common current-language lookup free of fallback iterator allocations.
            sourceLanguage = _currentLanguage;
            if (TryGetText(key, _currentLanguage, out var current) && !string.IsNullOrEmpty(current))
                return current;

            foreach (var code in GetLanguageChain(_currentLanguage, _currentProfile))
            {
                if (code == _currentLanguage) continue;
                if (TryGetText(key, code, out var value) && !string.IsNullOrEmpty(value))
                {
                    sourceLanguage = code;
                    LocalizationLogger.Log($"Key '{key}' missing for '{_currentLanguage}', using fallback '{code}'");
                    return value;
                }
            }

            LocalizationLogger.LogWarning(
                $"Translation not found for key '{key}' in '{_currentLanguage}' or any fallback");
            return key;
        }

        private bool TryGetText(string key, string languageCode, out string value)
        {
            value = null;

            if (string.IsNullOrEmpty(languageCode))
                return false;

            EnsureTextDataLoaded(languageCode);
            return _textCache.TryGetValue(languageCode, out var data)
                && data.TryGetValue(key, out value);
        }

        private IEnumerable<string> GetLanguageChain(string languageCode, LanguageProfile profile)
        {
            if (!string.IsNullOrEmpty(languageCode)) yield return languageCode;

            var visited = new HashSet<string> { languageCode };
            var fallback = profile != null ? profile.fallbackProfile : null;
            while (fallback != null && visited.Add(fallback.Code))
            {
                if (!string.IsNullOrEmpty(fallback.Code)) yield return fallback.Code;
                fallback = fallback.fallbackProfile;
            }

            string globalFallback = _config.FallbackLanguageCode;
            if (!string.IsNullOrEmpty(globalFallback) && visited.Add(globalFallback))
                yield return globalFallback;
        }

        private void PrepareData(ILocalizationDataProvider provider, string languageCode,
            LanguageProfile profile, Dictionary<string, Dictionary<string, string>> text,
            Dictionary<string, List<LocalizationAssetTable>> assets)
        {
            foreach (var code in GetLanguageChain(languageCode, profile))
            {
                if (!text.ContainsKey(code)) text[code] = LoadTextData(provider, code);
                // Once assets have consumers, prepare them before publishing any change event.
                if (_usesAssetTables && !assets.ContainsKey(code))
                    assets[code] = LoadAssetTables(provider, code);
            }
        }

        private void NotifyCurrentState()
        {
            if (string.IsNullOrEmpty(_currentLanguage)) return;
            OnLanguageChanged?.Invoke(_currentLanguage);
            if (_currentProfile != null) OnProfileChanged?.Invoke(_currentProfile);
        }

        private ParsedTemplate GetOrParseTemplate(string rawText)
        {
            if (string.IsNullOrEmpty(rawText))
                return null;

            if (_templateCache.TryGetValue(rawText, out var cached))
                return cached;

            var template = TokenParser.Parse(rawText);
            _templateCache[rawText] = template;
            return template;
        }

        private void EnsureTextDataLoaded(string languageCode)
        {
            if (_textCache.ContainsKey(languageCode))
                return;

            _textCache[languageCode] = LoadTextData(_dataProvider, languageCode);
        }

        private void EnsureAssetTablesLoaded(string languageCode)
        {
            if (_assetTableCache.ContainsKey(languageCode))
                return;

            _assetTableCache[languageCode] = LoadAssetTables(_dataProvider, languageCode);
        }

        private static Dictionary<string, string> LoadTextData(ILocalizationDataProvider provider, string code)
        {
            LocalizationLogger.Log($"Loading text data for '{code}'...");
            var data = provider.LoadTextData(code)
                ?? throw new InvalidOperationException($"Data provider returned null text data for '{code}'. Return an empty dictionary when no data exists.");
            // Snapshot collections so a provider reusing its buffers cannot alter the active cache.
            var snapshot = new Dictionary<string, string>(data, data.Comparer);
            LocalizationLogger.Log($"Loaded {snapshot.Count} keys for '{code}'");
            return snapshot;
        }

        private static List<LocalizationAssetTable> LoadAssetTables(ILocalizationDataProvider provider, string code)
        {
            var tables = provider.LoadAssetTables(code)
                ?? throw new InvalidOperationException($"Data provider returned null asset tables for '{code}'. Return an empty list when no tables exist.");
            return new List<LocalizationAssetTable>(tables);
        }
    }
}
