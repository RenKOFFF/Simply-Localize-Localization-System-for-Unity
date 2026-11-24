using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using TMPro;
using Object = UnityEngine.Object;

namespace SimplyLocalize
{
    // Unity-thread-only registry shared by live contexts. It never scans Resources or the scene.
    internal sealed class LocalizationResourceLease
    {
        private sealed class Entry
        {
            internal Object Asset;
            internal int Users;
            internal bool UnloadWhenUnused;
        }
        private sealed class Address
        {
            internal Object Root;
            internal readonly HashSet<LocalizationResourceLease> Pending = new();
        }
        private static readonly Dictionary<int, Entry> Shared = new();
        private static readonly Dictionary<int, HashSet<string>> RootAddresses = new();
        private static readonly Dictionary<string, Address> Addresses = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<int, Entry> entries = new();
        internal static int Generation { get; private set; }
        private readonly int generation = Generation;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSession()
        {
            Generation++; Shared.Clear(); Addresses.Clear(); RootAddresses.Clear(); LocalizationResourceDiagnostics.ResetSession();
        }
        private void EnsureSession()
        { if (generation != Generation) throw new OperationCanceledException("Localization context belongs to a previous play session."); }
        private readonly bool owned;
        private bool released;
        private Exception loadFailure;
        internal long ContextId { get; }
        internal long SetId { get; } = LocalizationResourceDiagnostics.NextId();
        internal string ContextName { get; }
        internal string Language { get; }
        internal bool IsCurrentSession => generation == Generation;
        internal LocalizationResourceOwnership Ownership => owned ? LocalizationResourceOwnership.Owned : LocalizationResourceOwnership.Borrowed;
        internal LocalizationResourceStage Stage { get; private set; } = LocalizationResourceStage.Loading;
        private readonly long created = System.Diagnostics.Stopwatch.GetTimestamp();
        private long ready, releaseStarted;
        private readonly Dictionary<string, string> pendingSources = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<int, (string address, string language)> origins = new();
        private KeyValuePair<int, Entry>[] closing;
        private static double Milliseconds(long start, long end) => start == 0 ? 0 : (end - start) * 1000d / System.Diagnostics.Stopwatch.Frequency;
        internal double PreparationMilliseconds => Milliseconds(created, ready != 0 ? ready : releaseStarted != 0 ? releaseStarted : System.Diagnostics.Stopwatch.GetTimestamp());
        internal double ReleaseMilliseconds => Milliseconds(releaseStarted, System.Diagnostics.Stopwatch.GetTimestamp());
        internal LocalizationResourceLease(bool owned, long contextId, string contextName, string language)
        {
            this.owned = owned; ContextId = contextId; ContextName = string.IsNullOrWhiteSpace(contextName) ? "Context " + contextId : contextName; Language = language;
            LocalizationResourceDiagnostics.Register(this);
            LocalizationResourceDiagnostics.Record(this, LocalizationResourceEventKind.Preparing);
        }
        internal void SetStage(LocalizationResourceStage stage)
        {
            Stage = stage;
            if (stage == LocalizationResourceStage.Ready) { ready = System.Diagnostics.Stopwatch.GetTimestamp(); LocalizationResourceDiagnostics.Record(this, LocalizationResourceEventKind.Ready); }
            if (stage == LocalizationResourceStage.Releasing && releaseStarted == 0) { releaseStarted = System.Diagnostics.Stopwatch.GetTimestamp(); LocalizationResourceDiagnostics.Record(this, LocalizationResourceEventKind.ReleaseRequested); }
        }
        internal IEnumerable<AudioClip> Audio => entries.Values.Select(e => e.Asset).OfType<AudioClip>();

