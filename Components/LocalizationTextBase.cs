using SimplyLocalize.Data;
using SimplyLocalize.Data.Keys;
using SimplyLocalize.Main;
using UnityEngine;

namespace SimplyLocalize.Components
{
    public abstract class LocalizationTextBase : MonoBehaviour
    {
        [SerializeField] private LocalizationKey _localizationKey;

        private static LocalizationDictionary _allLocalizationsDict;
        private static FontHolder _overrideFontHolder;
        
        private bool _isInitialized;

        public bool Translated { get; protected set; }
        public string DefaultText { get; protected set; }
        

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
                   _allLocalizationsDict.Items.TryGetValue(key, out translated);
        }

        public static void ApplyLocalizationDictionary()
        {
            _allLocalizationsDict = Localization.LocalizationDictionary;
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
            if (TryGetKey(_localizationKey, out var key))
                SetTextByKey(key);
            else Debug.Log($"Localization key {_localizationKey} not founded");
        }

        private void SetTextByKey(string key)
        {
            if (_allLocalizationsDict.Items.TryGetValue(key, out var translatedText))
            {
                ApplyTranslate(translatedText);
                SetTranslate(translatedText);
            }
            else OnHasNotTranslated(key);
        }

        private void Init()
        {
            if (_localizationKey != LocalizationKey.None)
            {
                SetTextByKey();
            }
            else
            {
                var o = gameObject;
                // Debug.Log($"The key is missing when awake is called. " +
                //           $"Need to set the key in the inspector or via code." +
                //           $" If the text {o.transform.root.name}/.../" +
                //           $"{o.transform.parent.name}/{o.name} is correct, dont worry");
            }

            if (_overrideFontHolder)
            {
                SetFont(_overrideFontHolder);
            }
            _isInitialized = true;
        }
    }
}