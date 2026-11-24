using TMPro;
using System.Collections.Generic;
using UnityEngine;

namespace SimplyLocalize
{
    /// <summary>Loadable presentation only. Project language definitions refer to its address, not this asset.</summary>
    [CreateAssetMenu(fileName = "LanguageAppearance", menuName = "SimplyLocalize/Language Appearance")]
    public class LocalizationAppearance : ScriptableObject, ILocalizationAssetDependencies
    {
        [Header("Font")]
        public bool overrideFont;
        public TMP_FontAsset primaryFont;
        public TMP_FontAsset fallbackFont;
        public Font legacyFont;
        [Header("Typography")]
        public bool overrideTypography;
        [Range(0.5f, 2f)] public float fontSizeMultiplier = 1f;
        public FontWeight fontWeight = FontWeight.Regular;
        public FontStyles fontStyle = FontStyles.Normal;
        [Header("Spacing")]
        public bool overrideSpacing;
        public float lineSpacingAdjustment;
        public float characterSpacingAdjustment;
        public float wordSpacingAdjustment;
        [Header("Layout")]
        public bool overrideLayout;
        public TextDirection textDirection;
        public bool overrideAlignment;
        public TextAlignmentOptions alignmentOverride = TextAlignmentOptions.Right;
        public bool IsRTL => textDirection == TextDirection.RTL;

        public virtual IEnumerable<Object> GetLocalizationDependencies()
        {
            if (primaryFont != null) yield return primaryFont;
            if (fallbackFont != null) yield return fallbackFont;
            if (legacyFont != null) yield return legacyFont;
        }
    }
}
