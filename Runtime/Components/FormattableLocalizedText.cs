using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SimplyLocalize.Components
{
    /// <summary>
    /// Localizes text with dynamic parameters (indexed and named).
    /// Uses ProfileApplier for full caching and clean language switching.
    /// Parameters are auto-detected from the template in the Inspector.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("SimplyLocalize/Formattable Localized Text")]
    public class FormattableLocalizedText : LocalizedComponentBase
    {
        [Header("Parameters")]
        [SerializeField] private List<NamedParam> _defaultNamedParams = new();

        private TMP_Text _tmpText;
        private Text _legacyText;
        private readonly ProfileApplier _profileApplier = new();

        private object[] _indexedArgs;
        private Dictionary<string, object> _namedArgs;

        // ──────────────────────────────────────────────
        //  Indexed parameters: {0}, {1}
        // ──────────────────────────────────────────────

        public void SetArgs(params object[] args)
        {
            _indexedArgs = args;
            if (isActiveAndEnabled) Refresh();
        }

        public void SetArg(int index, object value)
        {
            if (_indexedArgs == null || index >= _indexedArgs.Length)
            {
                var newArgs = new object[index + 1];
                if (_indexedArgs != null) Array.Copy(_indexedArgs, newArgs, _indexedArgs.Length);
                _indexedArgs = newArgs;
            }

            _indexedArgs[index] = value;
            if (isActiveAndEnabled) Refresh();
        }

        // ──────────────────────────────────────────────
        //  Named parameters: {playerName}, {count}
        // ──────────────────────────────────────────────

        public void SetParam(string name, object value)
        {
            _namedArgs ??= new Dictionary<string, object>();
            _namedArgs[name] = value;
            if (isActiveAndEnabled) Refresh();
        }

        public void SetParams(Dictionary<string, object> parameters)
        {
            _namedArgs ??= new Dictionary<string, object>();
            foreach (var kvp in parameters)
                _namedArgs[kvp.Key] = kvp.Value;
            if (isActiveAndEnabled) Refresh();
        }

        public void ClearParams()
        {
            _indexedArgs = null;
            _namedArgs?.Clear();
            if (isActiveAndEnabled) Refresh();
        }

        // ──────────────────────────────────────────────
        //  Lifecycle
        // ──────────────────────────────────────────────

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

            string text;

            bool hasIndexed = _indexedArgs is { Length: > 0 };
            bool hasNamed = _namedArgs is { Count: > 0 };

            if (hasIndexed || hasNamed)
                text = Localization.Get(_key, _indexedArgs, _namedArgs);
            else
                text = Localization.Get(_key);

            if (_tmpText != null)
            {
                _tmpText.text = text;
                _profileApplier.Apply(_tmpText, Localization.CurrentProfile);
            }
            else if (_legacyText != null)
            {
                _legacyText.text = text;
                _profileApplier.Apply(_legacyText, Localization.CurrentProfile);
            }
        }

        private void HandleProfileChanged(LanguageProfile profile)
        {
            if (_tmpText != null)
                _profileApplier.Apply(_tmpText, profile);
            else if (_legacyText != null)
                _profileApplier.Apply(_legacyText, profile);
        }

        private void BuildNamedArgsFromDefaults()
        {
            if (_defaultNamedParams == null || _defaultNamedParams.Count == 0) return;

            _namedArgs ??= new Dictionary<string, object>();

            for (int i = 0; i < _defaultNamedParams.Count; i++)
            {
                var param = _defaultNamedParams[i];
                if (string.IsNullOrEmpty(param.name)) continue;
                if (!_namedArgs.ContainsKey(param.name))
                    _namedArgs[param.name] = param.ParsedValue;
            }
        }

        private void CacheComponents()
        {
            if (_tmpText == null && _legacyText == null)
            {
                _tmpText = GetComponent<TMP_Text>();
                if (_tmpText == null) _legacyText = GetComponent<Text>();
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
                    if (int.TryParse(value, out int intVal)) return intVal;
                    if (float.TryParse(value,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out float floatVal)) return floatVal;
                    return value;
                }
            }
        }
    }
}
