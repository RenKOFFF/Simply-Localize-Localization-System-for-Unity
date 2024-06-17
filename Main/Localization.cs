using System;
using System.Collections.Generic;
using System.Linq;
using SimplyLocalize.Data;
using Unity.Plastic.Newtonsoft.Json;
using UnityEngine;

namespace SimplyLocalize.Main
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

        public static bool TryGetFontHolder(out FontHolder fontHolder)
        {
            fontHolder = _fontHolder;
            return _fontHolder != null;
        }

        public static bool SetLocalization(string lang)
        {
            IEnumerable<LocalizationData> allLocalizationData = Resources.LoadAll<LocalizationData>("LocalizationData");

            var localizationResource = allLocalizationData
                .FirstOrDefault(l => l.i18nLang.Equals(lang));

            if (localizationResource != null)
            {
                var localizationDictionary = JsonConvert.DeserializeObject<LocalizationDictionary>
                    (localizationResource.LocalizationJsonFile.text);

                _localization = localizationDictionary;
                _fontHolder = localizationResource.OverrideFontAsset;
                return true;
            }

            Debug.Log($"{nameof(localizationResource)} not founded");
            return false;
        }
    }
}