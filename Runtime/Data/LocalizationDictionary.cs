using System;
using System.Collections.Generic;

namespace SimplyLocalize.Runtime.Data
{
    [Serializable]
    public class LocalizationDictionary
    {
        public Dictionary<string, Dictionary<string, string>> Translations;

        public bool TryGetTranslating(string lang, string key, out string translated)
        {
            translated = null;
            return Translations.TryGetValue(lang, out var translations) &&
                   translations.TryGetValue(key, out translated);
        }
    }
}