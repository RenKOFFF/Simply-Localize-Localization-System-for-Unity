using System;

namespace SimplyLocalize.Runtime.Data.Serializable
{
    [Serializable]
    public class SerializableDictionaryElement<TKey, TValue>
    {
        public TKey Key;
        public TValue Value;
    }
}