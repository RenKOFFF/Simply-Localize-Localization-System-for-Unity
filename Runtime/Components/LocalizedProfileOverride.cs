using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SimplyLocalize.Components
{
    /// <summary>
    /// Overrides specific parts of the global LanguageProfile for this component.
    /// Auto-detects whether the target is TMP_Text or legacy Text and shows
    /// appropriate fields in the inspector.
    /// </summary>
    [DefaultExecutionOrder(10)]
    [AddComponentMenu("SimplyLocalize/Localized Profile Override")]
    public class LocalizedProfileOverride : MonoBehaviour
    {
        [SerializeField] private List<OverrideEntry> _entries = new();

        private TMP_Text _tmpText;
        private Text _legacyText;
        private ProfileApplier _applier;
        private bool _standalone;

        public IReadOnlyList<OverrideEntry> Entries => _entries;

        /// <summary>True if this component targets a TMP_Text. False for legacy Text.</summary>
        public bool IsTMP => _tmpText != null;

        /// <summary>True if this component targets a legacy UI Text.</summary>
        public bool IsLegacy => _legacyText != null && _tmpText == null;

        private void OnEnable()
        {
            _tmpText = GetComponent<TMP_Text>();
            _legacyText = GetComponent<Text>();

            _standalone = GetComponent<LocalizedComponentBase>() == null;

            if (_standalone)
            {
                _applier = new ProfileApplier();

                if (_tmpText != null)
                    _applier.CacheOriginals(_tmpText);
                else if (_legacyText != null)
                    _applier.CacheOriginals(_legacyText);

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

        // ──────────────────────────────────────────────
        //  TMP path
        // ──────────────────────────────────────────────

        /// <summary>
        /// Called by LocalizedText after restoring originals (TMP path).
        /// </summary>
        internal void ApplyMerged(TMP_Text text, LanguageProfile globalProfile, ProfileApplier applier)
        {
            if (text == null) return;

            var entry = GetEntry(globalProfile?.Code);

            if (entry == null)
            {
                applier.Apply(text, globalProfile);
                return;
            }

            applier.Apply(text, globalProfile,
                skipFont: entry.overrideFont,
                skipTypography: entry.overrideTypography,
                skipSpacing: entry.overrideSpacing,
                skipLayout: entry.overrideLayout);

            ApplyEntryTMP(text, entry);
        }

        // ──────────────────────────────────────────────
        //  Legacy path
        // ──────────────────────────────────────────────

        /// <summary>
        /// Called by LocalizedText after restoring originals (legacy Text path).
        /// </summary>
        internal void ApplyMerged(Text text, LanguageProfile globalProfile, ProfileApplier applier)
        {
            if (text == null) return;

            var entry = GetEntry(globalProfile?.Code);

            if (entry == null)
            {
                applier.Apply(text, globalProfile);
                return;
            }

            applier.Apply(text, globalProfile,
                skipFont: entry.overrideFont,
                skipTypography: entry.overrideTypography,
                skipLayout: entry.overrideLayout);

            ApplyEntryLegacy(text, entry);
        }

        // ──────────────────────────────────────────────
        //  Standalone (no LocalizedText)
        // ──────────────────────────────────────────────

        private void ApplyForLanguage(string langCode)
        {
            if (_applier == null) return;

            var globalProfile = Localization.CurrentProfile;

            if (_tmpText != null)
                ApplyMerged(_tmpText, globalProfile, _applier);
            else if (_legacyText != null)
                ApplyMerged(_legacyText, globalProfile, _applier);
        }

        // ──────────────────────────────────────────────
        //  Apply entry to specific text type
        // ──────────────────────────────────────────────

        private void ApplyEntryTMP(TMP_Text text, OverrideEntry entry)
        {
            if (entry.overrideFont)
            {
                if (entry.tmpFont != null)
                    text.font = entry.tmpFont;

                if (entry.tmpMaterial != null)
                    text.fontSharedMaterial = entry.tmpMaterial;
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
                    text.alignment = entry.tmpAlignmentOverride;
            }
        }

        private void ApplyEntryLegacy(Text text, OverrideEntry entry)
        {
            if (entry.overrideFont && entry.legacyFont != null)
            {
                text.font = entry.legacyFont;
            }

            if (entry.overrideTypography)
            {
                if (entry.fontSizeMultiplier != 1f)
                    text.fontSize = Mathf.RoundToInt(text.fontSize * entry.fontSizeMultiplier);

                text.fontStyle = entry.legacyFontStyle;
            }

            if (entry.overrideLayout && entry.overrideAlignment)
            {
                text.alignment = entry.legacyAlignmentOverride;
            }
        }

        // ──────────────────────────────────────────────
        //  Entry lookup
        // ──────────────────────────────────────────────

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

            // ── Font ──
            public bool overrideFont;

            // TMP font fields
            public TMP_FontAsset tmpFont;
            public Material tmpMaterial;

            // Legacy font field
            public Font legacyFont;

            // ── Typography ──
            public bool overrideTypography;
            [Range(0.5f, 2f)] public float fontSizeMultiplier = 1f;

            // TMP typography
            public FontWeight fontWeight = FontWeight.Regular;
            public FontStyles fontStyle = FontStyles.Normal;

            // Legacy typography
            public FontStyle legacyFontStyle = FontStyle.Normal;

            // ── Spacing (TMP only) ──
            public bool overrideSpacing;
            public float lineSpacingAdjustment;
            public float characterSpacingAdjustment;
            public float wordSpacingAdjustment;

            // ── Layout ──
            public bool overrideLayout;
            public TextDirection textDirection = TextDirection.LTR;
            public bool overrideAlignment;

            // TMP alignment
            public TextAlignmentOptions tmpAlignmentOverride = TextAlignmentOptions.Right;

            // Legacy alignment
            public TextAnchor legacyAlignmentOverride = TextAnchor.MiddleRight;
        }
    }
}