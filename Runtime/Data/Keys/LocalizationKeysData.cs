using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SimplyLocalize
{
    public class LocalizationKeysData : ScriptableObject
    {
        [field: SerializeField] public LocalizationData DefaultLocalizationData { get; set; }
#if UNITY_EDITOR
        [field: SerializeField] public List<string> Keys { get; set; } = new() { "Sample"};
        [field: SerializeField] public SerializableSerializableDictionary<string, SerializableSerializableDictionary<string, string>> Translations { get; set; } = new();
#endif
        [field: SerializeField] public SerializableSerializableDictionary<string, SerializableSerializableDictionary<Object, Object>> ObjectsTranslations { get; set; } = new();

        [field: SerializeField] public bool EnableLogging { get; set; } = true;
        [field: SerializeField] public bool LoggingInEditorOnly { get; set; } = true;

#if UNITY_EDITOR
        public bool TryAddNewKey(string newEnumKey)
        {
            var key = newEnumKey.ToEnumName();
            if (Keys.Any(x => x == key))
            {
                Logging.Log($"Key {key} already exists", LogType.Warning);
                return false;
            }
            
            Keys.Add(key);
            
            return true;
        }
#endif
    }
}