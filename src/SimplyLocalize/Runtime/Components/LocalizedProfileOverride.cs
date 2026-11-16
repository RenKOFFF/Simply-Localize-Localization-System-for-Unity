using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SimplyLocalize.Components
{
    /// <summary>
    /// Overrides selected appearance sections for the resolved language of a text component.
    /// Auto-detects whether the target is TMP_Text or legacy Text and shows
    /// appropriate fields in the inspector.
    /// </summary>
    [DefaultExecutionOrder(10)]
    [AddComponentMenu("SimplyLocalize/Localized Profile Override")]
    public class LocalizedProfileOverride : LocalizedComponentBase
    {
        [SerializeField] private List<OverrideEntry> _entries = new();

        private TMP_Text _tmpText;
        private Text _legacyText;
        private ProfileApplier _applier;
        private bool _standalone;
        private LocalizedText _localizedText;
        private LocalizationSourceLease appearanceOwner;

        public IReadOnlyList<OverrideEntry> Entries => _entries;

        /// <summary>True if this component targets a TMP_Text. False for legacy Text.</summary>
        public bool IsTMP => _tmpText != null;

        /// <summary>True if this component targets a legacy UI Text.</summary>
        public bool IsLegacy => _legacyText != null && _tmpText == null;

        protected override void OnEnable()
        {
            _tmpText = GetComponent<TMP_Text>();
            _legacyText = GetComponent<Text>();
            _localizedText = GetComponent<LocalizedText>();
            _standalone = _localizedText == null;
            if (_standalone)
            {
                _applier ??= new ProfileApplier();
                if (_tmpText != null) _applier.CacheOriginals(_tmpText);
                else if (_legacyText != null) _applier.CacheOriginals(_legacyText);
            }
            base.OnEnable();
            if (!_standalone) RefreshOwner();
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            if (!_standalone) RefreshOwner();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            _applier?.Dispose();
        }

        private void RefreshOwner()
        {
            if (_localizedText != null && _localizedText.isActiveAndEnabled) _localizedText.Refresh();
        }

        protected override void ClearLocalizedValue()
        {
            var old = appearanceOwner; appearanceOwner = null;
            try { _applier?.Restore(); }
            finally { ReleaseOwned(old); }
        }
        // ──────────────────────────────────────────────
        //  TMP path
        // ──────────────────────────────────────────────

        /// <summary>
        /// Called by LocalizedText after restoring originals (TMP path).
        /// </summary>
        internal void ApplyMerged(TMP_Text text, LocalizationAppearance globalProfile, string languageCode, ProfileApplier applier)
        {
            if (text == null) return;

            var entry = GetEntry(languageCode);

            if (entry == null)
            {
                applier.Apply(text, globalProfile);
                return;
            }

            // Reset the locally overridden sections too. Their offsets/multipliers must start
            // from the captured baseline, never from the previous language or previous refresh.
            applier.Apply(text, null,
                skipFont: !entry.overrideFont,
                skipTypography: !entry.overrideTypography,
                skipSpacing: !entry.overrideSpacing,
                skipLayout: !entry.overrideLayout);
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
        internal void ApplyMerged(Text text, LocalizationAppearance globalProfile, string languageCode, ProfileApplier applier)
        {
            if (text == null) return;

            var entry = GetEntry(languageCode);

            if (entry == null)
            {
                applier.Apply(text, globalProfile);
                return;
            }

            applier.Apply(text, null,
                skipFont: !entry.overrideFont,
                skipTypography: !entry.overrideTypography,
                skipLayout: !entry.overrideLayout);
            applier.Apply(text, globalProfile,
                skipFont: entry.overrideFont,
                skipTypography: entry.overrideTypography,
                skipLayout: entry.overrideLayout);

            ApplyEntryLegacy(text, entry);
        }

        // ──────────────────────────────────────────────
        //  Standalone (no LocalizedText)
        // ──────────────────────────────────────────────

        public override void Refresh()
        {
            if (!_standalone) { RefreshOwner(); return; }
            if (!IsReady || _applier == null) { ClearLocalizedValue(); return; }
            var language = Channel.ResolvedLanguage;
            var next = Channel.RetainAppearance(language);
            var old = appearanceOwner;
            try
            {
                if (_tmpText != null) ApplyMerged(_tmpText, next?.Data.Appearance, language, _applier);
                else if (_legacyText != null) ApplyMerged(_legacyText, next?.Data.Appearance, language, _applier);
                appearanceOwner = next;
            }
            catch
            {
                appearanceOwner = null;
                try { _applier.Restore(); }
                finally { ReleaseOwned(next); }
                throw;
            }
            finally { ReleaseOwned(old); }
        }
        // ──────────────────────────────────────────────
        //  Apply entry to specific text type
        // ──────────────────────────────────────────────

        private void ApplyEntryTMP(TMP_Text text, OverrideEntry entry)
        {
            if (entry.overrideFont)
            {
                if (entry.tmpFont != null && entry.tmpFont != text.font)
                {
                    text.font = entry.tmpFont;
                    text.fontSharedMaterial = entry.tmpFont.material;
                }

                if (entry.tmpMaterial != null)
                    text.fontSharedMaterial = entry.tmpMaterial;
            }

            if (entry.overrideTypography)
            {
                ProfileApplier.ScaleTypography(text, entry.fontSizeMultiplier);

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
                ProfileApplier.ScaleTypography(text, entry.fontSizeMultiplier);

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
            if (string.IsNullOrEmpty(languageCode) || _entries == null) return null;

            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i] != null && string.Equals(_entries[i].languageCode, languageCode, StringComparison.OrdinalIgnoreCase))
                    return _entries[i];
            }

            return null;
        }

        internal bool HasEntryForLanguage(string languageCode) => GetEntry(languageCode) != null;

        [Serializable]
        public class OverrideEntry
        {
            [Header("Target")]
            public string languageCode;

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
