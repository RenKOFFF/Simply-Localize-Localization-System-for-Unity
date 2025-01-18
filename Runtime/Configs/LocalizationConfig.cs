using UnityEngine;

namespace SimplyLocalize
{
    public class LocalizationConfig : ScriptableObject
    {
        [field: SerializeField] public SpaceUsage SpaceIsGroupSeparator { get; set; } = SpaceUsage.GroupSeparator;
        [field: SerializeField] public bool EnableLogging { get; set; } = true;
        [field: SerializeField] public bool LoggingInEditorOnly { get; set; } = true;
        [field: SerializeField] public bool ShowLanguagePopup { get; set; } = true;

        public enum SpaceUsage 
        {
            GroupSeparator,
            Underline,
            Original
        }
    }
}