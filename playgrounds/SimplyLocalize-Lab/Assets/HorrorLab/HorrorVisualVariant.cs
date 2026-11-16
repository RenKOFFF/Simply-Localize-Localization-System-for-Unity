using UnityEngine;

namespace SimplyLocalize.HorrorLab
{
    [CreateAssetMenu(menuName = "SimplyLocalize/Samples/World Visual Variant")]
    public sealed class HorrorVisualVariant : ScriptableObject
    {
        public Texture2D texture;
        public LanguageProfile contentLanguage;
        public bool containsText = true;
    }
}
