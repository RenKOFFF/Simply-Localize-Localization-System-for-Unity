using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace SimplyLocalize.Components
{
    /// <summary>
    /// Overrides the font and material of a TMP_Text component on a per-language basis.
    /// When this component is present, the LanguageProfile's font settings are IGNORED
    /// for this text — only the overrides defined here are applied.
    ///
    /// Use case: a decorative title that needs a specific font per language,
    /// with different TMP materials (outline, shadow, etc.) per language.
    ///
    /// Works alongside LocalizedText or FormattableLocalizedText — this component
    /// handles only visual overrides, the other handles the text content.
    /// </summary>
    [RequireComponent(typeof(TMP_Text))]
    [AddComponentMenu("SimplyLocalize/Localized Font Override")]
    public class LocalizedFontOverride : MonoBehaviour
    {
        [Tooltip("Font/material overrides per language")]
        [SerializeField] private List<FontOverrideEntry> _overrides = new();

        private TMP_Text _text;
        private TMP_FontAsset _originalFont;
        private Material _originalMaterial;
        private bool _cached;

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

        private void OnLanguageChanged(string newLanguage)
        {
            Apply(newLanguage);
        }

        private void Apply(string languageCode)
        {
            if (_text == null || !_cached) return;

            // Restore originals
            _text.font = _originalFont;
            _text.fontSharedMaterial = _originalMaterial;

            if (string.IsNullOrEmpty(languageCode))
                return;

            // Find override for this language
            var entry = GetOverride(languageCode);

            if (entry == null) return;

            if (entry.font != null)
                _text.font = entry.font;

            if (entry.material != null)
                _text.fontSharedMaterial = entry.material;
        }

        private FontOverrideEntry GetOverride(string languageCode)
        {
            for (int i = 0; i < _overrides.Count; i++)
            {
                var entry = _overrides[i];

                if (entry.profile != null && entry.profile.Code == languageCode)
                    return entry;
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
            [Tooltip("The language this override applies to")]
            public LanguageProfile profile;

            [Tooltip("Font to use for this language (null = keep original)")]
            public TMP_FontAsset font;

            [Tooltip("Material to use for this language (null = keep original/font default)")]
            public Material material;
        }
    }
}
