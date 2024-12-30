using System;

namespace SimplyLocalize
{
    [Serializable]
    public class SerializableDictionaryElement<TKey, TValue>
    {
        public TKey Key;
        public TValue Value;
    }
}