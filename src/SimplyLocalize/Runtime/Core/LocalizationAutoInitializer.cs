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

            var config = Localization.FindResourceConfig(true);
            if (config == null) return;
            var profile = config.autoDetectLanguage
                ? config.GetProfileBySystemLanguage(Application.systemLanguage) ?? config.defaultLanguage
                : config.defaultLanguage;
            Localization.Initialize(config, profile != null ? profile.Code : null);

            LocalizationLogger.Log("Auto-initialized from config in Resources");
        }
    }
}
