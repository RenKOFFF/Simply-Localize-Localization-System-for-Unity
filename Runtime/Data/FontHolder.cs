using TMPro;
using UnityEngine;

namespace SimplyLocalize.Runtime.Data
{
    [CreateAssetMenu(fileName = "LocalizationFontHolder_", menuName = "SimplyLocalize/New font holder", order = 0)]
    public class FontHolder : ScriptableObject
    {
        [field:SerializeField] public TMP_FontAsset TMPFont { get; private set; }
        [field:SerializeField] public Font LegacyFont { get; private set; }

    }
}