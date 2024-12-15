using TMPro;
using UnityEngine;

namespace SimplyLocalize.Runtime.Data
{
    public class FontHolder : ScriptableObject
    {
        [field:SerializeField] public TMP_FontAsset TMPFont { get; set; }
        [field:SerializeField] public Font LegacyFont { get; set; }

    }
}