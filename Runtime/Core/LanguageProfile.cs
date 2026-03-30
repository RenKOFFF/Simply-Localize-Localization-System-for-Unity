using TMPro;
using UnityEngine;

namespace SimplyLocalize
{
    /// <summary>
    /// Complete definition of a language: identity, typography, and layout.
    /// Each section has an "override" toggle — if disabled, that section is skipped
    /// and the component keeps its original values.
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
        public string languageCode;
        public string displayName;
        public SystemLanguage systemLanguage = SystemLanguage.Unknown;

        // ──────────────────────────────────────────────
        //  Fallback
        // ──────────────────────────────────────────────

        [Header("Fallback")]
        [Tooltip("Per-language fallback. If a key is missing in this language, try this profile's language before the global fallback.\n" +
                 "Example: Ukrainian → Russian → English (global)")]
        public LanguageProfile fallbackProfile;

        // ──────────────────────────────────────────────
        //  Assets
        // ──────────────────────────────────────────────

        [Header("Available Assets")]
        public bool hasText = true;
        public bool hasSprites;
        public bool hasAudio;

        // ──────────────────────────────────────────────
        //  Font
        // ──────────────────────────────────────────────

        [Header("Font")]
        [Tooltip("Enable to override the font for this language")]
        public bool overrideFont;

        [Tooltip("TMP font asset")]
        public TMP_FontAsset primaryFont;

        [Tooltip("TMP fallback font for missing glyphs")]
        public TMP_FontAsset fallbackFont;

        [Tooltip("Legacy UI Text font")]
        public Font legacyFont;

        // ──────────────────────────────────────────────
        //  Typography
        // ──────────────────────────────────────────────

        [Header("Typography")]
        [Tooltip("Enable to override typography settings")]
        public bool overrideTypography;

        [Range(0.5f, 2f)]
        public float fontSizeMultiplier = 1f;
        public FontWeight fontWeight = FontWeight.Regular;
        public FontStyles fontStyle = FontStyles.Normal;

        // ──────────────────────────────────────────────
        //  Spacing
        // ──────────────────────────────────────────────

        [Header("Spacing")]
        [Tooltip("Enable to override spacing settings")]
        public bool overrideSpacing;

        public float lineSpacingAdjustment;
        public float characterSpacingAdjustment;
        public float wordSpacingAdjustment;

        // ──────────────────────────────────────────────
        //  Layout
        // ──────────────────────────────────────────────

        [Header("Layout")]
        [Tooltip("Enable to override layout/direction settings")]
        public bool overrideLayout;

        public TextDirection textDirection = TextDirection.LTR;
        public bool overrideAlignment;
        public TextAlignmentOptions alignmentOverride = TextAlignmentOptions.Right;

        // ──────────────────────────────────────────────
        //  Computed
        // ──────────────────────────────────────────────

        public string Code => languageCode;
        public bool IsRTL => textDirection == TextDirection.RTL;

        /// <summary>
        /// Returns the fallback language code for this profile, or null.
        /// </summary>
        public string FallbackCode => fallbackProfile != null ? fallbackProfile.Code : null;
    }
}