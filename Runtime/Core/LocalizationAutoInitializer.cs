using UnityEngine;

namespace SimplyLocalize
{
    /// <summary>
    /// Automatically initializes the localization system before scene load
    /// when LocalizationConfig.autoInitialize is enabled.
    /// No MonoBehaviour or Bootstrap component needed.
    ///
    /// Loads the first LocalizationConfig found in Resources.
    /// If autoDetectLanguage is enabled, tries SystemLanguage matching first.
    /// </summary>
    internal static class LocalizationAutoInitializer
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void AutoInitialize()
        {
            // Find all configs in Resources
            var configs = Resources.LoadAll<LocalizationConfig>("");

            if (configs == null || configs.Length == 0)
                return;

            var config = configs[0];

            if (!config.autoInitialize)
                return;

            Localization.Initialize(config);

            if (config.autoDetectLanguage)
            {
                var detected = Localization.SetLanguageAuto();

                if (detected != null)
                {
                    LocalizationLogger.Log(
                        $"Auto-detected language: {detected.displayName} ({detected.Code})");
                }
            }
        }
    }
}
