using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace SimplyLocalize.Components
{
    /// <summary>
    /// Overrides font and material per language for a TMP_Text component.
    ///
    /// Works in two modes:
    /// - WITH LocalizedText/FormattableLocalizedText: those components detect this component
    ///   and skip font application from LanguageProfile. This component handles font/material.
    /// - STANDALONE (no localized text component): just changes font/material on language switch.
    ///
    /// Always caches and restores original font/material on language change.
    /// </summary>
    [DefaultExecutionOrder(10)] // After LocalizedText (default 0)
    [RequireComponent(typeof(TMP_Text))]
    [AddComponentMenu("SimplyLocalize/Localized Font Override")]
    public class LocalizedFontOverride : MonoBehaviour
    {
        [SerializeField] private List<FontOverrideEntry> _overrides = new();

        private TMP_Text _text;
        private TMP_FontAsset _originalFont;
        private Material _originalMaterial;
        private bool _cached;

        /// <summary>
        /// Checked by LocalizedText — if true, profile should NOT apply font.
        /// </summary>
        internal bool HasOverrideForCurrentLanguage =>
            !string.IsNullOrEmpty(Localization.CurrentLanguage) &&
            GetEntry(Localization.CurrentLanguage) != null;

        private void OnEnable()
        {
            _text = GetComponent<TMP_Text>();
            CacheOriginals();

            Localization.OnLanguageChanged += OnLanguageChanged;

            if (Localization.IsInitialized)
                Apply(Localization.CurrentLanguage);
        }

        private void OnDisable()
        {
            Localization.OnLanguageChanged -= OnLanguageChanged;
        }

        private void OnLanguageChanged(string lang) => Apply(lang);

        private void Apply(string languageCode)
        {
            if (_text == null || !_cached) return;

            // Restore originals first
            _text.font = _originalFont;
            _text.fontSharedMaterial = _originalMaterial;

            var entry = GetEntry(languageCode);
            if (entry == null) return;

            if (entry.font != null)
                _text.font = entry.font;

            if (entry.material != null)
                _text.fontSharedMaterial = entry.material;
        }

        private FontOverrideEntry GetEntry(string languageCode)
        {
            if (string.IsNullOrEmpty(languageCode)) return null;

            for (int i = 0; i < _overrides.Count; i++)
            {
                if (_overrides[i].profile != null && _overrides[i].profile.Code == languageCode)
                    return _overrides[i];
            }

            return null;
        }

        private void CacheOriginals()
        {
            if (_cached || _text == null) return;
            _originalFont = _text.font;
            _originalMaterial = _text.fontSharedMaterial;
            _cached = true;
        }

        [Serializable]
        public class FontOverrideEntry
        {
            public LanguageProfile profile;
            public TMP_FontAsset font;
            public Material material;
        }
    }
}