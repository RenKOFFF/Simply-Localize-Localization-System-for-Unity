using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace SimplyLocalize.Components
{
    public abstract class LocalizedComponentBase : MonoBehaviour
    {
        [SerializeField] protected string _key;
        [SerializeField] private string _channelId;
        [SerializeField] private string _setId;
        [SerializeField] private LocalizationEntryReference _entry;
        private bool explicitBinding;
        private LocalizationRuntime assignedRuntime, boundRuntime;
        private Task releaseCompletion = Task.CompletedTask;
        public string Key { get => _key; set { if (_key == value) return; _key = value; RefreshIfReady(); } }
        public string ChannelId { get => _channelId; set { _channelId = value; Rebind(); } }
        public string SetId { get => _setId; set { _setId = value; Rebind(); } }
        public LocalizationEntryReference Entry { get => _entry; set { _entry = value; Rebind(); } }
        public LocalizationRuntime BoundRuntime => boundRuntime;
        public Task ReleaseCompletion => releaseCompletion;
        protected bool IsReady => boundRuntime != null && boundRuntime.IsInitialized && !boundRuntime.IsReleased;
        protected string EffectiveChannelId => _entry.IsSet ? _entry.ChannelId : string.IsNullOrEmpty(_channelId) ? boundRuntime?.MainChannelId : _channelId;
        protected string EffectiveSetId => string.IsNullOrEmpty(_setId) ? null : _setId;
        protected LocalizationChannelSnapshot Channel => IsReady ? boundRuntime.GetChannel(EffectiveChannelId) : null;
        public void Bind(LocalizationRuntime instance, string channelId = null, string setId = null)
        {
            if (instance != null && instance.IsReleased) throw new ObjectDisposedException(nameof(instance));
            explicitBinding = true; assignedRuntime = instance; _channelId = channelId; _setId = setId; Rebind();
        }
        public void UseDefaultRuntime() { explicitBinding = false; assignedRuntime = null; Rebind(); }
        protected virtual void OnEnable() { Localization.BindingChanged += GlobalChanged; Rebind(); }
        protected virtual void OnDisable() { Localization.BindingChanged -= GlobalChanged; Attach(null); }
        protected virtual void OnDestroy() { Localization.BindingChanged -= GlobalChanged; Attach(null); }
        private void GlobalChanged(LocalizationRuntime value)
        {
            if (this == null) { Localization.BindingChanged -= GlobalChanged; return; }
            if (!explicitBinding && isActiveAndEnabled) Attach(value);
        }
        private void Rebind() { if (isActiveAndEnabled) Attach(explicitBinding ? assignedRuntime : Localization.Runtime); }
        private void Attach(LocalizationRuntime value)
        {
            if (boundRuntime != null) { boundRuntime.Changed -= Changed; boundRuntime.Releasing -= Closing; }
            ClearLocalizedValue(); boundRuntime = value != null && !value.IsReleased ? value : null;
            if (boundRuntime != null) { boundRuntime.Changed += Changed; boundRuntime.Releasing += Closing; }
            RefreshIfReady();
        }
        private void Closing(LocalizationRuntime sender) { if (ReferenceEquals(boundRuntime, sender)) Attach(null); }
        private void Changed(LocalizationRuntime sender, IReadOnlyList<string> channels)
        { if (ReferenceEquals(boundRuntime, sender) && channels.Contains(EffectiveChannelId)) RefreshIfReady(); }
        private void RefreshIfReady() { if (isActiveAndEnabled && IsReady) Refresh(); }
        protected bool TryResolveText(out LocalizationTextValue value)
        {
            value = default;
            return IsReady && (_entry.IsSet ? boundRuntime.TryGetText(_entry, out value) : Channel.TryGetText(_key, out value, EffectiveSetId));
        }
        protected bool TryResolveAsset<T>(out LocalizationAssetValue value) where T : UnityEngine.Object
        {
            value = default;
            return IsReady && (_entry.IsSet ? boundRuntime.TryGetAsset<T>(_entry, out value) : Channel.TryGetAsset<T>(_key, out value, EffectiveSetId));
        }
        protected void ReleaseOwned(LocalizationSourceLease owner)
        {
            if (owner == null) return;
            var task = owner.ReleaseAsync();
            releaseCompletion = releaseCompletion.IsCompletedSuccessfully ? task : Task.WhenAll(releaseCompletion, task);
            Localization.Observe(task);
        }
        protected virtual void ClearLocalizedValue() { }
        public abstract void Refresh();
#if UNITY_EDITOR
        protected virtual void OnValidate() { if (Application.isPlaying && isActiveAndEnabled) Rebind(); }
#endif
    }
}