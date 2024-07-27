using System;
using System.Collections.Generic;

namespace SimplyLocalize.Runtime.Data
{
    [Serializable]
    public class AllLocalizationsDictionary
    {
        public Dictionary<string, Dictionary<string, string>> Translations;

        public bool TryGetTranslating(string lang, string key, out string translated)
        {
            translated = null;
            return TryGetCurrentLocalization(lang, out var currentLocalization) && currentLocalization.TryGetValue(key, out translated);
        }

        public bool TryGetCurrentLocalization(string lang, out Dictionary<string, string> currentLocalization)
        {
            return Translations.TryGetValue(lang, out currentLocalization);
        }
    }
}