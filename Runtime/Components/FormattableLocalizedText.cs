using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SimplyLocalize.Components
{
    [DisallowMultipleComponent]
    [AddComponentMenu("SimplyLocalize/Formattable Localized Text")]
    public class FormattableLocalizedText : LocalizedComponentBase
    {
        [Header("Parameters")]
        [SerializeField] private List<NamedParam> _defaultNamedParams = new();

        private TMP_Text _tmpText;
        private Text _legacyText;
        private LocalizedProfileOverride _profileOverride;
        private readonly ProfileApplier _profileApplier = new();

        private object[] _indexedArgs;
        private Dictionary<string, object> _namedArgs;

        public void SetArgs(params object[] args)
        {
            _indexedArgs = args;
            if (isActiveAndEnabled) Refresh();
        }

        public void SetArg(int index, object value)
        {
            if (_indexedArgs == null || index >= _indexedArgs.Length)
            {
                var n = new object[index + 1];
                if (_indexedArgs != null) Array.Copy(_indexedArgs, n, _indexedArgs.Length);
                _indexedArgs = n;
            }
            _indexedArgs[index] = value;
            if (isActiveAndEnabled) Refresh();
        }

        public void SetParam(string name, object value)
        {
            _namedArgs ??= new Dictionary<string, object>();
            _namedArgs[name] = value;
            if (isActiveAndEnabled) Refresh();
        }

        public void SetParams(Dictionary<string, object> parameters)
        {
            _namedArgs ??= new Dictionary<string, object>();
            foreach (var kvp in parameters) _namedArgs[kvp.Key] = kvp.Value;
            if (isActiveAndEnabled) Refresh();
        }

        public void ClearParams()
        {
            _indexedArgs = null;
            _namedArgs?.Clear();
            if (isActiveAndEnabled) Refresh();
        }

        protected override void OnEnable()
        {
            CacheComponents();
            CacheOriginals();
            if (_namedArgs == null) BuildNamedArgsFromDefaults();
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

            bool hasIdx = _indexedArgs is { Length: > 0 };
            bool hasNamed = _namedArgs is { Count: > 0 };

            string text = (hasIdx || hasNamed)
                ? Localization.Get(_key, _indexedArgs, _namedArgs)
                : Localization.Get(_key);

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

        private void HandleProfileChanged(LanguageProfile profile) => ApplyProfile(profile);

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

        private void BuildNamedArgsFromDefaults()
        {
            if (_defaultNamedParams == null || _defaultNamedParams.Count == 0) return;

            _namedArgs ??= new Dictionary<string, object>();

            // First pass: find max indexed param to size the array
            int maxIndex = -1;
            for (int i = 0; i < _defaultNamedParams.Count; i++)
            {
                var p = _defaultNamedParams[i];
                if (!string.IsNullOrEmpty(p.name) && int.TryParse(p.name, out int idx) && idx > maxIndex)
                    maxIndex = idx;
            }

            if (maxIndex >= 0)
                _indexedArgs ??= new object[maxIndex + 1];

            // Second pass: route params
            for (int i = 0; i < _defaultNamedParams.Count; i++)
            {
                var p = _defaultNamedParams[i];
                if (string.IsNullOrEmpty(p.name)) continue;

                if (int.TryParse(p.name, out int idx))
                {
                    // Indexed param → goes into _indexedArgs
                    if (_indexedArgs != null && idx < _indexedArgs.Length && _indexedArgs[idx] == null)
                        _indexedArgs[idx] = p.ParsedValue;
                }
                else
                {
                    // Named param → goes into _namedArgs
                    if (!_namedArgs.ContainsKey(p.name))
                        _namedArgs[p.name] = p.ParsedValue;
                }
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

        [Serializable]
        public class NamedParam
        {
            public string name;
            public string value;

            public object ParsedValue
            {
                get
                {
                    if (string.IsNullOrEmpty(value)) return value;
                    if (int.TryParse(value, out int i)) return i;
                    if (float.TryParse(value,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float f)) return f;
                    return value;
                }
            }
        }
    }
}