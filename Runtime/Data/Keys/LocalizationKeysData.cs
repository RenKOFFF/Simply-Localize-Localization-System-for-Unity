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
                Name = "Sample"
            }
        };

        private void OnValidate()
        {
            for (var i = 0; i < Keys.Length; i++)
            {
                var key = Keys[i];
                key.Name = key.Name.ToPascalCase();
                
                if (i > 0 && key.Name == Keys[i - 1].Name)
                {
                    key.Name = $"{key.Name}2";
                }
            }
        }
    }
}