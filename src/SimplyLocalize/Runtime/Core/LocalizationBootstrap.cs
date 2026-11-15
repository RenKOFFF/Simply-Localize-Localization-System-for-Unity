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

        private LocalizationManager _ownedManager;

        private void Awake()
        {
            if (_config == null)
            {
                Debug.LogError(
                    "[SimplyLocalize] LocalizationBootstrap: No config assigned!", this);
                return;
            }

            // An existing manual/automatic/scene owner wins. A duplicate must neither
            // reset the chosen language nor acquire shutdown rights over that manager.
            if (Localization.IsInitialized)
            {
                if (Localization.Config != _config)
                    Debug.LogWarning($"[SimplyLocalize] Bootstrap '{name}' config '{_config.name}' ignored: active config is '{Localization.Config.name}'. Use Localization.Initialize explicitly to replace it.", this);
                return;
            }
            var profile = _autoDetectLanguage
                ? _config.GetProfileBySystemLanguage(Application.systemLanguage) ?? _config.defaultLanguage
                : _overrideLanguage != null ? _overrideLanguage : _config.defaultLanguage;
            _ownedManager = Localization.InitializeForBootstrap(_config, profile);
            if (_dontDestroyOnLoad)
                DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy()
        {
            if (_ownedManager != null && ReferenceEquals(Localization.Manager, _ownedManager))
                Localization.Shutdown();
            _ownedManager = null;
        }
    }
}
