using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using SimplyLocalize.Runtime.Data;
using SimplyLocalize.Runtime.Data.Keys;
using SimplyLocalize.Runtime.Data.Keys.Generated;
using UnityEngine;

namespace SimplyLocalize.Runtime.Main
{
    public static class Localization
    {
        private static AllLocalizationsDictionary _allLocalizations;
        private static Dictionary<string, string> _currentLocalization;

        private static FontHolder _fontHolder;
        
        public static AllLocalizationsDictionary AllLocalizations
        {
            get
            {
                if (_allLocalizations != null) return _allLocalizations;

                if (!TryLoadDefaultLanguage())
                {
                    throw new NotImplementedException(
                        $"{nameof(AllLocalizations)} is empty. " +
                        $"You should call {nameof(Localization)}.{nameof(SetLocalization)} method before use localization components " +
                        $"or set default localization data in {nameof(LocalizationKeysData)} asset.");
                }

                return _allLocalizations;
            }
        }

        public static Dictionary<string, string> CurrentLocalization
        {
            get
            {
                if (_currentLocalization != null) return _currentLocalization;

                AllLocalizations.Translations.TryGetValue(CurrentLanguage, out _currentLocalization);

                return _currentLocalization;
            }
        }

        public static string CurrentLanguage { get; private set; }

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
                Debug.LogWarning($"{nameof(localization)} not founded");
                return false;
            }

            var allLocalizations = JsonConvert.DeserializeObject<AllLocalizationsDictionary>(localization.text);
            allLocalizations.Translations.TryGetValue(localizationData.i18nLang, out _currentLocalization);

            _allLocalizations = allLocalizations;
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
                Debug.LogWarning($"{nameof(localizationResource)} not founded");
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
            var data = Resources.Load<LocalizationKeysData>(nameof(LocalizationKeysData));
            if (data.DefaultLocalizationData != null)
            {
                SetLocalization(data.DefaultLocalizationData);

                Debug.Log($"Used default language: {CurrentLanguage}, please call the method: " +
                          $"{nameof(Localization)}.{nameof(SetLocalization)} to use another one.");

                return true;
            }

            return false;
        }
    }
}