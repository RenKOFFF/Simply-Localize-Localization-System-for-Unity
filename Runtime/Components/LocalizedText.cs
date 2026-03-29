using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SimplyLocalize.Components
{
    /// <summary>
    /// Localizes a static text element (TextMeshPro or legacy Text).
    /// Automatically detects the text component on the same GameObject.
    ///
    /// Applies both the translated string and the language profile settings
    /// (font, size multiplier, weight, style, spacing, RTL).
    ///
    /// For text with dynamic parameters, use FormattableLocalizedText instead.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("SimplyLocalize/Localized Text")]
    public class LocalizedText : LocalizedComponentBase
    {
        private TMP_Text _tmpText;
        private Text _legacyText;

        // Cached original values for profile application
        private float _originalFontSize;
        private bool _originalsCached;

        protected override void OnEnable()
        {
            CacheComponents();
            base.OnEnable();

            Localization.OnProfileChanged += HandleProfileChanged;
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            Localization.OnProfileChanged -= HandleProfileChanged;
        }

        public override void Refresh()
        {
            if (string.IsNullOrEmpty(_key))
                return;

            CacheComponents();

            string text = Localization.Get(_key);

            if (_tmpText != null)
            {
                _tmpText.text = text;
                ApplyProfile(_tmpText);
            }
            else if (_legacyText != null)
            {
                _legacyText.text = text;
                ApplyProfile(_legacyText);
            }
        }

        private void HandleProfileChanged(LanguageProfile profile)
        {
            if (_tmpText != null)
                ApplyProfile(_tmpText);
            
            if (_legacyText != null)
                ApplyProfile(_legacyText);
        }

        private void ApplyProfile(TMP_Text text)
        {
            var profile = Localization.CurrentProfile;

            if (profile == null)
                return;

            if (!_originalsCached)
            {
                _originalFontSize = text.fontSize;
                _originalsCached = true;
            }

            if (profile.primaryFont != null)
                text.font = profile.primaryFont;

            text.fontSize = _originalFontSize * profile.fontSizeMultiplier;
            text.fontWeight = profile.fontWeight;
            text.fontStyle = profile.fontStyle;
            text.lineSpacing = profile.lineSpacingAdjustment;
            text.characterSpacing = profile.characterSpacingAdjustment;
            text.wordSpacing = profile.wordSpacingAdjustment;
            text.isRightToLeftText = profile.IsRTL;

            if (profile.overrideAlignment)
                text.alignment = profile.alignmentOverride;
        }

        private void ApplyProfile(Text legacyText)
        {
            throw new System.NotImplementedException();
        }

        private void CacheComponents()
        {
            if (_tmpText == null)
            {
                _tmpText = GetComponent<TMP_Text>();
            }

            if (_legacyText == null)
            {
                _legacyText = GetComponent<Text>();
            }
        }
    }
}
