using System;
using UnityEngine;

namespace SimplyLocalize
{
    [Serializable]
    public class LocalizationKey
    {
        [field:SerializeField] private string _key;
        public string Key => _key;

        public override string ToString()
        {
            return _key;
        }
    }
}