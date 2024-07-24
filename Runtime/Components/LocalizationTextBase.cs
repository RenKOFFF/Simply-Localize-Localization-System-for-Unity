using SimplyLocalize.Runtime.Data;
using SimplyLocalize.Runtime.Data.Keys.Generated;
using SimplyLocalize.Runtime.Data.StringEnum;
using SimplyLocalize.Runtime.Main;
using UnityEngine;

namespace SimplyLocalize.Runtime.Components
{
    public abstract class LocalizationTextBase : MonoBehaviour
    {
        private static string _lang;
        private static FontHolder _overrideFontHolder;

        [SerializeField] private StringEnum<LocalizationKey> _localizationKey;
        
        private bool _isInitialized;
        
        public bool Translated { get; protected set; }
        public string DefaultText { get; protected set; }
        public LocalizationKey LocalizationKey => _localizationKey;
        
        private static LocalizationDictionary AllLocalizationsDict => Localization.LocalizationDictionary;

        private void Awake()
        {
            if (!_isInitialized) Init();
        }

        public void TranslateByKey(LocalizationKey key)
        {
            _localizationKey = key;

            if (_isInitialized == false) 
                Init();

            SetTextByKey();
        }

        public void TranslateByKey(string key)
        {
            if (_isInitialized == false) 
                Init();

            SetTextByKey(key);
        }

        public static bool TryGetKey(LocalizationKey localizationKey, out string key)
        {
            return LocalizationKeys.Keys.TryGetValue(localizationKey, out key);
        }
        
        /// <summary>
        /// Concatenates the given string 'str' with the 'DefaultText' and a separator.
        /// </summary>
        /// <param name="str">Additional string</param>
        /// <param name="separator">Separator between 'DefaultText' and 'str'</param>
        public abstract void Concatenate(string str, string separator = " ");

        public static bool TryGetTranslatedText(LocalizationKey localizationKey, out string translated)
        {
            translated = "***";
            return LocalizationKeys.Keys.TryGetValue(localizationKey, out var key) &&
                   AllLocalizationsDict.TryGetTranslating(_lang, key, out translated);
        }

        public static void ApplyLocalization()
        {
            _lang = Localization.CurrentLanguage;
            if (Localization.TryGetFontHolder(out var fontHolder))
            {
                _overrideFontHolder = fontHolder;
            }
        }

        protected abstract void SetTranslate(string translatedText);
        protected abstract void OnHasNotTranslated(string key);

        protected void ApplyTranslate(string translatedText)
        {
            DefaultText = translatedText;
            Translated = true;
        }

        protected abstract void SetFont(FontHolder overrideFontHolder);
        
        private void SetTextByKey()
        {
            if (TryGetKey(LocalizationKey, out var key))
                SetTextByKey(key);
            else Debug.LogWarning($"Localization key {LocalizationKey} not founded");
        }

        private void SetTextByKey(string key)
        {
            if (AllLocalizationsDict.TryGetTranslating(_lang, key, out var translatedText))
            {
                ApplyTranslate(translatedText);
                SetTranslate(translatedText);
            }
            else OnHasNotTranslated(key);
        }

        private void Init()
        {
            SetTextByKey();

            if (_overrideFontHolder)
            {
                SetFont(_overrideFontHolder);
            }
            _isInitialized = true;
        }
    }
}