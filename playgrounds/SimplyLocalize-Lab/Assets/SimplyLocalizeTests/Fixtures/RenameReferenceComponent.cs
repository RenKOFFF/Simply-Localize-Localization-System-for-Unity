using UnityEngine;
namespace SimplyLocalize.Tests
{
    public class RenameReferenceComponent : MonoBehaviour
    {
        public LocalizationEntryReference reference;
        [LocalizationKey] public string key;
        public RenameNestedReference nested = new();
        public string literal;
    }
}
