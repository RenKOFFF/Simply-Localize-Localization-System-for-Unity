using UnityEngine;

namespace SimplyLocalize.Runtime.Data
{
    public class LocalizationData : ScriptableObject
    {
        [field:SerializeField] public string i18nLang { get; set; }
        [field:SerializeField] public FontHolder OverrideFontAsset { get; set; }
    }
}