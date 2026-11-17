using UnityEngine;
using System.Collections.Generic;

namespace SimplyLocalize
{
    /// <summary>Legacy authoring identity. Presentation fields now belong to LocalizationAppearance.
    /// Retained during the editor/data transition; channel runtimes do not use this type.</summary>
    public class LanguageProfile : LocalizationAppearance
    {
        [Header("Language Identity")]
        public string languageCode;
        public string displayName;
        public SystemLanguage systemLanguage = SystemLanguage.Unknown;
        public LanguageProfile fallbackProfile;
        public string Code => languageCode;
        public string FallbackCode => fallbackProfile != null ? fallbackProfile.Code : null;
        public override IEnumerable<Object> GetLocalizationDependencies()
        {
            foreach (var dependency in base.GetLocalizationDependencies()) yield return dependency;
            if (fallbackProfile != null) yield return fallbackProfile;
        }
    }
}
