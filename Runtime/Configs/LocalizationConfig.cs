using UnityEngine;

namespace SimplyLocalize
{
    public class LocalizationConfig : ScriptableObject
    {
        [field: SerializeField] public SpaceUsage SpaceIsGroupSeparator { get; set; } = SpaceUsage.GroupSeparator;
        [field: SerializeField] public bool EnableLogging { get; set; } = true;
        [field: SerializeField] public bool EnableLoggingInBuild { get; set; }
        [field: SerializeField] public bool TranslateInEditor { get; set; }
        [field: SerializeField] public bool ChangeDefaultLanguageWhenCantTranslateInEditor { get; set; } = true;
        [field: SerializeField] public bool ShowLanguagePopup { get; set; } = true;
        [field: SerializeField] public bool ShowAppLocalizationGitPackagePopup { get; set; } = true;

        public enum SpaceUsage 
        {
            GroupSeparator,
            Underline,
            Original
        }
    }
}