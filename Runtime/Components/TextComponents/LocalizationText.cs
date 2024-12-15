using SimplyLocalize.Runtime.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SimplyLocalize.Runtime.Components.TextComponents
{
    public class LocalizationText : LocalizationTextBase
    {
        [SerializeField] private bool _overrideTextElements;
        
        [SerializeField] protected TMP_Text _textElement;
        [SerializeField] protected Text _textElementLegacy;
        
        private TMP_FontAsset _defaultFont;
        private Font _defaultFontLegacy;

        public virtual TMP_Text TextElement => _textElement;
        public virtual Text TextElementLegacy => _textElementLegacy;
        
        protected virtual void OnValidate()
        {
            _textElement ??= GetComponent<TMP_Text>();
            _textElementLegacy ??= GetComponent<Text>();
        }
        
        public override void Concatenate(string str, string separator = " ")
        {
            var newText = $"{DefaultText}{separator}{str}";

            if (_textElement != null) _textElement.text = newText;
            if (_textElementLegacy != null) _textElementLegacy.text = newText;
        }
        
        protected override void SetTranslate(string translatedText)
        {
            if (_textElement != null) _textElement.text = translatedText;
            if (_textElementLegacy != null) _textElementLegacy.text = translatedText;
        }

        protected override void OnHasNotTranslated(string key)
        {
            if (_textElement != null) _textElement.text = key;
            if (_textElementLegacy != null) _textElementLegacy.text = key;
            
            Debug.Log($"Translated text not founded by key: {key}");
        }

        protected override void SaveDefaultFont()
        {
            if (_textElement != null) _defaultFont = _textElement.font;
            if (_textElementLegacy != null) _defaultFontLegacy = _textElementLegacy.font;
        }

        protected override void SetFont(FontHolder overrideFontHolder)
        {
            if (_textElement != null) _textElement.font = overrideFontHolder.TMPFont;
            if (_textElementLegacy != null) _textElementLegacy.font = overrideFontHolder.LegacyFont;
        }

        protected override void ResetFont()
        {
            if (_textElement != null) _textElement.font = _defaultFont;
            if (_textElementLegacy != null) _textElementLegacy.font = _defaultFontLegacy;
        }
    }
}