using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using SimplyLocalize.Runtime.Components;
using SimplyLocalize.Runtime.Data;
using SimplyLocalize.Runtime.Data.Keys;
using UnityEngine;

namespace SimplyLocalize.Runtime.Main
{
    public static class Localization
    {
        private static LocalizationDictionary _localization;
        private static FontHolder _fontHolder;

        public static LocalizationDictionary LocalizationDictionary
        {
            get
            {
                if (_localization != null) return _localization;
                
                if (!TryLoadDefaultLanguage())
                {
                    throw new NotImplementedException(
                        $"{nameof(LocalizationDictionary)} is empty. " +
                        $"You should call {nameof(Localization)}.{nameof(SetLocalization)} method before use localization components " +
                        $"or set default localization data in {nameof(LocalizationKeysData)} asset.");
                }

                return _localization;
            }
        }

        public static string CurrentLanguage { get; private set; }

        public static bool SetLocalization(string lang)
        {
            IEnumerable<LocalizationData> allLocalizationData = Resources.LoadAll<LocalizationData>("");
            var localization = Resources.Load<TextAsset>("Localization");

            var localizationResource = allLocalizationData
                .FirstOrDefault(l => l.i18nLang.Equals(lang));

            if (localization == null)
            {
                Debug.LogWarning($"{nameof(localization)} not founded");
                return false;
            }

            if (localizationResource == null)
            {
                Debug.LogWarning($"{nameof(localizationResource)} not founded");
                return false;
            }

            CurrentLanguage = lang;

            var localizationDictionary = JsonConvert.DeserializeObject<LocalizationDictionary>(localization.text);

            _localization = localizationDictionary;
            _fontHolder = localizationResource.OverrideFontAsset;

            LocalizationTextBase.ApplyLocalization();
            return true;
        }

        public static bool TryGetFontHolder(out FontHolder fontHolder)
        {
            fontHolder = _fontHolder;
            return _fontHolder != null;
        }

        private static bool TryLoadDefaultLanguage()
        {
            var data = Resources.Load<LocalizationKeysData>(nameof(LocalizationKeysData));
            if (data.DefaultLocalizationData != null)
            {
                SetLocalization(data.DefaultLocalizationData.i18nLang);

                Debug.Log($"Used default language: {CurrentLanguage}, please call the method: " +
                          $"{nameof(Localization)}.{nameof(SetLocalization)} to use another one.");

                return true;
            }

            return false;
        }
    }
}