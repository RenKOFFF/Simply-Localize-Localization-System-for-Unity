using System;
using UnityEditor;
using UnityEngine;

namespace SimplyLocalize
{
    [Serializable]
    public class LocalizationKey : IComparable<LocalizationKey>
    {
        [SerializeField] private string _keyGuid;
        [SerializeField] private string _key;

        public LocalizationKey(string key)
        {
            _key = key;
            _keyGuid = GUID.Generate().ToString();
        }
        public LocalizationKey(string key, bool generateGuid)
        {
            _key = key;
            _keyGuid = generateGuid ? GUID.Generate().ToString() : "00000000000000000000000000000000";
        }

        public string Key
        {
            get => _key;
            set => _key = value;
        }

        public string KeyGuid => _keyGuid;

        public static implicit operator LocalizationKey(string key)
        {
            var localizationKey = new LocalizationKey(key);
            return localizationKey;
        }

        public static implicit operator string(LocalizationKey localizationKey)
        {
            return localizationKey?._key ?? string.Empty;
        }
        
        public override string ToString()
        {
            return _key;
        }

        public static bool operator == (LocalizationKey b1, LocalizationKey b2)
        {
            if ((object)b1 == null)
                return (object)b2 == null;

            return b1.Equals(b2);
        }

        public static bool operator != (LocalizationKey b1, LocalizationKey b2)
        {
            return !(b1 == b2);
        }

        public override bool Equals(object obj)
        {
            if (obj == null || GetType() != obj.GetType())
                return false;

            var b2 = (LocalizationKey)obj;
            return KeyGuid == b2.KeyGuid || Key == b2.Key;
        }

        public override int GetHashCode()
        {
            return KeyGuid.GetHashCode();
        }

        public int CompareTo(LocalizationKey other)
        {
            if (ReferenceEquals(this, other)) return 0;
            if (other is null) return 1;
            
            var keyGuidComparison = string.Compare(_keyGuid, other._keyGuid, StringComparison.Ordinal);
            if (keyGuidComparison != 0) return keyGuidComparison;
            return string.Compare(_key, other._key, StringComparison.Ordinal);
        }
    }
}