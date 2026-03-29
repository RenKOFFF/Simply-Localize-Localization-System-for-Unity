using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SimplyLocalize
{
    /// <summary>
    /// Caches original text component settings and applies LanguageProfile overrides.
    /// Restores originals before each new profile application, ensuring clean state.
    /// Supports both TMP_Text and legacy Text.
    /// </summary>
    public class ProfileApplier
    {
        // Cached originals
        private bool _cached;
        private TMP_FontAsset _originalFont;
        private float _originalFontSize;
        private FontWeight _originalFontWeight;
        private FontStyles _originalFontStyle;
        private float _originalLineSpacing;
        private float _originalCharSpacing;
        private float _originalWordSpacing;
        private TextAlignmentOptions _originalAlignment;
        private bool _originalRTL;

        // Legacy Text originals
        private Font _originalLegacyFont;
        private int _originalLegacyFontSize;
        private FontStyle _originalLegacyFontStyle;
        private TextAnchor _originalLegacyAlignment;

        /// <summary>
        /// Caches the current state of a TMP_Text component.
        /// Call once on first enable, before any profile is applied.
        /// </summary>
        public void CacheOriginals(TMP_Text text)
        {
            if (_cached || text == null) return;

            _originalFont = text.font;
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

        /// <summary>
        /// Caches the current state of a legacy Text component.
        /// </summary>
        public void CacheOriginals(Text text)
        {
            if (_cached || text == null) return;

            _originalLegacyFont = text.font;
            _originalLegacyFontSize = text.fontSize;
            _originalLegacyFontStyle = text.fontStyle;
            _originalLegacyAlignment = text.alignment;
            _cached = true;
        }

        /// <summary>
        /// Restores originals then applies the profile's enabled overrides to TMP_Text.
        /// If skipFont is true, font override from profile is skipped (handled by LocalizedFontOverride).
        /// </summary>
        public void Apply(TMP_Text text, LanguageProfile profile, bool skipFont = false)
        {
            if (text == null || !_cached) return;

            // Always restore originals first (except font if skipFont — FontOverride handles it)
            if (!skipFont)
                text.font = _originalFont;

            text.fontSize = _originalFontSize;
            text.fontWeight = _originalFontWeight;
            text.fontStyle = _originalFontStyle;
            text.lineSpacing = _originalLineSpacing;
            text.characterSpacing = _originalCharSpacing;
            text.wordSpacing = _originalWordSpacing;
            text.alignment = _originalAlignment;
            text.isRightToLeftText = _originalRTL;

            if (profile == null) return;

            // Apply only enabled overrides
            if (!skipFont && profile.overrideFont && profile.primaryFont != null)
            {
                text.font = profile.primaryFont;

                if (profile.fallbackFont != null
                    && !profile.primaryFont.fallbackFontAssetTable.Contains(profile.fallbackFont))
                {
                    profile.primaryFont.fallbackFontAssetTable.Add(profile.fallbackFont);
                }
            }

            if (profile.overrideTypography)
            {
                text.fontSize = _originalFontSize * profile.fontSizeMultiplier;
                text.fontWeight = profile.fontWeight;
                text.fontStyle = profile.fontStyle;
            }

            if (profile.overrideSpacing)
            {
                text.lineSpacing = _originalLineSpacing + profile.lineSpacingAdjustment;
                text.characterSpacing = _originalCharSpacing + profile.characterSpacingAdjustment;
                text.wordSpacing = _originalWordSpacing + profile.wordSpacingAdjustment;
            }

            if (profile.overrideLayout)
            {
                text.isRightToLeftText = profile.IsRTL;

                if (profile.overrideAlignment)
                    text.alignment = profile.alignmentOverride;
            }
        }

        /// <summary>
        /// Restores originals then applies the profile's enabled overrides to legacy Text.
        /// </summary>
        public void Apply(Text text, LanguageProfile profile)
        {
            if (text == null || !_cached) return;

            // Restore originals
            text.font = _originalLegacyFont;
            text.fontSize = _originalLegacyFontSize;
            text.fontStyle = _originalLegacyFontStyle;
            text.alignment = _originalLegacyAlignment;

            if (profile == null) return;

            if (profile.overrideTypography)
            {
                text.fontSize = Mathf.RoundToInt(_originalLegacyFontSize * profile.fontSizeMultiplier);

                text.fontStyle = profile.fontStyle switch
                {
                    FontStyles.Bold => FontStyle.Bold,
                    FontStyles.Italic => FontStyle.Italic,
                    FontStyles.Bold | FontStyles.Italic => FontStyle.BoldAndItalic,
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
                    TextAlignmentOptions.TopLeft => TextAnchor.UpperLeft,
                    TextAlignmentOptions.Top => TextAnchor.UpperCenter,
                    TextAlignmentOptions.TopRight => TextAnchor.UpperRight,
                    TextAlignmentOptions.BottomLeft => TextAnchor.LowerLeft,
                    TextAlignmentOptions.Bottom => TextAnchor.LowerCenter,
                    TextAlignmentOptions.BottomRight => TextAnchor.LowerRight,
                    _ => _originalLegacyAlignment
                };
            }
        }
    }
}