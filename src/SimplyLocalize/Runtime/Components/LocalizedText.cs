using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SimplyLocalize.Components
{
    [DisallowMultipleComponent]
    [AddComponentMenu("SimplyLocalize/Localized Text")]
    public class LocalizedText : LocalizedComponentBase
    {
        private TMP_Text tmp;
        private Text legacy;
        private string originalText;
        private bool cached;
        private readonly ProfileApplier applier = new();
        private LocalizationSourceLease appearanceOwner;
        protected override void OnEnable() { CacheTarget(); base.OnEnable(); }
        protected override void OnDestroy() { base.OnDestroy(); applier.Dispose(); }
        public override void Refresh()
        {
            CacheTarget();
            if (!IsReady) { ClearLocalizedValue(); return; }
            bool found = TryResolveText(out var value);
            string language = found ? value.Language : Channel.ResolvedLanguage;
            string text = found ? FormatValue(value) : !string.IsNullOrEmpty(_key) ? _key : originalText;
            var next = Channel.RetainAppearance(language);
            var old = appearanceOwner;
            try
            {
                var appearance = next?.Data.Appearance;
                if (tmp != null) tmp.text = text; else if (legacy != null) legacy.text = text;
                var overrides = GetComponent<LocalizedProfileOverride>();
                if (tmp != null)
                {
                    if (overrides != null && overrides.isActiveAndEnabled) overrides.ApplyMerged(tmp, appearance, language, applier);
                    else applier.Apply(tmp, appearance);
                }
                else if (legacy != null)
                {
                    if (overrides != null && overrides.isActiveAndEnabled) overrides.ApplyMerged(legacy, appearance, language, applier);
                    else applier.Apply(legacy, appearance);
                }
                appearanceOwner = next;
            }
            catch
            {
                appearanceOwner = null;
                try { applier.Restore(); }
                finally { ReleaseOwned(next); }
                throw;
            }
            finally { ReleaseOwned(old); }
        }
        protected virtual string FormatValue(LocalizationTextValue value) => BoundRuntime.Formatter.Format(value, null, null);
        protected override void ClearLocalizedValue()
        {
            var old = appearanceOwner; appearanceOwner = null;
            try
            {
                if (cached)
                {
                    if (tmp != null) tmp.text = originalText; else if (legacy != null) legacy.text = originalText;
                    applier.Restore();
                }
            }
            finally { ReleaseOwned(old); }
        }
        private void CacheTarget()
        {
            if (cached) return;
            tmp = GetComponent<TMP_Text>();
            if (tmp == null) legacy = GetComponent<Text>();
            if (tmp != null) { originalText = tmp.text; applier.CacheOriginals(tmp); }
            else if (legacy != null) { originalText = legacy.text; applier.CacheOriginals(legacy); }
            cached = tmp != null || legacy != null;
        }
    }
}
