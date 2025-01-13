using System;
using System.Collections.Generic;
using UnityEngine;

namespace SimplyLocalize
{
    public abstract class LocalizationTextBase : MonoBehaviour
    {
        private static FontHolder _overrideFontHolder;

        [SerializeField] private LocalizationKey _localizationKey;
        
        public bool IsInitialized { get; protected set; }

        public bool Translated { get; protected set; }
        public string DefaultText { get; protected set; }
        public LocalizationKey LocalizationKey => _localizationKey;

        public static FontHolder OverrideFontHolder => Localization.TryGetFontHolder(out _overrideFontHolder) ? _overrideFontHolder : null;

        public static Dictionary<string, string> CurrentLocalization => Localization.CurrentLocalization;
        
        public event Action LanguageChanged;

        protected virtual void Awake()
        {
            if (!IsInitialized)
            {
                Initialize();
            }
        }

        private void OnDestroy()
        {
            Localization.LanguageChanged -= SetLanguage;
        }

        public void TranslateByKey(LocalizationKey key)
        {
            _localizationKey = key;

            if (!IsInitialized)
            {
                Initialize();
            }
            else
            {
                SetTextByKey();
            }
        }

        public void TranslateByKey(string key)
        {
            if (!IsInitialized)
            {
                Initialize();
            }
            else
            {
                SetTextByKey(key);
            }
        }

        /// <summary>
        /// Concatenates the given string 'str' with the 'DefaultText' and a separator.
        /// </summary>
        /// <param name="str">Additional string</param>
        /// <param name="separator">Separator between 'DefaultText' and 'str'</param>
        public abstract void Concatenate(string str, string separator = " ");

        protected abstract void SetTranslate(string translatedText);
        protected abstract void OnHasNotTranslated(string key);

        protected virtual void ApplyTranslate(string translatedText)
        {
            DefaultText = translatedText;
            Translated = true;
        }
        
        protected abstract void SaveDefaultFont();
        protected abstract void SetFont(FontHolder overrideFontHolder);
        protected abstract void ResetFont();

        private void SetTextByKey()
        {
            if (Localization.TryGetKey(LocalizationKey, out var key))
                SetTextByKey(key);
            else Logging.Log($"Localization key {LocalizationKey.Key} not founded in object: {gameObject.name}.", LogType.Warning, this);
        }

        private void SetTextByKey(string key)
        {
            if (CurrentLocalization.TryGetValue(key, out var translatedText))
            {
                ApplyTranslate(translatedText);
                SetTranslate(translatedText);
            }
            else OnHasNotTranslated(key);
        }

        private void Initialize()
        {
            SaveDefaultFont();
            
            SetLanguage();
            Localization.LanguageChanged += SetLanguage;

            IsInitialized = true;
        }
        
        private void SetLanguage()
        {
            SetTextByKey();

            if (OverrideFontHolder)
            {
                SetFont(OverrideFontHolder);
            }
            else
            {
                ResetFont();
            }
            
            LanguageChanged?.Invoke();
        }
    }
}