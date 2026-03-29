using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace SimplyLocalize.Components
{
    /// <summary>
    /// Overrides specific parts of the global LanguageProfile for this component.
    /// Each entry targets a language and selectively overrides font, typography,
    /// spacing, layout, or material — anything not overridden passes through
    /// from the global profile as usual.
    ///
    /// Works with or without LocalizedText on the same GameObject.
    /// When used with LocalizedText, the text component delegates profile application here.
    /// When standalone, subscribes to OnLanguageChanged directly.
    /// </summary>
    [DefaultExecutionOrder(10)]
    [RequireComponent(typeof(TMP_Text))]
    [AddComponentMenu("SimplyLocalize/Localized Profile Override")]
    public class LocalizedProfileOverride : MonoBehaviour
    {
        [SerializeField] private List<OverrideEntry> _entries = new();

        private TMP_Text _text;
        private ProfileApplier _applier;
        private bool _standalone;

        public IReadOnlyList<OverrideEntry> Entries => _entries;

        private void OnEnable()
        {
            _text = GetComponent<TMP_Text>();

            // Check if LocalizedText is present — if not, we work standalone
            _standalone = GetComponent<LocalizedComponentBase>() == null;

            if (_standalone)
            {
                _applier = new ProfileApplier();
                _applier.CacheOriginals(_text);
                Localization.OnLanguageChanged += OnLanguageChanged;

                if (Localization.IsInitialized)
                    ApplyForLanguage(Localization.CurrentLanguage);
            }
        }

        private void OnDisable()
        {
            if (_standalone)
                Localization.OnLanguageChanged -= OnLanguageChanged;
        }

        private void OnLanguageChanged(string lang) => ApplyForLanguage(lang);

        /// <summary>
        /// Called by LocalizedText/FormattableLocalizedText after restoring originals.
        /// Applies the merged profile: global profile + local overrides.
        /// </summary>
        internal void ApplyMerged(TMP_Text text, LanguageProfile globalProfile, ProfileApplier applier)
        {
            if (text == null) return;

            var entry = GetEntry(globalProfile?.Code);

            if (entry == null)
            {
                // No override for this language — apply global profile fully
                applier.Apply(text, globalProfile);
                return;
            }

            // Apply global profile but skip sections that are overridden locally
            applier.Apply(text, globalProfile,
                skipFont: entry.overrideFont,
                skipTypography: entry.overrideTypography,
                skipSpacing: entry.overrideSpacing,
                skipLayout: entry.overrideLayout);

            // Now apply local overrides on top
            ApplyEntry(text, entry);
        }

        private void ApplyForLanguage(string langCode)
        {
            if (_text == null || _applier == null) return;

            var globalProfile = Localization.CurrentProfile;
            ApplyMerged(_text, globalProfile, _applier);
        }

        private void ApplyEntry(TMP_Text text, OverrideEntry entry)
        {
            if (entry.overrideFont)
            {
                if (entry.font != null)
                    text.font = entry.font;

                if (entry.material != null)
                    text.fontSharedMaterial = entry.material;
            }

            if (entry.overrideTypography)
            {
                if (entry.fontSizeMultiplier != 1f)
                    text.fontSize *= entry.fontSizeMultiplier;

                text.fontWeight = entry.fontWeight;
                text.fontStyle = entry.fontStyle;
            }

            if (entry.overrideSpacing)
            {
                text.lineSpacing += entry.lineSpacingAdjustment;
                text.characterSpacing += entry.characterSpacingAdjustment;
                text.wordSpacing += entry.wordSpacingAdjustment;
            }

            if (entry.overrideLayout)
            {
                text.isRightToLeftText = entry.textDirection == TextDirection.RTL;

                if (entry.overrideAlignment)
                    text.alignment = entry.alignmentOverride;
            }
        }

        internal OverrideEntry GetEntry(string languageCode)
        {
            if (string.IsNullOrEmpty(languageCode)) return null;

            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].profile != null && _entries[i].profile.Code == languageCode)
                    return _entries[i];
            }

            return null;
        }

        internal bool HasEntryForLanguage(string languageCode) => GetEntry(languageCode) != null;

        [Serializable]
        public class OverrideEntry
        {
            [Header("Target")]
            public LanguageProfile profile;

            [Header("Font & Material")]
            public bool overrideFont;
            public TMP_FontAsset font;
            public Material material;

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
            public TextDirection textDirection = TextDirection.LTR;
            public bool overrideAlignment;
            public TMPro.TextAlignmentOptions alignmentOverride = TMPro.TextAlignmentOptions.Right;
        }
    }
}