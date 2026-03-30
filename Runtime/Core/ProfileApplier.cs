using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SimplyLocalize
{
    /// <summary>
    /// Caches original text settings (including material) and applies LanguageProfile overrides.
    /// Per-section skip flags allow LocalizedProfileOverride to handle specific sections.
    /// </summary>
    public class ProfileApplier
    {
        private bool _cached;

        // TMP originals
        private TMP_FontAsset _originalFont;
        private Material _originalMaterial;
        private float _originalFontSize;
        private FontWeight _originalFontWeight;
        private FontStyles _originalFontStyle;
        private float _originalLineSpacing;
        private float _originalCharSpacing;
        private float _originalWordSpacing;
        private TextAlignmentOptions _originalAlignment;
        private bool _originalRTL;

        // Legacy originals
        private Font _originalLegacyFont;
        private int _originalLegacyFontSize;
        private FontStyle _originalLegacyFontStyle;
        private TextAnchor _originalLegacyAlignment;

        public void CacheOriginals(TMP_Text text)
        {
            if (_cached || text == null) return;
            _originalFont = text.font;
            _originalMaterial = text.fontSharedMaterial;
            _originalFontSize = text.fontSize;
            _originalFontWeight = text.fontWeight;
            _originalFontStyle = text.fontStyle;
            _originalLineSpacing = text.lineSpacing;
            _originalCharSpacing = text.characterSpacing;
            _originalWordSpacing = text.wordSpacing;
            _originalAlignment = text.alignment;
            _originalRTL = text.isRightToLeftText;
            _cached = true;
        }

        public void CacheOriginals(Text text)
        {
            if (_cached || text == null) return;
            _originalLegacyFont = text.font;
            _originalLegacyFontSize = text.fontSize;
            _originalLegacyFontStyle = text.fontStyle;
            _originalLegacyAlignment = text.alignment;
            _cached = true;
        }

        public void Apply(TMP_Text text, LanguageProfile profile,
            bool skipFont = false, bool skipTypography = false,
            bool skipSpacing = false, bool skipLayout = false)
        {
            if (text == null || !_cached) return;

            // Restore originals (except skipped sections)
            if (!skipFont)
            {
                text.font = _originalFont;
                text.fontSharedMaterial = _originalMaterial;
            }

            if (!skipTypography)
            {
                text.fontSize = _originalFontSize;
                text.fontWeight = _originalFontWeight;
                text.fontStyle = _originalFontStyle;
            }

            if (!skipSpacing)
            {
                text.lineSpacing = _originalLineSpacing;
                text.characterSpacing = _originalCharSpacing;
                text.wordSpacing = _originalWordSpacing;
            }

            if (!skipLayout)
            {
                text.alignment = _originalAlignment;
                text.isRightToLeftText = _originalRTL;
            }

            if (profile == null) return;

            // Apply global profile for non-skipped sections
            if (!skipFont && profile.overrideFont && profile.primaryFont != null)
            {
                text.font = profile.primaryFont;

                if (profile.fallbackFont != null
                    && !profile.primaryFont.fallbackFontAssetTable.Contains(profile.fallbackFont))
                {
                    profile.primaryFont.fallbackFontAssetTable.Add(profile.fallbackFont);
                }
            }

            if (!skipTypography && profile.overrideTypography)
            {
                text.fontSize = _originalFontSize * profile.fontSizeMultiplier;
                text.fontWeight = profile.fontWeight;
                text.fontStyle = profile.fontStyle;
            }

            if (!skipSpacing && profile.overrideSpacing)
            {
                text.lineSpacing = _originalLineSpacing + profile.lineSpacingAdjustment;
                text.characterSpacing = _originalCharSpacing + profile.characterSpacingAdjustment;
                text.wordSpacing = _originalWordSpacing + profile.wordSpacingAdjustment;
            }

            if (!skipLayout && profile.overrideLayout)
            {
                text.isRightToLeftText = profile.IsRTL;
                if (profile.overrideAlignment)
                    text.alignment = profile.alignmentOverride;
            }
        }

        public void Apply(Text text, LanguageProfile profile)
        {
            if (text == null || !_cached) return;

            text.font = _originalLegacyFont;
            text.fontSize = _originalLegacyFontSize;
            text.fontStyle = _originalLegacyFontStyle;
            text.alignment = _originalLegacyAlignment;

            if (profile == null) return;

            if (profile.overrideFont && profile.legacyFont != null)
            {
                text.font = profile.legacyFont;
            }

            if (profile.overrideTypography)
            {
                text.fontSize = Mathf.RoundToInt(_originalLegacyFontSize * profile.fontSizeMultiplier);
                text.fontStyle = profile.fontStyle switch
                {
                    FontStyles.Bold => FontStyle.Bold,
                    FontStyles.Italic => FontStyle.Italic,
                    _ => FontStyle.Normal
                };
            }

            if (profile.overrideLayout && profile.overrideAlignment)
            {
                text.alignment = profile.alignmentOverride switch
                {
                    TextAlignmentOptions.Left => TextAnchor.MiddleLeft,
                    TextAlignmentOptions.Center => TextAnchor.MiddleCenter,
                    TextAlignmentOptions.Right => TextAnchor.MiddleRight,
                    _ => _originalLegacyAlignment
                };
            }
        }
    }
}