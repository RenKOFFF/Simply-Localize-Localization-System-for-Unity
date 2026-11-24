using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace SimplyLocalize
{
    [DefaultExecutionOrder(-100)]
    [AddComponentMenu("SimplyLocalize/Localization Bootstrap")]
    public class LocalizationBootstrap : MonoBehaviour
    {
        [SerializeField] private LocalizationProjectConfig _config;
        [SerializeField] private bool _dontDestroyOnLoad = true;
        [SerializeField] private bool _autoDetectLanguage;
        [SerializeField] private string _overrideLanguage;
        private readonly CancellationTokenSource lifetime = new();
        private LocalizationRuntime owned;
        public Task Initialization { get; private set; } = Task.CompletedTask;
        public Task ReleaseCompletion { get; private set; } = Task.CompletedTask;
        private void Awake()
        {
            if (Localization.IsInitialized) return;
            if (_config == null)
            {
                Debug.LogError("[SimplyLocalize] Bootstrap requires a Project Configuration. Replace the old config explicitly.", this);
                return;
            }
            if (_dontDestroyOnLoad) DontDestroyOnLoad(gameObject);
            Initialization = Initialize();
            Localization.Observe(Initialization);
        }
        private async Task Initialize()
        {
            string language = string.IsNullOrEmpty(_overrideLanguage) ? null : _overrideLanguage;
            if (_autoDetectLanguage)
            {
                language = null;
                foreach (var candidate in _config.Languages)
                    if (candidate.SystemLanguage != SystemLanguage.Unknown && candidate.SystemLanguage == Application.systemLanguage
                        && System.Linq.Enumerable.Contains(_config.MainChannel.Languages, candidate.Code)) { language = candidate.Code; break; }
                language ??= _config.MainChannel.DefaultLanguage;
            }
            owned = await Localization.InitializeForBootstrapAsync(_config, language, lifetime.Token);
        }
        private void OnDestroy() { lifetime.Cancel(); ReleaseCompletion = Close(); Localization.Observe(ReleaseCompletion); }
        private async Task Close()
        {
            try { await Initialization; } catch (OperationCanceledException) { } catch { }
            try { await Localization.ShutdownIfOwnedAsync(owned); } finally { lifetime.Dispose(); owned = null; }
        }
    }
}
