using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace SimplyLocalize.Components
{
    /// <summary>
    /// Invokes a UnityEvent when the language changes.
    /// Each entry maps a standard language code to a UnityEvent.
    ///
    /// Use cases: switch animations, toggle objects, change layouts,
    /// call custom methods — anything that needs to react to language changes
    /// without writing a custom component.
    ///
    /// If no entry matches the current language, the fallback event is invoked (if set).
    /// </summary>
    [AddComponentMenu("SimplyLocalize/Localized Event")]
    public class LocalizedEvent : LocalizedComponentBase
    {
        [SerializeField]
        [Tooltip("Per-language events. When switching to a language, its event is invoked.")]
        private List<LanguageEventEntry> _entries = new();

        [SerializeField]
        [Tooltip("Invoked when the current language has no matching entry.")]
        private UnityEvent _fallbackEvent;

        public override void Refresh()
        {
            if (!IsReady) return;
            string languageCode = Channel.ResolvedLanguage;
            if (string.IsNullOrEmpty(languageCode)) return;

            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i] != null && string.Equals(_entries[i].languageCode, languageCode, StringComparison.OrdinalIgnoreCase))
                {
                    _entries[i].onLanguageSet?.Invoke();
                    return;
                }
            }

            // No match — invoke fallback
            _fallbackEvent?.Invoke();
        }

        [Serializable]
        public class LanguageEventEntry
        {
            public string languageCode;
            public UnityEvent onLanguageSet;
        }
    }
}