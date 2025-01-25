using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SimplyLocalize
{
    public class LocalizationKeysData : ScriptableObject
    {
        [field: SerializeField] public LocalizationData DefaultLocalizationData { get; set; }
        [field: SerializeField] public List<LocalizationKey> Keys { get; set; } = new() { new LocalizationKey("Sample", false)};
#if UNITY_EDITOR
        [field: SerializeField] public SerializableDictionary<string, SerializableDictionary<LocalizationKey, string>> Translations { get; set; } = new();
#endif
        [field: SerializeField] public SerializableDictionary<string, SerializableDictionary<Object, Object>> ObjectsTranslations { get; set; } = new();
    }
}