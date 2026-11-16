using System;
using System.Collections.Generic;
using UnityEngine;
namespace SimplyLocalize.Tests
{
    [Serializable] public class RenameNestedReference { [LocalizationKey] public string key; public string literal; [SerializeReference] public RenameNestedReference next; }
    public class RenameReferenceAsset : ScriptableObject
    {
        [LocalizationKey] public string key;
        [LocalizationPreview] public string preview;
        public string literal;
        [LocalizationKey] public List<string> keys = new();
        public List<RenameNestedReference> nested = new();
        [SerializeReference] public RenameNestedReference managed;
    }
}
