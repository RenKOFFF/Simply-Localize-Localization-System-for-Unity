using System;
using UnityEngine;

namespace SimplyLocalize
{
    [Serializable]
    public class LocalizationKey
    {
        [field:SerializeField] private string _key;
        public string Key => _key;

        public LocalizationKey(string key)
        {
            _key = key;
        }
        
        public static implicit operator LocalizationKey(string key)
        {
            return new LocalizationKey(key);
        }
        
        public override string ToString()
        {
            return _key;
        }
    }
}