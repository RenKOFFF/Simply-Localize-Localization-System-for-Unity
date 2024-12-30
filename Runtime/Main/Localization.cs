using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SimplyLocalize
{
    public static class Localization
    {
        private static LocalizationKeysData _localizationKeysData;
        private static Dictionary<string, Dictionary<string, string>> _allLocalizations;
        private static Dictionary<string, Dictionary<Object, Object>> _allLocalizationsObjects;
        
        private static Dictionary<string, string> _currentLocalization;
        private static Dictionary<Object, Object> _currentLocalizationObjects;

        private static FontHolder _fontHolder;

        public static LocalizationKeysData LocalizationKeysData
        {
            get
            {
                if (_localizationKeysData != null) return _localizationKeysData;
                
                if (!TryLoadDefaultLanguage())
                {
                    throw new NotImplementedException(
                        $"{nameof(AllLocalizations)} is empty. " +
                        $"You should call {nameof(Localization)}.{nameof(SetLocalization)} method before use localization components " +
                        $"or set default localization data in Localization Settings window.");
                }
                
                return _localizationKeysData;
            }
        }
        
        public static Dictionary<string, Dictionary<string, string>> AllLocalizations
        {
            get
            {
                if (_allLocalizations != null) return _allLocalizations;

                if (!TryLoadDefaultLanguage())
                {
                    throw new NotImplementedException(
                        $"{nameof(AllLocalizations)} is empty. " +
                        $"You should call {nameof(Localization)}.{nameof(SetLocalization)} method before use localization components " +
                        $"or set default localization data in Localization Settings window.");
                }

                return _allLocalizations;
            }
        }
        
        public static Dictionary<string, Dictionary<Object, Object>> AllLocalizationsObjects
        {
            get
            {
                if (_allLocalizationsObjects != null) return _allLocalizationsObjects;

                if (!TryLoadDefaultLanguage())
                {
                    throw new NotImplementedException(
                        $"{nameof(AllLocalizations)} is empty. " +
                        $"You should call {nameof(Localization)}.{nameof(SetLocalization)} method before use localization components " +
                        $"or set default localization data in Localization Settings window.");
                }

                return _allLocalizationsObjects;
            }
        }

        public static Dictionary<string, string> CurrentLocalization
        {
            get
            {
                if (_currentLocalization != null) return _currentLocalization;

                AllLocalizations.TryGetValue(CurrentLanguage, out _currentLocalization);

                return _currentLocalization;
            }
        }

        public static Dictionary<Object, Object> CurrentLocalizationObjects
        {
            get
            {
                if (_currentLocalizationObjects != null) return _currentLocalizationObjects;
                
                AllLocalizationsObjects.TryGetValue(CurrentLanguage, out _currentLocalizationObjects);

                return _currentLocalizationObjects;
            }
        }

        public static string CurrentLanguage { get; private set; }
        public static bool EnableLogging => LocalizationKeysData.EnableLogging;
        public static bool LoggingOnlyInEditor => LocalizationKeysData.LoggingInEditorOnly;

        public static event Action LanguageChanged;

        public static bool SetLocalization(LocalizationData localizationData)
        {
            if (CurrentLanguage == localizationData.i18nLang)
            {
                return false;
            }
            
            var localization = Resources.Load<TextAsset>("Localization");
            
            if (localization == null)
            {
                Logging.Log($"{nameof(localization)} not founded", LogType.Warning);
                return false;
            }

            var allLocalizations = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, string>>>(localization.text);
            allLocalizations.TryGetValue(localizationData.i18nLang, out _currentLocalization);
            
            var allLocalizationsObjects = _localizationKeysData.ObjectsTranslations
                .ToDictionary(d => d.Key, d => d.Value
                    .ToDictionary(d => d.Key, d => d.Value));

            allLocalizationsObjects.TryGetValue(localizationData.i18nLang, out _currentLocalizationObjects);
            
            _allLocalizations = allLocalizations;
            _allLocalizationsObjects = allLocalizationsObjects;
            _fontHolder = localizationData.OverrideFontAsset;
            
            if (CurrentLanguage == null)
            {
                CurrentLanguage = localizationData.i18nLang;
            }
            else
            {
                CurrentLanguage = localizationData.i18nLang;
                LanguageChanged?.Invoke();
            }

            return true;
        }

        public static bool SetLocalization(string lang)
        {
            IEnumerable<LocalizationData> allLocalizationData = Resources.LoadAll<LocalizationData>("");
            var localizationResource = allLocalizationData.FirstOrDefault(l => l.i18nLang.Equals(lang));
            
            if (localizationResource == null)
            {
                Logging.Log($"{nameof(localizationResource)} not founded", LogType.Warning);
                return false;
            }

            return SetLocalization(localizationResource);
        }

        public static bool TryGetFontHolder(out FontHolder fontHolder)
        {
            fontHolder = _fontHolder;
            return _fontHolder != null;
        }
        
        public static bool TryGetKey(LocalizationKey localizationKey, out string key)
        {
            return LocalizationKeys.Keys.TryGetValue(localizationKey, out key);
        }

        public static bool TryGetTranslatedText(LocalizationKey localizationKey, out string translated)
        {
            translated = "***";
            return LocalizationKeys.Keys.TryGetValue(localizationKey, out var key) &&
                   CurrentLocalization.TryGetValue(key, out translated);
        }

        private static bool TryLoadDefaultLanguage()
        {
            _localizationKeysData ??= Resources.Load<LocalizationKeysData>(nameof(LocalizationKeysData));
            
            if (_localizationKeysData.DefaultLocalizationData != null)
            {
                SetLocalization(_localizationKeysData.DefaultLocalizationData);

                Logging.Log($"Used default language: {CurrentLanguage}, please call the method: " +
                            $"{nameof(Localization)}.{nameof(SetLocalization)} to use another one.");

                return true;
            }

            return false;
        }
    }
}