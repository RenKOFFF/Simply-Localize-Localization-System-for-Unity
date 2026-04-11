using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SimplyLocalize.Components
{
    [DisallowMultipleComponent]
    [AddComponentMenu("SimplyLocalize/Localized Text")]
    public class LocalizedText : LocalizedComponentBase
    {
        private TMP_Text _tmpText;
        private Text _legacyText;
        private LocalizedProfileOverride _profileOverride;
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
            if (string.IsNullOrEmpty(_key)) return;

            CacheComponents();
            CacheOriginals();

            string text = Localization.Get(_key);

            if (_tmpText != null)
            {
                _tmpText.text = text;
                ApplyProfile(Localization.CurrentProfile);
            }
            else if (_legacyText != null)
            {
                _legacyText.text = text;
                _profileApplier.Apply(_legacyText, Localization.CurrentProfile);
            }
        }

        private void HandleProfileChanged(LanguageProfile profile)
        {
            ApplyProfile(profile);
        }

        private void ApplyProfile(LanguageProfile profile)
        {
            if (_tmpText != null)
            {
                if (_profileOverride != null)
                    _profileOverride.ApplyMerged(_tmpText, profile, _profileApplier);
                else
                    _profileApplier.Apply(_tmpText, profile);
            }
            else if (_legacyText != null)
            {
                _profileApplier.Apply(_legacyText, profile);
            }
        }

        private void CacheComponents()
        {
            if (_tmpText == null && _legacyText == null)
            {
                _tmpText = GetComponent<TMP_Text>();
                if (_tmpText == null) _legacyText = GetComponent<Text>();
                _profileOverride = GetComponent<LocalizedProfileOverride>();
            }
        }

        private void CacheOriginals()
        {
            if (_tmpText != null) _profileApplier.CacheOriginals(_tmpText);
            else if (_legacyText != null) _profileApplier.CacheOriginals(_legacyText);
        }
    }
}