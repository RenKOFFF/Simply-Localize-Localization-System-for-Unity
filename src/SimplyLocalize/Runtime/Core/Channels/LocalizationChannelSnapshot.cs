using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Object = UnityEngine.Object;

namespace SimplyLocalize
{
    public readonly struct LocalizationTextValue
    {
        public string Text { get; }
        public string Language { get; }
        public string RequestedLanguage { get; }
        internal LocalizationTextValue(string text, string language, string requested) { Text = text; Language = language; RequestedLanguage = requested; }
    }
    public readonly struct LocalizationAssetValue
    {
        internal LocalizationSourceLease Owner { get; }
        public Object Asset { get; }
        public string Language { get; }
        public string RequestedLanguage { get; }
        internal LocalizationAssetValue(Object asset, string language, string requested, LocalizationSourceLease owner)
        { Asset = asset; Language = language; RequestedLanguage = requested; Owner = owner; }
        /// <summary>Retains this asset's source, not unrelated channel sets. Actual granularity is declared by the provider.</summary>
        public LocalizationSourceLease RetainSource() => Owner?.Retain() ?? throw new InvalidOperationException("There is no resolved asset source to retain.");
    }

    internal sealed class LocalizationPreparedSet
    {
        internal readonly Dictionary<string, LocalizationTextValue> Text = new(StringComparer.Ordinal);
        internal readonly Dictionary<string, List<LocalizationAssetValue>> Assets = new(StringComparer.Ordinal);
        internal readonly List<LocalizationSourceLease> Sources = new();
        internal LocalizationPreparedSet Retain()
        {
            var copy = new LocalizationPreparedSet();
            var owners = new Dictionary<LocalizationSourceLease, LocalizationSourceLease>();
            foreach (var source in Sources) { var retained = source.Retain(); copy.Sources.Add(retained); owners.Add(source, retained); }
            foreach (var pair in Text) copy.Text.Add(pair.Key, pair.Value);
            foreach (var pair in Assets) copy.Assets.Add(pair.Key, pair.Value.Select(value =>
                new LocalizationAssetValue(value.Asset, value.Language, value.RequestedLanguage, owners[value.Owner])).ToList());
            return copy;
        }
    }

    /// <summary>A prepared channel view. Reads never perform I/O. Retain it before keeping assets across a switch.</summary>
    public sealed class LocalizationChannelSnapshot
    {
        private readonly int thread = Environment.CurrentManagedThreadId, session = LocalizationResourceLease.Generation;
        private int owners = 1;
        internal readonly Dictionary<string, LocalizationPreparedSet> Sets;
        internal readonly Dictionary<string, LocalizationSourceLease> Appearances;
        public string ChannelId { get; }
        public string RequestedLanguage { get; }
        public string ResolvedLanguage { get; }
        public string DefaultSetId { get; }
        public ReadOnlyCollection<string> ActiveSetIds { get; }
        internal LocalizationChannelSnapshot(string channel, string requested, string resolved, string defaultSet,
            Dictionary<string, LocalizationPreparedSet> sets, Dictionary<string, LocalizationSourceLease> appearances)
        {
            ChannelId = channel; RequestedLanguage = requested; ResolvedLanguage = resolved; DefaultSetId = defaultSet;
            Sets = sets; Appearances = appearances; ActiveSetIds = Array.AsReadOnly(sets.Keys.ToArray());
        }
        public bool TryGetText(string key, out LocalizationTextValue value, string setId = null)
        {
            EnsureAlive(); value = default;
            string target = setId ?? DefaultSetId;
            return key != null && target != null && Sets.TryGetValue(target, out var set) && set.Text.TryGetValue(key, out value);
        }
        public bool TryGetAsset(string key, out LocalizationAssetValue value, string setId = null)
            => TryGetAsset<Object>(key, out value, setId);
        public bool TryGetAsset<T>(string key, out LocalizationAssetValue value, string setId = null) where T : Object
        {
            EnsureAlive(); value = default;
            string target = setId ?? DefaultSetId;
            if (key == null || target == null || !Sets.TryGetValue(target, out var set) || !set.Assets.TryGetValue(key, out var candidates)) return false;
            foreach (var candidate in candidates)
                if (candidate.Asset is T && candidate.Asset != null) { value = candidate; return true; }
            return false;
        }
        public LocalizationAppearance GetAppearance(string language = null)
        { EnsureAlive(); return Appearances.TryGetValue(language ?? ResolvedLanguage, out var lease) ? lease.Data.Appearance : null; }
        /// <summary>Retains only the loaded appearance source while a consumer applies its fonts. Null means no appearance was configured.</summary>
        public LocalizationSourceLease RetainAppearance(string language = null)
        { EnsureAlive(); return Appearances.TryGetValue(language ?? ResolvedLanguage, out var lease) ? lease.Retain() : null; }
        public LocalizationChannelLease Retain() { EnsureAlive(); owners++; return new LocalizationChannelLease(this); }
        internal void EnsureAlive()
        {
            if (thread != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Read channel state on its owning Unity thread.");
            if (owners == 0 || session != LocalizationResourceLease.Generation) throw new ObjectDisposedException(nameof(LocalizationChannelSnapshot));
        }
        internal Task ReleaseOwner()
        {
            if (thread != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Release channel state on its owning Unity thread.");
            if (--owners != 0) return Task.CompletedTask;
            var sources = Sets.Values.SelectMany(s => s.Sources).Concat(Appearances.Values).ToArray();
            Sets.Clear(); Appearances.Clear();
            return ReleaseSources(sources);
        }
        internal static async Task ReleaseSources(IEnumerable<LocalizationSourceLease> sources)
        {
            List<Exception> failures = null;
            foreach (var source in sources)
                try { await source.ReleaseAsync(); } catch (Exception error) { (failures ??= new()).Add(error); }
            if (failures != null) throw new AggregateException("Localization source cleanup failed.", failures);
        }
    }

    /// <summary>One retained channel owner, for example an audio line that finishes after the language changes.</summary>
    public sealed class LocalizationChannelLease : IDisposable
    {
        private readonly int thread = Environment.CurrentManagedThreadId;
        private LocalizationChannelSnapshot snapshot;
        private Task release;
        internal LocalizationChannelLease(LocalizationChannelSnapshot snapshot) => this.snapshot = snapshot;
        public LocalizationChannelSnapshot Snapshot
        { get { if (snapshot == null) throw new ObjectDisposedException(nameof(LocalizationChannelLease)); snapshot.EnsureAlive(); return snapshot; } }
        public Task ReleaseAsync()
        {
            if (thread != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Release channel owners on their owning Unity thread.");
            if (release != null) return release;
            var value = snapshot;
            if (value == null) return Task.CompletedTask;
            snapshot = null;
            // Publish the task before invoking provider cleanup, which can reenter this owner.
            var completion = new TaskCompletionSource<bool>(); release = completion.Task;
            Finish(value, completion); return release;
        }
        private static async void Finish(LocalizationChannelSnapshot value, TaskCompletionSource<bool> completion)
        { try { await value.ReleaseOwner(); completion.TrySetResult(true); } catch (Exception e) { completion.TrySetException(e); } }
        public void Dispose() => Observe(ReleaseAsync());
        internal static async void Observe(Task task) { try { await task; } catch (Exception e) { UnityEngine.Debug.LogException(e); } }
    }
}
