using UnityEngine;

namespace SimplyLocalize
{
    /// <summary>
    /// Automatically initializes localization before scene load.
    /// Requires LocalizationConfig to be placed in a Resources folder
    /// (the Settings tab in the editor handles this).
    ///
    /// Protection: if already initialized (e.g. by Bootstrap), does nothing.
    /// </summary>
    internal static class LocalizationAutoInitializer
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void AutoInitialize()
        {
            // Don't double-initialize
            if (Localization.IsInitialized)
                return;

            var configs = Resources.LoadAll<LocalizationConfig>("");

            if (configs == null || configs.Length == 0)
                return;

            var config = configs[0];

            if (!config.autoInitialize)
                return;

            Localization.Initialize(config);

            if (config.autoDetectLanguage)
                Localization.SetLanguageAuto();

            LocalizationLogger.Log("Auto-initialized from config in Resources");
        }
    }
}