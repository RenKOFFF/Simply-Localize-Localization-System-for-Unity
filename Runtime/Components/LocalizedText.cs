using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SimplyLocalize.Components
{
    /// <summary>
    /// Localizes a static text element (TMP or legacy Text).
    /// Applies language profile with full caching — switching languages
    /// always restores originals first, then applies overrides.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("SimplyLocalize/Localized Text")]
    public class LocalizedText : LocalizedComponentBase
    {
        private TMP_Text _tmpText;
        private Text _legacyText;
        private LocalizedFontOverride _fontOverride;
        private readonly ProfileApplier _profileApplier = new();

        protected override void OnEnable()
        {
            CacheComponents();
            CacheOriginals();
            base.OnEnable();
            Localization.OnProfileChanged += HandleProfileChanged;
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            Localization.OnProfileChanged -= HandleProfileChanged;
        }

        public override void Refresh()
        {
            if (string.IsNullOrEmpty(_key))
                return;

            CacheComponents();
            CacheOriginals();

            string text = Localization.Get(_key);
            bool skipFont = _fontOverride != null && _fontOverride.HasOverrideForCurrentLanguage;

            if (_tmpText != null)
            {
                _tmpText.text = text;
                _profileApplier.Apply(_tmpText, Localization.CurrentProfile, skipFont);
            }
            else if (_legacyText != null)
            {
                _legacyText.text = text;
                _profileApplier.Apply(_legacyText, Localization.CurrentProfile);
            }
        }

        private void HandleProfileChanged(LanguageProfile profile)
        {
            bool skipFont = _fontOverride != null && _fontOverride.HasOverrideForCurrentLanguage;

            if (_tmpText != null)
                _profileApplier.Apply(_tmpText, profile, skipFont);
            else if (_legacyText != null)
                _profileApplier.Apply(_legacyText, profile);
        }

        private void CacheComponents()
        {
            if (_tmpText == null && _legacyText == null)
            {
                _tmpText = GetComponent<TMP_Text>();
                if (_tmpText == null)
                    _legacyText = GetComponent<Text>();

                _fontOverride = GetComponent<LocalizedFontOverride>();
            }
        }

        private void CacheOriginals()
        {
            if (_tmpText != null)
                _profileApplier.CacheOriginals(_tmpText);
            else if (_legacyText != null)
                _profileApplier.CacheOriginals(_legacyText);
        }
    }
}