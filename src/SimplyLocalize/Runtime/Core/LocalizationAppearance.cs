using TMPro;
using UnityEngine;

namespace SimplyLocalize
{
    /// <summary>Loadable presentation only. Project language definitions refer to its address, not this asset.</summary>
    [CreateAssetMenu(fileName = "LanguageAppearance", menuName = "SimplyLocalize/Language Appearance")]
    public sealed class LocalizationAppearance : ScriptableObject
    {
        public bool overrideFont;
        public TMP_FontAsset primaryFont;
        public TMP_FontAsset fallbackFont;
        public Font legacyFont;
        public bool overrideTypography;
        [Range(0.5f, 2f)] public float fontSizeMultiplier = 1f;
        public FontWeight fontWeight = FontWeight.Regular;
        public FontStyles fontStyle = FontStyles.Normal;
        public bool overrideSpacing;
        public float lineSpacingAdjustment;
        public float characterSpacingAdjustment;
        public float wordSpacingAdjustment;
        public bool overrideLayout;
        public TextDirection textDirection;
        public bool overrideAlignment;
        public TextAlignmentOptions alignmentOverride = TextAlignmentOptions.Right;
    }
}
