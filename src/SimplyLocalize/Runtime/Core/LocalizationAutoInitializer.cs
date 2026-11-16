using UnityEngine;

namespace SimplyLocalize
{
    internal static class LocalizationAutoInitializer
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void AutoInitialize() => Localization.Observe(Localization.InitializeAutomaticallyAsync());
    }
}