using System;

namespace SimplyLocalize.Runtime.Components.Legacy
{
    public class FormattableLocalizationTextLegacy : LocalizationTextLegacy
    {
        private string _deferredValue;

        public event Action TranslatedEvent;

        /// <summary>
        /// To update dynamic text, use the format like this: Hi, {0}. Then in SetValue method, replace the placeholder {0} with the desired value. For example, SetValue("Alex") will display Hi, Alex.
        /// </summary>
        /// <param name="value">You can use placeholders like this: Hi, value</param>
        public void SetValue(string value)
        {
            if (!Translated)
            {
                _deferredValue = value;
                TranslatedEvent += SetValueWhenTranslated;
            }
            else _textElement.text = string.Format(DefaultText, value);
        }

        /// <summary>
        /// To update dynamic text, use the format like this: Current day {0}. Then in SetValue method, replace the placeholder {0} with the desired value. For example, SetValue(1) will display Current day 1.
        /// </summary>
        /// <param name="value">You can use placeholders like this: Current day {0}</param>
        public void SetValue(int value) => SetValue(value.ToString());

        protected override void SetTranslate(string translatedText)
        {
            TranslatedEvent?.Invoke();
        }

        protected override void ApplyTranslate(string translatedText)
        {
            base.ApplyTranslate(translatedText);
            SetValue(_deferredValue);
        }

        protected void SetValueWhenTranslated()
        {
            TranslatedEvent -= SetValueWhenTranslated;
            SetValue(_deferredValue);
        }
    }
}