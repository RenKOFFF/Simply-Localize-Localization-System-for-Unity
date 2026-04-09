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
    /// Not a MonoBehaviour — lifetime is managed by the static Localization class.
    /// </summary>
    internal class LocalizationManager
    {
        private readonly LocalizationConfig _config;
        private ILocalizationDataProvider _dataProvider;

        private string _currentLanguage;
        private LanguageProfile _currentProfile;

        // Translation data: languageCode → (key → value)
        private readonly Dictionary<string, Dictionary<string, string>> _textCache = new();

        // Parsed template cache: raw string → parsed template (shared across languages)
        private readonly Dictionary<string, ParsedTemplate> _templateCache = new();

        // Asset tables: languageCode → list of loaded tables
        private readonly Dictionary<string, List<LocalizationAssetTable>> _assetTableCache = new();

        internal event Action<string> OnLanguageChanged;
        internal event Action<LanguageProfile> OnProfileChanged;

        internal string CurrentLanguage => _currentLanguage;
        internal LanguageProfile CurrentProfile => _currentProfile;
        internal LocalizationConfig Config => _config;

        internal LocalizationManager(LocalizationConfig config)
        {
            _config = config != null
                ? config
                : throw new ArgumentNullException(nameof(config));

            LocalizationLogger.Enabled = config.enableLogging;
            LocalizationLogger.EnabledInBuild = config.enableLoggingInBuild;
            _dataProvider = new ResourcesDataProvider(config.resourcesBasePath);
        }

        /// <summary>
        /// Replaces the data provider (e.g. to switch from Resources to Addressables).
        /// Clears all cached data.
        /// </summary>
        internal void SetDataProvider(ILocalizationDataProvider provider)
        {
            _dataProvider = provider ?? throw new ArgumentNullException(nameof(provider));
            _textCache.Clear();
            _templateCache.Clear();
            LocalizationLogger.Log("Data provider changed, caches cleared");
        }

        /// <summary>
        /// Sets the active language by profile. Loads data if not already cached.
        /// Fires OnLanguageChanged and OnProfileChanged events.
        /// </summary>
        internal void SetLanguage(LanguageProfile profile)
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
        internal void SetLanguage(string languageCode)
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
                    $"Language '{languageCode}' is not defined in LocalizationConfig");
                return;
            }

            // Ensure text data is loaded
            EnsureTextDataLoaded(languageCode);

            // Preload per-language fallback chain (e.g. uk → ru → en)
            PreloadFallbackChain(profile);

            // Also preload global fallback
            string globalFallback = _config.FallbackLanguageCode;

            if (!string.IsNullOrEmpty(globalFallback) && globalFallback != languageCode)
                EnsureTextDataLoaded(globalFallback);

            _currentLanguage = languageCode;
            _currentProfile = profile;

            LocalizationLogger.Log($"Language set to '{languageCode}' ({profile.displayName})");

            OnLanguageChanged?.Invoke(languageCode);

            if (_currentProfile != null)
                OnProfileChanged?.Invoke(_currentProfile);
        }

        /// <summary>
        /// Gets a translated string by key, with no parameter substitution.
        /// Falls back to fallback language, then to the raw key.
        /// </summary>
        internal string Get(string key)
        {
            if (string.IsNullOrEmpty(key))
                return string.Empty;

            string raw = GetRaw(key);
            var template = GetOrParseTemplate(raw);

            // If template has no tokens beyond text, return raw directly
            if (template == null)
                return raw;

            bool hasOnlyText = true;

            for (int i = 0; i < template.Tokens.Count; i++)
            {
                if (template.Tokens[i].Type != TokenType.Text)
                {
                    hasOnlyText = false;
                    break;
                }
            }

            return hasOnlyText ? raw : TextFormatter.Format(template, _currentLanguage, (object[])null);
        }

        /// <summary>
        /// Gets a translated string by key with indexed parameter substitution and pluralization.
        /// </summary>
        internal string Get(string key, object[] args)
        {
            if (string.IsNullOrEmpty(key))
                return string.Empty;

            string raw = GetRaw(key);
            var template = GetOrParseTemplate(raw);

            if (template == null)
                return raw;

            return TextFormatter.Format(template, _currentLanguage, args);
        }

        /// <summary>
        /// Gets a translated string with named parameter substitution and pluralization.
        /// </summary>
        internal string Get(string key, Dictionary<string, object> namedArgs)
        {
            if (string.IsNullOrEmpty(key))
                return string.Empty;

            string raw = GetRaw(key);
            var template = GetOrParseTemplate(raw);

            if (template == null)
                return raw;

            return TextFormatter.Format(template, _currentLanguage, namedArgs);
        }

        /// <summary>
        /// Gets a translated string with both indexed and named parameters.
        /// </summary>
        internal string Get(string key, object[] args, Dictionary<string, object> namedArgs)
        {
            if (string.IsNullOrEmpty(key))
                return string.Empty;

            string raw = GetRaw(key);
            var template = GetOrParseTemplate(raw);

            if (template == null)
                return raw;

            return TextFormatter.Format(template, _currentLanguage, args, namedArgs);
        }

        /// <summary>
        /// Gets a localized asset of type T from asset tables.
        /// Follows the full fallback chain: current → per-language → global.
        /// </summary>
        internal T GetAsset<T>(string key) where T : UnityEngine.Object
        {
            if (string.IsNullOrEmpty(key)) return null;

            // Try current language
            var result = GetAssetFromTables<T>(key, _currentLanguage);
            if (result != null) return result;

            // Per-language fallback chain
            if (_currentProfile != null)
            {
                var visited = new HashSet<string> { _currentLanguage };
                var fb = _currentProfile.fallbackProfile;

                while (fb != null && visited.Add(fb.Code))
                {
                    result = GetAssetFromTables<T>(key, fb.Code);
                    if (result != null) return result;
                    fb = fb.fallbackProfile;
                }
            }

            // Global fallback
            string gf = _config.FallbackLanguageCode;

            if (!string.IsNullOrEmpty(gf) && gf != _currentLanguage)
                result = GetAssetFromTables<T>(key, gf);

            return result;
        }

        private T GetAssetFromTables<T>(string key, string languageCode) where T : UnityEngine.Object
        {
            if (string.IsNullOrEmpty(languageCode)) return null;

            EnsureAssetTablesLoaded(languageCode);

            if (!_assetTableCache.TryGetValue(languageCode, out var tables))
                return null;

            for (int i = 0; i < tables.Count; i++)
            {
                var asset = tables[i].Get<T>(key);
                if (asset != null) return asset;
            }

            return null;
        }

        /// <summary>
        /// Checks whether a key exists in the current language or fallback.
        /// </summary>
        internal bool HasKey(string key)
        {
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(_currentLanguage))
                return false;

            if (TryGetText(key, _currentLanguage, out _))
                return true;

            // Per-language fallback chain
            if (_currentProfile != null)
            {
                var visited = new HashSet<string> { _currentLanguage };
                var fb = _currentProfile.fallbackProfile;

                while (fb != null && visited.Add(fb.Code))
                {
                    if (TryGetText(key, fb.Code, out _))
                        return true;
                    fb = fb.fallbackProfile;
                }
            }

            // Global fallback
            string gf = _config.FallbackLanguageCode;
            return !string.IsNullOrEmpty(gf) && TryGetText(key, gf, out _);
        }

        /// <summary>
        /// Checks whether a specific language has a translation for the given key.
        /// </summary>
        internal bool HasTranslation(string key, string languageCode)
        {
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(languageCode))
                return false;

            EnsureTextDataLoaded(languageCode);

            return _textCache.TryGetValue(languageCode, out var data) && data.ContainsKey(key);
        }

        /// <summary>
        /// Returns all keys loaded for the current language.
        /// </summary>
        internal IEnumerable<string> GetAllKeys()
        {
            if (!string.IsNullOrEmpty(_currentLanguage)
                && _textCache.TryGetValue(_currentLanguage, out var data))
            {
                return data.Keys;
            }

            return Array.Empty<string>();
        }

        /// <summary>
        /// Clears all caches. Call when reloading data at runtime.
        /// </summary>
        internal void ClearCaches()
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
        private string GetRaw(string key)
        {
            // Try current language
            if (TryGetText(key, _currentLanguage, out var value))
                return value;

            // Walk per-language fallback chain
            if (_currentProfile != null)
            {
                var visited = new HashSet<string> { _currentLanguage };
                var fallback = _currentProfile.fallbackProfile;

                while (fallback != null && visited.Add(fallback.Code))
                {
                    EnsureTextDataLoaded(fallback.Code);

                    if (TryGetText(key, fallback.Code, out var fbValue))
                    {
                        LocalizationLogger.Log(
                            $"Key '{key}' missing for '{_currentLanguage}', found in per-language fallback '{fallback.Code}'");
                        return fbValue;
                    }

                    fallback = fallback.fallbackProfile;
                }
            }

            // Try global fallback
            string globalFallback = _config.FallbackLanguageCode;

            if (!string.IsNullOrEmpty(globalFallback) && globalFallback != _currentLanguage)
            {
                EnsureTextDataLoaded(globalFallback);

                if (TryGetText(key, globalFallback, out var gfValue))
                {
                    LocalizationLogger.Log(
                        $"Key '{key}' missing for '{_currentLanguage}', using global fallback '{globalFallback}'");
                    return gfValue;
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

            return _textCache.TryGetValue(languageCode, out var data)
                && data.TryGetValue(key, out value);
        }

        private void PreloadFallbackChain(LanguageProfile profile)
        {
            if (profile == null) return;

            var visited = new HashSet<string> { profile.Code };
            var fb = profile.fallbackProfile;

            while (fb != null && visited.Add(fb.Code))
            {
                EnsureTextDataLoaded(fb.Code);
                fb = fb.fallbackProfile;
            }
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

            LocalizationLogger.Log($"Loading text data for '{languageCode}'...");
            var data = _dataProvider.LoadTextData(languageCode);
            _textCache[languageCode] = data;
            LocalizationLogger.Log($"Loaded {data.Count} keys for '{languageCode}'");
        }

        private void EnsureAssetTablesLoaded(string languageCode)
        {
            if (_assetTableCache.ContainsKey(languageCode))
                return;

            var tables = _dataProvider.LoadAssetTables(languageCode);
            _assetTableCache[languageCode] = tables;
        }
    }
}