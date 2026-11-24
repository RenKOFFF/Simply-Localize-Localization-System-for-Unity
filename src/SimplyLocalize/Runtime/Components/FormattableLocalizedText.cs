using System;
using System.Collections.Generic;
using UnityEngine;

namespace SimplyLocalize.Components
{
    [DisallowMultipleComponent]
    [AddComponentMenu("SimplyLocalize/Formattable Localized Text")]
    public class FormattableLocalizedText : LocalizedText
    {
        public const int MaxParameterIndex = 65535;

        [Header("Parameters")]
        [SerializeField] private List<NamedParam> _defaultNamedParams = new();

        private object[] _indexedArgs;
        private Dictionary<string, object> _namedArgs;

        public void SetArgs(params object[] args)
        {
            _indexedArgs = args;
            if (isActiveAndEnabled) Refresh();
        }

        public void SetArg(int index, object value)
        {
            if (index < 0 || index > MaxParameterIndex) throw new ArgumentOutOfRangeException(nameof(index));
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
            if (_namedArgs == null) BuildNamedArgsFromDefaults();
            base.OnEnable();
        }

        protected override string FormatValue(LocalizationTextValue value) =>
            BoundRuntime.Formatter.Format(value, _indexedArgs, _namedArgs);

        private void BuildNamedArgsFromDefaults()
        {
            if (_defaultNamedParams == null || _defaultNamedParams.Count == 0) return;

            _namedArgs ??= new Dictionary<string, object>();

            // First pass: find max indexed param to size the array
            int maxIndex = -1;
            for (int i = 0; i < _defaultNamedParams.Count; i++)
            {
                var p = _defaultNamedParams[i];
                if (!string.IsNullOrEmpty(p.name) && int.TryParse(p.name, out int idx) && idx <= MaxParameterIndex && idx > maxIndex)
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
                    if (_indexedArgs != null && idx >= 0 && idx < _indexedArgs.Length && _indexedArgs[idx] == null)
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
