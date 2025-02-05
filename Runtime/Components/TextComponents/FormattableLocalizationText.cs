using System;
using System.Linq;
using UnityEngine;

namespace SimplyLocalize
{
    [AddComponentMenu("Simply Localize/Formattable Localization Text")]
    public class FormattableLocalizationText : LocalizationText
    {
        [SerializeField] private string[] _defaultValues;
        
        private string[] _formattableValue;

        public event Action TranslatedEvent;

        protected override void OnEnable()
        {
            if (_defaultValues is { Length: > 0 })
            {
                SetValue(_defaultValues);
            }
            
            base.OnEnable();
        }

        /// <summary>
        /// To update dynamic text, use the format like this: Hi, {0}. Then in SetValue method, replace the placeholder {0} with the desired value. For example, SetValue("Alex") will display Hi, Alex.
        /// </summary>
        /// <param name="value">You can use placeholders like this: Hi, value</param>
        public void SetValue(params string[] value)
        {
            if (value == null || value.Length == 0)
            {
                if (_defaultValues is { Length: > 0 })
                {
                    value = _defaultValues;
                }
                else
                {
                    value = new[] {""};
                }
            }
            
            _formattableValue = value;

            if (!Translated)
            {
                TranslatedEvent += SetValueWhenTranslated;
            }
            else
            {
                if (_textElement != null) _textElement.text = string.Format(DefaultText, value);
                if (_textElementLegacy != null) _textElementLegacy.text = string.Format(DefaultText, value);
            }
        }

        /// <summary>
        /// To update dynamic text, use the format like this: Current day {0}. Then in SetValue method, replace the placeholder {0} with the desired value. For example, SetValue(1) will display Current day 1.
        /// </summary>
        /// <param name="value">You can use placeholders like this: Current day {0}</param>
        public void SetValue(params int[] value) => SetValue(value.Select(x => x.ToString()).ToArray());

        protected override void SetTranslate(string translatedText)
        {
            TranslatedEvent?.Invoke();
        }

        protected override void ApplyTranslate(string translatedText)
        {
            base.ApplyTranslate(translatedText);
            SetValue(_formattableValue);
        }

        protected void SetValueWhenTranslated()
        {
            TranslatedEvent -= SetValueWhenTranslated;
            SetValue(_formattableValue);
        }
    }
}