        internal void BeginRequest(string address, string language)
        {
            EnsureSession();
            if (!Addresses.TryGetValue(address, out var source)) Addresses.Add(address, source = new Address());
            source.Pending.Add(this); pendingSources[address] = language;
            try { Track(source.Root, address, language); }
            catch { EndRequest(address); throw; }
        }
        internal void EndRequest(string address)
        {
            if (generation != Generation) return;
            pendingSources.Remove(address);
            if (Addresses.TryGetValue(address, out var source))
            {
                source.Pending.Remove(this);
                if (source.Pending.Count == 0 && ReferenceEquals(source.Root, null)) Addresses.Remove(address);
            }
        }
        internal void TrackLoaded(string address, string language, Object asset)
        {
            EnsureSession();
            if (!Addresses.TryGetValue(address, out var source)) Addresses.Add(address, source = new Address());
            if (!ReferenceEquals(source.Root, asset))
            {
                if (!ReferenceEquals(source.Root, null))
                {
                    int previous = source.Root.GetInstanceID();
                    var aliases = RootAddresses[previous];
                    aliases.Remove(address);
                    if (aliases.Count == 0) RootAddresses.Remove(previous);
                }
                source.Root = asset;
                if (!ReferenceEquals(asset, null))
                {
                    int id = asset.GetInstanceID();
                    if (!RootAddresses.TryGetValue(id, out var aliases))
                        RootAddresses.Add(id, aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                    aliases.Add(address);
                }
            }
            // Protect every in-flight owner before the first completed caller can close its context.
            foreach (var lease in source.Pending.ToArray())
                try { lease.Track(asset, address, lease.pendingSources.TryGetValue(address, out var code) ? code : language); } catch (Exception error) { lease.loadFailure ??= error; }
            Track(asset, address, language);
            if (loadFailure != null) throw loadFailure;
        }

        private static bool RequiresBorrowedOwnership(Object asset)
        {
#if SIMPLY_LOCALIZE_HAS_ASSETBUNDLE
            if (asset is AssetBundle) return true;
#endif
            return asset is GameObject || asset is Component;
        }

        internal void Track(Object asset, string address, string language)
        {
            EnsureSession();
            if (asset == null) return;
            if (released) throw new ObjectDisposedException(nameof(LocalizationResourceLease));
            int id = asset.GetInstanceID();
            if (entries.ContainsKey(id)) return;
            if (owned && RequiresBorrowedOwnership(asset))
                throw new NotSupportedException("Owned localization contexts cannot unload " + asset.GetType().Name + ". Use Borrowed ownership for prefabs/components/bundles.");
            if (!Shared.TryGetValue(id, out var entry)) Shared.Add(id, entry = new Entry { Asset = asset });
            entry.Users++; entry.UnloadWhenUnused |= owned; entries.Add(id, entry); origins[id] = (address, language);
            if (asset is LocalizationAssetTable table)
                foreach (var item in table.Entries) Track(item.asset, address, language);
            else if (asset is Sprite sprite) Track(sprite.texture, address, language);
            else if (asset is TMP_FontAsset font)
            {
                Track(font.material, address, language);
                foreach (var atlas in font.atlasTextures ?? Array.Empty<Texture2D>()) Track(atlas, address, language);
                Track(font.sourceFontFile, address, language);
                if (font.fallbackFontAssetTable != null)
                    foreach (var fallback in font.fallbackFontAssetTable) Track(fallback, address, language);
            }
            else if (asset is Font legacyFont) Track(legacyFont.material, address, language);
            if (asset is ILocalizationAssetDependencies dependencies)
                foreach (var dependency in dependencies.GetLocalizationDependencies() ?? Array.Empty<Object>()) Track(dependency, address, language);
        }

        internal async Task PrepareAudioAsync(double timeoutSeconds, CancellationToken token)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            foreach (var clip in Audio)
            {
                EnsureSession(); token.ThrowIfCancellationRequested();
                if (clip.loadState == AudioDataLoadState.Unloaded && !clip.LoadAudioData())
                    throw new InvalidOperationException("Cannot load audio data: " + clip.name);
                while (generation == Generation && clip != null && clip.loadState == AudioDataLoadState.Loading)
                {
                    token.ThrowIfCancellationRequested();
                    if (timer.Elapsed.TotalSeconds > timeoutSeconds) throw new TimeoutException("Timed out preparing localization audio.");
                    await Task.Yield();
                }
                EnsureSession(); token.ThrowIfCancellationRequested();
                if (clip == null || clip.loadState != AudioDataLoadState.Loaded)
                    throw new InvalidOperationException("Localization audio did not become ready: " + (clip != null ? clip.name : "destroyed clip"));
            }
        }

