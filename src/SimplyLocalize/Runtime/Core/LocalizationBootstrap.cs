using UnityEngine;

namespace SimplyLocalize
{
    /// <summary>
    /// Initializes the localization system on Awake.
    /// Place this on a GameObject in your first scene (or use DontDestroyOnLoad).
    ///
    /// Alternatively, call Localization.Initialize(config) manually from your own code.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    [AddComponentMenu("SimplyLocalize/Localization Bootstrap")]
    public class LocalizationBootstrap : MonoBehaviour
    {
        [Tooltip("The localization config asset to initialize with")]
        [SerializeField] private LocalizationConfig _config;

        [Tooltip("Persist this object across scene loads")]
        [SerializeField] private bool _dontDestroyOnLoad = true;

        [Header("Startup Language")]
        [Tooltip("Auto-detect language from device settings (uses SystemLanguage matching)")]
        [SerializeField] private bool _autoDetectLanguage;

        [Tooltip("Override the default language from config (ignored if auto-detect is on). Drag a LanguageProfile here.")]
        [SerializeField] private LanguageProfile _overrideLanguage;

        private void Awake()
        {
            if (_dontDestroyOnLoad)
                DontDestroyOnLoad(gameObject);

            if (_config == null)
            {
                Debug.LogError(
                    "[SimplyLocalize] LocalizationBootstrap: No config assigned!", this);
                return;
            }

            Localization.Initialize(_config);

            if (_autoDetectLanguage)
                Localization.SetLanguageAuto();
            else if (_overrideLanguage != null)
                Localization.SetLanguage(_overrideLanguage);
        }

        private void OnDestroy()
        {
            Localization.Shutdown();
        }
    }
}