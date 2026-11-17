using UnityEngine;

namespace SimplyLocalize.HorrorLab
{
    [CreateAssetMenu(menuName = "SimplyLocalize/Samples/World Visual Variant")]
    public sealed class HorrorVisualVariant : ScriptableObject, ILocalizationAssetDependencies
    {
        public Texture2D texture;
        public string contentLanguage;
        public bool containsText = true;
        public System.Collections.Generic.IEnumerable<UnityEngine.Object> GetLocalizationDependencies()
        { yield return texture; }
    }
}
