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
            if (_formattableValue == null && _defaultValues is { Length: > 0 })
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
            var requiredArgs = GetRequiredArgumentsCount(DefaultText);
        
            if (value == null || value.Length < requiredArgs)
            {
                if (_defaultValues != null && _defaultValues.Length == requiredArgs)
                {
                    value = _defaultValues;
                }
                else
                {
                    var newValue = new string[requiredArgs];
                    if (value != null)
                    {
                        Array.Copy(value, newValue, Math.Min(value.Length, requiredArgs));
                    }
                    for (int i = value?.Length ?? 0; i < requiredArgs; i++)
                    {
                        newValue[i] = "";
                    }
                    value = newValue;
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
        
        public int GetRequiredArgumentsCount(string formatString)
        {
            if (string.IsNullOrEmpty(formatString))
                return 0;
        
            var matches = System.Text.RegularExpressions.Regex.Matches(formatString, @"{(\d+)(?:[^}]*)?}");
            var maxIndex = -1;
        
            foreach (System.Text.RegularExpressions.Match match in matches)
            {
                if (int.TryParse(match.Groups[1].Value, out int index) && index > maxIndex)
                {
                    maxIndex = index;
                }
            }
        
            return maxIndex + 1;
        }
    }
}