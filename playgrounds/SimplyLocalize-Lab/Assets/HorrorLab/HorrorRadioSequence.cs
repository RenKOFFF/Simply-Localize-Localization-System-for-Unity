using UnityEngine;

namespace SimplyLocalize.HorrorLab
{
    [CreateAssetMenu(menuName = "SimplyLocalize/Samples/Radio Sequence")]
    public sealed class HorrorRadioSequence : ScriptableObject
    {
        public string sequenceId = "dispatch-intro";
        public Line[] lines;
        [System.Serializable]
        public sealed class Line
        {
            public string speakerKey;
            public string textKey;
            public string audioKey;
            [Min(1)] public float silentDuration = 5;
        }
    }
}

