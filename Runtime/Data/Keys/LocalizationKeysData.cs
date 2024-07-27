using System.Collections.Generic;
using System.Linq;
using SimplyLocalize.Runtime.Data.Extensions;
using UnityEngine;

namespace SimplyLocalize.Runtime.Data.Keys
{
    public class LocalizationKeysData : ScriptableObject
    {
        [field: SerializeField] public LocalizationData DefaultLocalizationData { get; private set; }
        [field: SerializeField] public List<EnumHolder> Keys { get; private set; } = new() { new EnumHolder()
        {
            Name = "Sample",
        } };

        private void OnValidate()
        {
            for (var i = 0; i < Keys.Count; i++)
            {
                var key = Keys[i];
                key.Name = key.Name.ToPascalCase();
                
                if (i > 0 && key.Name == Keys[i - 1].Name)
                {
                    key.Name = $"{key.Name}2";
                }
            }
        }

        public bool TryAddNewKey(string newEnumKey)
        {
            var key = newEnumKey.ToPascalCase();
            if (Keys.Any(x => x.Name == key))
            {
                Debug.LogWarning($"Key {key} already exists");
                return false;
            }
            
            Keys.Add(new EnumHolder
            {
                Name = key
            });
            
            return true;
        }
    }
}