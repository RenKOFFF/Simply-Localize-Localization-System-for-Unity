using UnityEngine;

namespace SimplyLocalize.Runtime.Data
{
    [CreateAssetMenu(fileName = "LocalizationData_", menuName = "SimplyLocalize/New localization data", order = 0)]
    public class LocalizationData : ScriptableObject
    {
        [field:SerializeField] public string i18nLang { get; private set; }
        [field:SerializeField] public TextAsset LocalizationJsonFile { get; private set; }
        [field:SerializeField] public FontHolder OverrideFontAsset { get; private set; }
    }
}