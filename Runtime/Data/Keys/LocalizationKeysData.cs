using SimplyLocalize.Runtime.Data.Extensions;
using UnityEngine;

namespace SimplyLocalize.Runtime.Data.Keys
{
    public class LocalizationKeysData : ScriptableObject
    {
        [field: SerializeField] public LocalizationData DefaultLocalizationData { get; private set; }
        [field: SerializeField] public EnumHolder[] Keys { get; private set; } =
        {
            new()
            {
                Name = "None"
            }
        };

        private void OnValidate()
        {
            foreach (var key in Keys)
            {
                key.Name = key.Name.ToPascalCase();
            }
        }
    }
}