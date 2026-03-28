using TMPro;
using UnityEngine;

namespace SimplyLocalize
{
    /// <summary>
    /// Complete definition of a language: identity, typography, and layout.
    /// Create one asset per language via Create → SimplyLocalize → Language Profile.
    ///
    /// The profile is the single source of truth — LocalizationConfig references
    /// these assets directly (drag-and-drop), eliminating manual string entry.
    /// </summary>
    [CreateAssetMenu(
        fileName = "LanguageProfile",
        menuName = "SimplyLocalize/Language Profile")]
    public class LanguageProfile : ScriptableObject
    {
        // ──────────────────────────────────────────────
        //  Identity
        // ──────────────────────────────────────────────

        [Header("Language Identity")]
        [Tooltip("ISO 639-1 language code used for folder names and data lookup (e.g. en, ru, ja, ar)")]
        public string languageCode;

        [Tooltip("Human-readable name shown in UI (e.g. English, Русский, 日本語)")]
        public string displayName;

        [Tooltip("Matching Unity SystemLanguage for auto-detection on startup")]
        public SystemLanguage systemLanguage = SystemLanguage.Unknown;

        // ──────────────────────────────────────────────
        //  Assets
        // ──────────────────────────────────────────────

        [Header("Available Assets")]
        [Tooltip("Whether text translations exist for this language")]
        public bool hasText = true;

        [Tooltip("Whether localized sprites exist for this language")]
        public bool hasSprites;

        [Tooltip("Whether localized audio clips exist for this language")]
        public bool hasAudio;

        // ──────────────────────────────────────────────
        //  Font
        // ──────────────────────────────────────────────

        [Header("Font")]
        [Tooltip("Primary font asset for this language")]
        public TMP_FontAsset primaryFont;

        [Tooltip("Fallback font for missing glyphs")]
        public TMP_FontAsset fallbackFont;

        // ──────────────────────────────────────────────
        //  Typography
        // ──────────────────────────────────────────────

        [Header("Typography")]
        [Tooltip("Font size multiplier relative to the original size (1.0 = no change)")]
        [Range(0.5f, 2f)]
        public float fontSizeMultiplier = 1f;

        [Tooltip("Font weight override")]
        public FontWeight fontWeight = FontWeight.Regular;

        [Tooltip("Font style override")]
        public FontStyles fontStyle = FontStyles.Normal;

        // ──────────────────────────────────────────────
        //  Spacing
        // ──────────────────────────────────────────────

        [Header("Spacing")]
        [Tooltip("Additional line spacing offset")]
        public float lineSpacingAdjustment;

        [Tooltip("Additional character spacing offset")]
        public float characterSpacingAdjustment;

        [Tooltip("Additional word spacing offset")]
        public float wordSpacingAdjustment;

        // ──────────────────────────────────────────────
        //  Layout
        // ──────────────────────────────────────────────

        [Header("Layout")]
        [Tooltip("Text direction for this language")]
        public TextDirection textDirection = TextDirection.LTR;

        [Tooltip("Override text alignment (useful for RTL languages)")]
        public bool overrideAlignment;

        [Tooltip("Forced alignment when overrideAlignment is enabled")]
        public TextAlignmentOptions alignmentOverride = TextAlignmentOptions.Right;

        // ──────────────────────────────────────────────
        //  Computed
        // ──────────────────────────────────────────────

        /// <summary>Short accessor for the language code.</summary>
        public string Code => languageCode;

        public bool IsRTL => textDirection == TextDirection.RTL;

        // ──────────────────────────────────────────────
        //  Methods
        // ──────────────────────────────────────────────

        /// <summary>
        /// Applies this profile's typography and layout settings to a TMP_Text component.
        /// </summary>
        public void ApplyTo(TMP_Text text)
        {
            if (text == null)
                return;

            if (primaryFont != null)
            {
                text.font = primaryFont;

                if (fallbackFont != null
                    && !primaryFont.fallbackFontAssetTable.Contains(fallbackFont))
                {
                    primaryFont.fallbackFontAssetTable.Add(fallbackFont);
                }
            }

            text.fontWeight = fontWeight;
            text.fontStyle = fontStyle;
            text.lineSpacing += lineSpacingAdjustment;
            text.characterSpacing += characterSpacingAdjustment;
            text.wordSpacing += wordSpacingAdjustment;

            if (fontSizeMultiplier != 1f)
                text.fontSize *= fontSizeMultiplier;

            if (overrideAlignment)
                text.alignment = alignmentOverride;

            text.isRightToLeftText = IsRTL;
        }
    }
}