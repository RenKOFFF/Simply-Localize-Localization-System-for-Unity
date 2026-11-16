using UnityEngine;

namespace SimplyLocalize.HorrorLab
{
    [CreateAssetMenu(menuName = "SimplyLocalize/Samples/World Content Definition")]
    public sealed class HorrorWorldDefinition : ScriptableObject
    {
        public string worldId = "precinct";
        public string scenePath = "Assets/HorrorLab/NightShift.unity";
        public LocalizationConfig voiceConfig, visualConfig;
        public HorrorRadioSequence[] dialogues;
        public string[] visualKeys;
    }
}
