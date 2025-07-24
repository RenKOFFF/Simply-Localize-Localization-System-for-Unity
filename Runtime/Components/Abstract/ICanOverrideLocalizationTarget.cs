using UnityEngine;

namespace SimplyLocalize
{
    public interface ICanOverrideLocalizationTarget<out T> where T : Component
    {
        public bool OverrideLocalizationTarget { get; }
        public T LocalizationTarget { get; } 
    }
}