        internal async Task ReleaseAsync()
        {
            if (released) return;
            released = true;
            if (generation != Generation) { entries.Clear(); origins.Clear(); Stage = LocalizationResourceStage.Released; LocalizationResourceDiagnostics.Forget(this); return; }
            SetStage(LocalizationResourceStage.Releasing);
            var candidates = entries.ToArray(); closing = candidates; entries.Clear();
            // Drop all counts before releasing roots and then their explicitly tracked dependencies.
            foreach (var pair in candidates) pair.Value.Users--;
            foreach (var pair in candidates)
            {
                var entry = pair.Value;
                if (entry.Users != 0) { if (LocalizationResourceDiagnostics.RecordingEnabled) LocalizationResourceDiagnostics.Record(this, LocalizationResourceEventKind.Retained, pair.Key, entry.Asset != null ? entry.Asset.name : null, "Another live context retains this object."); continue; }
                // A canceled LoadAudioData may still be running. New contexts can retain the same entry meanwhile.
                while (generation == Generation && entry.Users == 0 && entry.Asset is AudioClip clip && clip != null && clip.loadState == AudioDataLoadState.Loading)
                { Stage = LocalizationResourceStage.WaitingForAudio; await Task.Yield(); }
                if (generation != Generation) { closing = null; origins.Clear(); Stage = LocalizationResourceStage.Released; LocalizationResourceDiagnostics.Forget(this); return; }
                if (entry.Users != 0) continue;
                string resourceName = LocalizationResourceDiagnostics.RecordingEnabled && entry.Asset != null ? entry.Asset.name : null;
                if (entry.UnloadWhenUnused && entry.Asset != null)
                {
                    Resources.UnloadAsset(entry.Asset);
                    LocalizationResourceDiagnostics.Record(this, LocalizationResourceEventKind.Unloaded, pair.Key, resourceName, "Resources.UnloadAsset completed; not a process RAM measurement.");
                }
                else if (!entry.UnloadWhenUnused) LocalizationResourceDiagnostics.Record(this, LocalizationResourceEventKind.BorrowedReleased, pair.Key, resourceName, "Reference released; native residency is not managed by this context.");
                if (Shared.TryGetValue(pair.Key, out var current) && ReferenceEquals(current, entry)) Shared.Remove(pair.Key);
                if (RootAddresses.TryGetValue(pair.Key, out var aliases))
                {
                    RootAddresses.Remove(pair.Key);
                    foreach (var address in aliases)
                    {
                        var source = Addresses[address];
                        source.Root = null;
                        if (source.Pending.Count == 0) Addresses.Remove(address);
                    }
                }
                entry.Asset = null;
            }
            closing = null; origins.Clear(); Stage = LocalizationResourceStage.Released;
            LocalizationResourceDiagnostics.Record(this, LocalizationResourceEventKind.Released);
            LocalizationResourceDiagnostics.Forget(this);
        }
        internal static LocalizationResourceSnapshot CaptureDiagnostics(IEnumerable<LocalizationResourceLease> leases, IEnumerable<LocalizationResourceEvent> history, int dropped)
        {
            var sets = new List<LocalizationResourceSetInfo>();
            var owners = new Dictionary<int, List<LocalizationResourceOwnerInfo>>();
            foreach (var lease in leases)
            {
                var retained = lease.closing ?? lease.entries.ToArray();
                sets.Add(new LocalizationResourceSetInfo(lease, retained.Count(p => p.Value.Asset != null), lease.pendingSources.Keys));
                foreach (var pair in retained)
                {
                    if (pair.Value.Asset == null) continue;
                    if (!owners.TryGetValue(pair.Key, out var list)) owners.Add(pair.Key, list = new List<LocalizationResourceOwnerInfo>());
                    lease.origins.TryGetValue(pair.Key, out var source);
                    list.Add(new LocalizationResourceOwnerInfo(lease, source.address, source.language));
                }
            }
            var resources = new List<LocalizationResourceInfo>();
            foreach (var pair in Shared)
            {
                var entry = pair.Value; if (entry.Asset == null) continue;
                owners.TryGetValue(pair.Key, out var list);
                resources.Add(new LocalizationResourceInfo(pair.Key, entry.Asset.name, entry.Asset.GetType().Name, entry.Users, entry.UnloadWhenUnused, list ?? new List<LocalizationResourceOwnerInfo>()));
            }
            return new LocalizationResourceSnapshot(sets, resources, history, dropped);
        }
    }
}
