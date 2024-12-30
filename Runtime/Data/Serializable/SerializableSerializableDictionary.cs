using System;
using System.Collections.Generic;
using UnityEngine;

namespace SimplyLocalize
{
    [Serializable]
    public class SerializableSerializableDictionary<TKey, TValue> : Dictionary<TKey, TValue>, ISerializationCallbackReceiver
    {
        [SerializeField] private List<SerializableDictionaryElement<TKey, TValue>> _elements = new();

        public void OnAfterDeserialize()
        {
            Clear();

            foreach (var element in _elements)
            {
                Add(element.Key, element.Value);
            }
        }

        public void OnBeforeSerialize()
        {
            _elements.Clear();
            
            foreach (var pair in this)
            {
                _elements.Add(new SerializableDictionaryElement<TKey, TValue>
                {
                    Key = pair.Key,
                    Value = pair.Value
                });
            }
        }
    }
}