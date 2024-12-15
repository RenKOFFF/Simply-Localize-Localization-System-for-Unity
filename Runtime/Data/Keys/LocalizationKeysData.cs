using System.Collections.Generic;
using System.Linq;
using SimplyLocalize.Runtime.Data.Extensions;
using UnityEngine;

#if UNITY_EDITOR
using SimplyLocalize.Runtime.Data.Serializable;
#endif

namespace SimplyLocalize.Runtime.Data.Keys
{
    public class LocalizationKeysData : ScriptableObject
    {
        [field: SerializeField] public LocalizationData DefaultLocalizationData { get; set; }
#if UNITY_EDITOR
        [field: SerializeField] public List<string> Keys { get; set; } = new() { "Sample"};
        [field: SerializeField] public SerializableSerializableDictionary<string, SerializableSerializableDictionary<string, string>> Translations { get; set; } = new();
#endif
        [field: SerializeField] public SerializableSerializableDictionary<string, SerializableSerializableDictionary<Object, Object>> ObjectsTranslations { get; set; } = new();

        public bool TryAddNewKey(string newEnumKey)
        {
            var key = newEnumKey.ToEnumName();
            if (Keys.Any(x => x == key))
            {
                Debug.LogWarning($"Key {key} already exists");
                return false;
            }
            
            Keys.Add(key);
            
            return true;
        }
    }
}