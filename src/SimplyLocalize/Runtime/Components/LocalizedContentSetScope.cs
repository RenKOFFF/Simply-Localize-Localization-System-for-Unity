using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace SimplyLocalize.Components
{
    /// <summary>Keeps one content set available while its chapter object is enabled.</summary>
    [AddComponentMenu("SimplyLocalize/Localized Content Set Scope")]
    public sealed class LocalizedContentSetScope : MonoBehaviour
    {
        [SerializeField, LocalizationId] private string _channelId;
        [SerializeField, LocalizationId] private string _setId;
        private LocalizationSetLease lease;
        private CancellationTokenSource pending;

        public string ChannelId => _channelId;
        public string SetId => _setId;

        private void OnEnable()
        {
            Localization.BindingChanged += Bind;
            Bind(Localization.Runtime);
        }

        private void OnDisable()
        {
            Localization.BindingChanged -= Bind;
            Bind(null);
        }

        private void Bind(LocalizationRuntime runtime)
        {
            pending?.Cancel();
            pending = null;
            var previous = lease;
            lease = null;
            if (previous != null) Localization.Observe(previous.ReleaseAsync());
            if (runtime == null || !runtime.IsInitialized || runtime.IsReleased || string.IsNullOrEmpty(_setId)) return;

            pending = new CancellationTokenSource();
            Localization.Observe(Acquire(runtime, pending));
        }

        private async Task Acquire(LocalizationRuntime runtime, CancellationTokenSource request)
        {
            try
            {
                var acquired = await runtime.AcquireSetAsync(_channelId, _setId, request.Token);
                if (request.IsCancellationRequested) await acquired.ReleaseAsync();
                else lease = acquired;
            }
            catch (OperationCanceledException) when (request.IsCancellationRequested || runtime.IsReleased) { }
            finally
            {
                if (ReferenceEquals(pending, request)) pending = null;
                request.Dispose();
            }
        }
    }
}
