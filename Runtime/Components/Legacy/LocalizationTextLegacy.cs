using SimplyLocalize.Runtime.Data;
using UnityEngine;
using UnityEngine.UI;

namespace SimplyLocalize.Runtime.Components.Legacy
{
    public class LocalizationTextLegacy : LocalizationTextBase
    {
        [SerializeField] protected Text _textElement;
        
        public virtual Text TextElement => _textElement;
        
        protected virtual void OnValidate()
        {
            _textElement ??= GetComponent<Text>();
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

        protected override void SetFont(FontHolder overrideFontHolder) => _textElement.font = overrideFontHolder.LegacyFont;
    }
}