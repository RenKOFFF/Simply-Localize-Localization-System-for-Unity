using TMPro;
using UnityEngine;

namespace SimplyLocalize
{
    public class FontHolder : ScriptableObject
    {
        [field:SerializeField] public TMP_FontAsset TMPFont { get; set; }
        [field:SerializeField] public Font LegacyFont { get; set; }

    }
}