using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
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
        private static readonly Dictionary<string, Address> Addresses = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<int, Entry> entries = new();
        internal static int Generation { get; private set; }
        private readonly int generation = Generation;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSession()
        {
            Generation++; Shared.Clear(); Addresses.Clear();
        }
        private void EnsureSession()
        { if (generation != Generation) throw new OperationCanceledException("Localization context belongs to a previous play session."); }
        private readonly bool owned;
        private bool released;
        private Exception loadFailure;
        internal LocalizationResourceLease(bool owned) { this.owned = owned; }
        internal IEnumerable<AudioClip> Audio => entries.Values.Select(e => e.Asset).OfType<AudioClip>();

        internal void BeginRequest(string address)
        {
            EnsureSession();
            if (!Addresses.TryGetValue(address, out var source)) Addresses.Add(address, source = new Address());
            source.Pending.Add(this);
            try { Track(source.Root); }
            catch { EndRequest(address); throw; }
        }
        internal void EndRequest(string address)
        {
            if (generation != Generation) return;
            if (Addresses.TryGetValue(address, out var source))
            {
                source.Pending.Remove(this);
                if (source.Pending.Count == 0 && ReferenceEquals(source.Root, null)) Addresses.Remove(address);
            }
        }
        internal void TrackLoaded(string address, Object asset)
        {
            EnsureSession();
            if (!Addresses.TryGetValue(address, out var source)) Addresses.Add(address, source = new Address());
            source.Root = asset;
            // Protect every in-flight owner before the first completed caller can close its context.
            foreach (var lease in source.Pending.ToArray())
                try { lease.Track(asset); } catch (Exception error) { lease.loadFailure ??= error; }
            Track(asset);
            if (loadFailure != null) throw loadFailure;
        }

        internal void Track(Object asset)
        {
            EnsureSession();
            if (asset == null) return;
            if (released) throw new ObjectDisposedException(nameof(LocalizationResourceLease));
            int id = asset.GetInstanceID();
            if (entries.ContainsKey(id)) return;
            if (owned && (asset is GameObject || asset is Component || asset is AssetBundle))
                throw new NotSupportedException("Owned localization contexts cannot unload " + asset.GetType().Name + ". Use Borrowed ownership for prefabs/components/bundles.");
            if (!Shared.TryGetValue(id, out var entry)) Shared.Add(id, entry = new Entry { Asset = asset });
            entry.Users++; entry.UnloadWhenUnused |= owned; entries.Add(id, entry);
            if (asset is LocalizationAssetTable table)
                foreach (var item in table.Entries) Track(item.asset);
            else if (asset is Sprite sprite) Track(sprite.texture);
            if (asset is ILocalizationAssetDependencies dependencies)
                foreach (var dependency in dependencies.GetLocalizationDependencies() ?? Array.Empty<Object>()) Track(dependency);
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
            if (generation != Generation) { entries.Clear(); return; }
            var candidates = entries.ToArray(); entries.Clear();
            // Drop all counts before releasing roots and then their explicitly tracked dependencies.
            foreach (var pair in candidates) pair.Value.Users--;
            foreach (var pair in candidates)
            {
                var entry = pair.Value;
                if (entry.Users != 0) continue;
                // A canceled LoadAudioData may still be running. New contexts can retain the same entry meanwhile.
                while (generation == Generation && entry.Users == 0 && entry.Asset is AudioClip clip && clip != null && clip.loadState == AudioDataLoadState.Loading)
                    await Task.Yield();
                if (generation != Generation) return;
                if (entry.Users != 0) continue;
                if (entry.UnloadWhenUnused && entry.Asset != null) Resources.UnloadAsset(entry.Asset);
                if (Shared.TryGetValue(pair.Key, out var current) && ReferenceEquals(current, entry)) Shared.Remove(pair.Key);
                foreach (var address in Addresses.Where(p => (object)p.Value.Root != null && p.Value.Root.GetInstanceID() == pair.Key).Select(p => p.Key).ToArray())
                {
                    var source = Addresses[address]; source.Root = null;
                    if (source.Pending.Count == 0) Addresses.Remove(address);
                }
                entry.Asset = null;
            }
        }
    }
}
