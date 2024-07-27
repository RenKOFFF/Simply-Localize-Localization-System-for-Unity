using SimplyLocalize.Runtime.Data;
using TMPro;
using UnityEngine;

namespace SimplyLocalize.Runtime.Components.TMP
{

    public class LocalizationTextTMP : LocalizationTextBase
    {
        [SerializeField] protected TMP_Text _textElement;
        
        private TMP_FontAsset _defaultFont;

        public virtual TMP_Text TextElement => _textElement;
        
        protected virtual void OnValidate()
        {
            _textElement ??= GetComponent<TMP_Text>();
        }
        
        public override void Concatenate(string str, string separator = " ")
        {
            _textElement.text = $"{DefaultText}{separator}{str}";
        }
        
        protected override void SetTranslate(string translatedText)
        {
            _textElement.text = translatedText;
        }

        protected override void OnHasNotTranslated(string key)
        {
            _textElement.text = key;
            Debug.Log($"Translated text not founded by key: {key}");
        }

        protected override void SaveDefaultFont()
        {
            _defaultFont = _textElement.font;
        }

        protected override void SetFont(FontHolder overrideFontHolder)
        {
            _textElement.font = overrideFontHolder.TMPFont;
        }

        protected override void ResetFont()
        {
            _textElement.font = _defaultFont;
        }
    }
}