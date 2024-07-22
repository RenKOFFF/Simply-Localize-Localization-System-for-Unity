using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using SimplyLocalize.Runtime.Components;
using SimplyLocalize.Runtime.Data;
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
                if (_localization == null)
                    throw new NotImplementedException(
                        $"LocalizationDictionary is empty, check the boot scene and call the method: {nameof(SetLocalization)}()");
                return _localization;
            }
        }

        public static string CurrentLanguage { get; private set; }

        public static bool TryGetFontHolder(out FontHolder fontHolder)
        {
            fontHolder = _fontHolder;
            return _fontHolder != null;
        }

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
                
            LocalizationTextBase.ApplyLocalizationDictionary();
            return true;
        }
    }
}