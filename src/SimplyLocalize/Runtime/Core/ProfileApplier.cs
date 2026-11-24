using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SimplyLocalize
{
    /// <summary>
    /// Caches original text settings (including material) and applies localization appearance overrides.
    /// Per-section skip flags allow LocalizedProfileOverride to handle specific sections.
    /// </summary>
    public class ProfileApplier : IDisposable
    {
        private TMP_Text _tmpTarget;
        private Text _legacyTarget;
        private ProfileFontCache.Entry _fontLease;

        // TMP originals
        private TMP_FontAsset _originalFont;
        private Material _originalMaterial;
        private float _originalFontSize;
        private bool _originalAutoSize;
        private float _originalMinSize, _originalMaxSize;
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
        private bool _originalBestFit;
        private int _originalLegacyMinSize, _originalLegacyMaxSize;
        private FontStyle _originalLegacyFontStyle;
        private TextAnchor _originalLegacyAlignment;

        public void CacheOriginals(TMP_Text text)
        {
            if (text == null || _tmpTarget == text) return;
            if (_tmpTarget != null) Apply(_tmpTarget, null);
            ReleaseFont();
            _tmpTarget = text;
            _originalFont = text.font;
            _originalMaterial = text.fontSharedMaterial;
            _originalFontSize = text.fontSize;
            _originalAutoSize = text.enableAutoSizing;
            _originalMinSize = text.fontSizeMin;
            _originalMaxSize = text.fontSizeMax;
            _originalFontWeight = text.fontWeight;
            _originalFontStyle = text.fontStyle;
            _originalLineSpacing = text.lineSpacing;
            _originalCharSpacing = text.characterSpacing;
            _originalWordSpacing = text.wordSpacing;
            _originalAlignment = text.alignment;
            _originalRTL = text.isRightToLeftText;
        }

        public void CacheOriginals(Text text)
        {
            if (text == null || _legacyTarget == text) return;
            if (_legacyTarget != null) Apply(_legacyTarget, null);
            _legacyTarget = text;
            _originalLegacyFont = text.font;
            _originalLegacyFontSize = text.fontSize;
            _originalBestFit = text.resizeTextForBestFit;
            _originalLegacyMinSize = text.resizeTextMinSize;
            _originalLegacyMaxSize = text.resizeTextMaxSize;
            _originalLegacyFontStyle = text.fontStyle;
            _originalLegacyAlignment = text.alignment;
        }

        public void Apply(TMP_Text text, LocalizationAppearance profile,
            bool skipFont = false, bool skipTypography = false,
            bool skipSpacing = false, bool skipLayout = false)
        {
            if (text == null || text != _tmpTarget) return;

            // Restore originals (except skipped sections)
            if (!skipFont)
            {
                var source = profile != null && profile.overrideFont && profile.primaryFont != null
                    ? profile.primaryFont : _originalFont;
                var fallback = profile != null && profile.overrideFont ? profile.fallbackFont : null;
                var material = source == _originalFont ? _originalMaterial : source != null ? source.material : null;
                ApplyFont(text, source, fallback, material);
            }

            if (!skipTypography)
            {
                text.fontSize = _originalFontSize;
                text.enableAutoSizing = _originalAutoSize;
                text.fontSizeMin = _originalMinSize;
                text.fontSizeMax = _originalMaxSize;
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
            if (!skipTypography && profile.overrideTypography)
            {
                ScaleTypography(text, profile.fontSizeMultiplier);
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

        public void Apply(Text text, LocalizationAppearance profile,
            bool skipFont = false, bool skipTypography = false,
            bool skipSpacing = false, bool skipLayout = false)
        {
            if (text == null || text != _legacyTarget) return;

            // Restore originals (except skipped sections)
            if (!skipFont)
                text.font = _originalLegacyFont;

            if (!skipTypography)
            {
                text.fontSize = _originalLegacyFontSize;
                text.resizeTextForBestFit = _originalBestFit;
                text.resizeTextMinSize = _originalLegacyMinSize;
                text.resizeTextMaxSize = _originalLegacyMaxSize;
                text.fontStyle = _originalLegacyFontStyle;
            }

            if (!skipLayout)
                text.alignment = _originalLegacyAlignment;

            if (profile == null) return;

            if (!skipFont && profile.overrideFont && profile.legacyFont != null)
            {
                text.font = profile.legacyFont;
            }

            if (!skipTypography && profile.overrideTypography)
            {
                ScaleTypography(text, profile.fontSizeMultiplier);
                bool bold = (profile.fontStyle & FontStyles.Bold) != 0;
                bool italic = (profile.fontStyle & FontStyles.Italic) != 0;
                text.fontStyle = bold ? (italic ? FontStyle.BoldAndItalic : FontStyle.Bold)
                    : italic ? FontStyle.Italic : FontStyle.Normal;
            }

            if (!skipLayout && profile.overrideLayout && profile.overrideAlignment)
            {
                text.alignment = profile.alignmentOverride switch
                {
                    TextAlignmentOptions.TopLeft => TextAnchor.UpperLeft,
                    TextAlignmentOptions.Top => TextAnchor.UpperCenter,
                    TextAlignmentOptions.TopRight => TextAnchor.UpperRight,
                    TextAlignmentOptions.Left => TextAnchor.MiddleLeft,
                    TextAlignmentOptions.Center => TextAnchor.MiddleCenter,
                    TextAlignmentOptions.Right => TextAnchor.MiddleRight,
                    TextAlignmentOptions.BottomLeft => TextAnchor.LowerLeft,
                    TextAlignmentOptions.Bottom => TextAnchor.LowerCenter,
                    TextAlignmentOptions.BottomRight => TextAnchor.LowerRight,
                    _ => _originalLegacyAlignment
                };
            }
        }

        internal static void ScaleTypography(TMP_Text text, float multiplier)
        {
            text.fontSize *= multiplier;
            text.fontSizeMin *= multiplier;
            text.fontSizeMax *= multiplier;
        }

        internal static void ScaleTypography(Text text, float multiplier)
        {
            text.fontSize = Mathf.RoundToInt(text.fontSize * multiplier);
            text.resizeTextMinSize = Mathf.RoundToInt(text.resizeTextMinSize * multiplier);
            text.resizeTextMaxSize = Mathf.RoundToInt(text.resizeTextMaxSize * multiplier);
        }

        private void ApplyFont(TMP_Text text, TMP_FontAsset source, TMP_FontAsset fallback, Material material)
        {
            var previous = _fontLease;
            bool needsVariant = source != null && fallback != null && fallback != source
                && (source.fallbackFontAssetTable == null || !source.fallbackFontAssetTable.Contains(fallback));
            if (!needsVariant) _fontLease = null;
            else if (_fontLease == null || _fontLease.Source != source || _fontLease.Fallback != fallback)
                _fontLease = ProfileFontCache.Acquire(source, fallback);

            text.font = _fontLease != null ? _fontLease.Font : source;
            text.fontSharedMaterial = material;
            if (previous != _fontLease) ProfileFontCache.Release(previous);
        }

        /// <summary>Restores cached settings and releases transient fonts, keeping the baseline for re-enable.</summary>
        public void Restore()
        {
            if (_tmpTarget != null) Apply(_tmpTarget, null);
            if (_legacyTarget != null) Apply(_legacyTarget, null);
            ReleaseFont();
        }

        /// <summary>Restores targets, releases owned font variants and forgets the cached targets.</summary>
        public void Dispose()
        {
            Restore();
            _tmpTarget = null;
            _legacyTarget = null;
        }

        private void ReleaseFont()
        {
            ProfileFontCache.Release(_fontLease);
            _fontLease = null;
        }
    }
}
