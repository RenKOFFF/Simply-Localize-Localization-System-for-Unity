using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SimplyLocalize.Components
{
    /// <summary>
    /// Localizes a text element with dynamic parameters.
    ///
    /// Supports two types of parameters:
    ///   - Indexed: {0}, {1} — set via SetArgs(params object[])
    ///   - Named: {playerName}, {level} — set via SetParam("playerName", "Alex")
    ///
    /// Both can have plural forms:
    ///   "{playerName} picked up {count} {count|coin|coins}"
    ///
    /// Default values for named parameters can be set in the Inspector
    /// via the serialized list (name → value pairs).
    ///
    /// Examples:
    ///   Key "level_up"   → "Level {0}"
    ///     component.SetArgs(5);
    ///     Result: "Level 5"
    ///
    ///   Key "greeting"   → "Hello, {playerName}!"
    ///     component.SetParam("playerName", "Alex");
    ///     Result: "Hello, Alex!"
    ///
    ///   Key "loot"       → "{playerName} found {count} {count|apple|apples}"
    ///     component.SetParam("playerName", "Alex");
    ///     component.SetParam("count", 5);
    ///     Result: "Alex found 5 apples"
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("SimplyLocalize/Formattable Localized Text")]
    public class FormattableLocalizedText : LocalizedComponentBase
    {
        [Header("Named Parameters")]
        [Tooltip("Default named parameter values (editable in Inspector)")]
        [SerializeField] private List<NamedParam> _defaultNamedParams = new();

        private TMP_Text _tmpText;
        private Text _legacyText;

        private object[] _indexedArgs;
        private Dictionary<string, object> _namedArgs;

        private float _originalFontSize;
        private bool _originalsCached;

        // ──────────────────────────────────────────────
        //  Indexed parameters: {0}, {1}, ...
        // ──────────────────────────────────────────────

        /// <summary>
        /// Sets all indexed parameter values and refreshes the text.
        /// Usage: component.SetArgs(5, "sword", 100);
        /// </summary>
        public void SetArgs(params object[] args)
        {
            _indexedArgs = args;

            if (isActiveAndEnabled)
                Refresh();
        }

        /// <summary>
        /// Sets a single indexed parameter at the given position.
        /// </summary>
        public void SetArg(int index, object value)
        {
            if (_indexedArgs == null || index >= _indexedArgs.Length)
            {
                var newArgs = new object[index + 1];

                if (_indexedArgs != null)
                    Array.Copy(_indexedArgs, newArgs, _indexedArgs.Length);

                _indexedArgs = newArgs;
            }

            _indexedArgs[index] = value;

            if (isActiveAndEnabled)
                Refresh();
        }

        // ──────────────────────────────────────────────
        //  Named parameters: {playerName}, {count}, ...
        // ──────────────────────────────────────────────

        /// <summary>
        /// Sets a named parameter value and refreshes the text.
        /// Usage: component.SetParam("playerName", "Alex");
        /// </summary>
        public void SetParam(string name, object value)
        {
            _namedArgs ??= new Dictionary<string, object>();
            _namedArgs[name] = value;

            if (isActiveAndEnabled)
                Refresh();
        }

        /// <summary>
        /// Sets multiple named parameters at once and refreshes.
        /// </summary>
        public void SetParams(Dictionary<string, object> parameters)
        {
            _namedArgs ??= new Dictionary<string, object>();

            foreach (var kvp in parameters)
                _namedArgs[kvp.Key] = kvp.Value;

            if (isActiveAndEnabled)
                Refresh();
        }

        /// <summary>
        /// Removes a named parameter.
        /// </summary>
        public void RemoveParam(string name)
        {
            if (_namedArgs != null && _namedArgs.Remove(name) && isActiveAndEnabled)
                Refresh();
        }

        /// <summary>
        /// Clears all parameters (both indexed and named).
        /// </summary>
        public void ClearParams()
        {
            _indexedArgs = null;
            _namedArgs?.Clear();

            if (isActiveAndEnabled)
                Refresh();
        }

        // ──────────────────────────────────────────────
        //  Lifecycle
        // ──────────────────────────────────────────────

        protected override void OnEnable()
        {
            CacheComponents();
            BuildNamedArgsFromDefaults();
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

            string text = Localization.Get(_key, _indexedArgs, _namedArgs);

            if (_tmpText != null)
            {
                _tmpText.text = text;
                ApplyProfile(_tmpText);
            }
            else if (_legacyText != null)
            {
                _legacyText.text = text;
            }
        }

        // ──────────────────────────────────────────────
        //  Internal
        // ──────────────────────────────────────────────

        private void HandleProfileChanged(LanguageProfile profile)
        {
            if (_tmpText != null)
                ApplyProfile(_tmpText);
        }

        private void ApplyProfile(TMP_Text text)
        {
            var profile = Localization.CurrentProfile;

            if (profile == null)
                return;

            if (!_originalsCached)
            {
                _originalFontSize = text.fontSize;
                _originalsCached = true;
            }

            if (profile.primaryFont != null)
                text.font = profile.primaryFont;

            text.fontSize = _originalFontSize * profile.fontSizeMultiplier;
            text.fontWeight = profile.fontWeight;
            text.fontStyle = profile.fontStyle;
            text.lineSpacing = profile.lineSpacingAdjustment;
            text.characterSpacing = profile.characterSpacingAdjustment;
            text.wordSpacing = profile.wordSpacingAdjustment;
            text.isRightToLeftText = profile.IsRTL;

            if (profile.overrideAlignment)
                text.alignment = profile.alignmentOverride;
        }

        private void BuildNamedArgsFromDefaults()
        {
            if (_defaultNamedParams == null || _defaultNamedParams.Count == 0)
                return;

            _namedArgs ??= new Dictionary<string, object>();

            for (int i = 0; i < _defaultNamedParams.Count; i++)
            {
                var param = _defaultNamedParams[i];

                if (string.IsNullOrEmpty(param.name))
                    continue;

                // Don't overwrite values already set from code
                if (!_namedArgs.ContainsKey(param.name))
                    _namedArgs[param.name] = param.ParsedValue;
            }
        }

        private void CacheComponents()
        {
            if (_tmpText == null && _legacyText == null)
            {
                _tmpText = GetComponent<TMP_Text>();

                if (_tmpText == null)
                    _legacyText = GetComponent<Text>();
            }
        }

        // ──────────────────────────────────────────────
        //  Serializable named parameter for Inspector
        // ──────────────────────────────────────────────

        [Serializable]
        public class NamedParam
        {
            [Tooltip("Parameter name as used in the translation string (e.g. playerName, count)")]
            public string name;

            [Tooltip("Default value (parsed as int if possible, otherwise used as string)")]
            public string value;

            /// <summary>
            /// Returns the value parsed as the most specific type possible:
            /// int → float → string.
            /// </summary>
            public object ParsedValue
            {
                get
                {
                    if (string.IsNullOrEmpty(value))
                        return value;

                    if (int.TryParse(value, out int intVal))
                        return intVal;

                    if (float.TryParse(value,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out float floatVal))
                        return floatVal;

                    return value;
                }
            }
        }
    }
}