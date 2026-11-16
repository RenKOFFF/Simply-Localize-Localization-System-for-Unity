using UnityEngine;
namespace SimplyLocalize.Tests
{
    public class RenameReferenceComponent : MonoBehaviour
    {
        [LocalizationKey] public string key;
        public RenameNestedReference nested = new();
        public string literal;
    }
}
