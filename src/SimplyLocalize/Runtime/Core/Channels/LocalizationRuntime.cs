using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SimplyLocalize
{
    /// <summary>One player's localization state. Construct and use on the Unity thread.
    /// Main and FollowMain publish together; independent channels have independent operation gates.</summary>
    public sealed class LocalizationRuntime : IDisposable
    {
        private sealed class Group
        {
            internal readonly SemaphoreSlim Gate = new(1, 1);
            internal CancellationTokenSource Switch;
            internal string[] Channels;
        }
        private readonly LocalizationRuntimeDefinition definition;
        private readonly LocalizationSourceRegistry sources;
        private readonly LocalizationContextOptions options;
        private readonly Dictionary<string, Group> groups = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Dictionary<string, int>> active = new(StringComparer.Ordinal);
        private readonly Dictionary<string, LocalizationChannelLease> current = new(StringComparer.Ordinal);
        private readonly HashSet<Task> operations = new();
        private readonly CancellationTokenSource lifetime = new();
        private readonly int thread = Environment.CurrentManagedThreadId, session = LocalizationResourceLease.Generation;
        private readonly long ownerId = LocalizationResourceDiagnostics.NextId();
        private bool initializing, released;
        private Task release;
        public string MainChannelId => definition.MainId;
        public ReadOnlyCollection<string> ChannelIds { get; }
        public bool IsInitialized { get; private set; }
        public bool IsReleased => released;
        /// <summary>All participants are committed before any subscriber runs. Subscriber failures do not roll back state.</summary>
        public event Action<LocalizationRuntime, IReadOnlyList<string>> Changed;
        public event Action<Exception> ObserverError;

        public LocalizationRuntime(LocalizationProjectConfig config, LocalizationSourceRegistry providers = null, LocalizationContextOptions options = null)
        {
            definition = new LocalizationRuntimeDefinition(config);
            sources = (providers ?? new LocalizationSourceRegistry()).Copy();
            this.options = (options ?? new LocalizationContextOptions()).Copy();
            ChannelIds = Array.AsReadOnly(definition.Channels.Keys.ToArray());
            var main = new Group { Channels = definition.Channels.Values.Where(c => c.Id == definition.MainId || c.Mode == LocalizationChannelMode.FollowMain).Select(c => c.Id).ToArray() };
            foreach (var channel in definition.Channels.Values)
            {
                groups.Add(channel.Id, main.Channels.Contains(channel.Id) ? main : new Group { Channels = new[] { channel.Id } });
                active.Add(channel.Id, channel.StartupSetIds.ToDictionary(id => id, _ => 1, StringComparer.Ordinal));
            }
        }
        public LocalizationChannelSnapshot GetChannel(string channelId = null)
        { RequireReady(); return current[Channel(channelId).Id].Snapshot; }
        public bool TryGetText(LocalizationEntryReference reference, out LocalizationTextValue value)
        {
            RequireReady(); value = default;
            return definition.Index.Resolve(reference, out var entry) == LocalizationReferenceStatus.Found
                && entry.Kind == LocalizationEntryKind.Text && GetChannel(reference.ChannelId).TryGetText(entry.Key, out value, entry.SetId);
        }
        public bool TryGetAsset(LocalizationEntryReference reference, out LocalizationAssetValue value)
        {
            RequireReady(); value = default;
            return definition.Index.Resolve(reference, out var entry) == LocalizationReferenceStatus.Found
                && entry.Kind == LocalizationEntryKind.Asset && GetChannel(reference.ChannelId).TryGetAsset(entry.Key, out value, entry.SetId);
        }
        public Task InitializeAsync(string mainLanguage = null, CancellationToken cancellationToken = default)
        {
            EnsureUsable();
            if (IsInitialized) return Task.CompletedTask;
            if (initializing) throw new InvalidOperationException("Initialization is already running. Await its task.");
            string language = NormalizeSelection(definition.Channels[MainChannelId], mainLanguage);
            initializing = true;
            return RunTracked(async () =>
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, cancellationToken);
                var prepared = new Dictionary<string, LocalizationChannelLease>(StringComparer.Ordinal);
                Exception failure = null;
                try
                {
                    foreach (var channel in definition.Channels.Values)
                    {
                        string requested = groups[channel.Id] == groups[MainChannelId] ? language : channel.DefaultLanguage;
                        prepared.Add(channel.Id, new LocalizationChannelLease(await Prepare(channel, requested, active[channel.Id].Keys, linked.Token)));
                    }
                    Check(linked.Token);
                    foreach (var pair in prepared) current.Add(pair.Key, pair.Value);
                    prepared.Clear(); IsInitialized = true;
                    Notify(ChannelIds);
                }
                catch (Exception error) { failure = error; throw; }
                finally { try { await ReleaseOwners(prepared.Values, failure); } finally { initializing = false; } }
            });
        }
        public Task SwitchLanguageAsync(string language, CancellationToken cancellationToken = default) => SwitchLanguageAsync(MainChannelId, language, cancellationToken);
        public Task SwitchLanguageAsync(string channelId, string language, CancellationToken cancellationToken = default)
        {
            RequireReady(); var channel = Channel(channelId);
            if (channel.Mode == LocalizationChannelMode.FollowMain) throw new InvalidOperationException("Switch Main to change a FollowMain channel.");
            language = NormalizeSelection(channel, language);
            var group = groups[channel.Id];
            var linked = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, cancellationToken);
            var previous = group.Switch; group.Switch = linked; Cancel(previous);
            return RunTracked(async () =>
            {
                bool entered = false;
                var prepared = new Dictionary<string, LocalizationChannelLease>(StringComparer.Ordinal);
                Exception failure = null;
                try
                {
                    await group.Gate.WaitAsync(linked.Token); entered = true; Check(linked.Token);
                    foreach (string id in group.Channels)
                        prepared.Add(id, new LocalizationChannelLease(await Prepare(definition.Channels[id], language, active[id].Keys, linked.Token)));
                    Check(linked.Token);
                    var old = prepared.Keys.Select(id => current[id]).ToArray();
                    foreach (var pair in prepared) current[pair.Key] = pair.Value;
                    prepared.Clear(); Notify(Array.AsReadOnly(group.Channels));
                    await ReleaseAfterCommit(old);
                }
                catch (Exception error) { failure = error; throw; }
                finally
                {
                    try { await ReleaseOwners(prepared.Values, failure); }
                    finally
                    {
                        if (entered) group.Gate.Release();
                        if (ReferenceEquals(group.Switch, linked)) group.Switch = null;
                        linked.Dispose();
                    }
                }
            });
        }
        /// <summary>Reload source content using the frozen definitions. Recreate the runtime to adopt authoring changes.</summary>
        public Task ReloadAsync(string channelId = null, CancellationToken cancellationToken = default)
        {
            var channel = Channel(channelId);
            string leader = channel.Mode == LocalizationChannelMode.FollowMain ? MainChannelId : channel.Id;
            return SwitchLanguageAsync(leader, GetChannel(leader).RequestedLanguage, cancellationToken);
        }
        public Task<LocalizationSetLease> AcquireSetAsync(string channelId, string setId, CancellationToken cancellationToken = default)
        {
            RequireReady(); var channel = Channel(channelId);
            if (!channel.SetIds.Contains(setId)) throw new ArgumentException("Set is not available to this channel.", nameof(setId));
            LocalizationSetLease result = null;
            var task = RunTracked(async () =>
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, cancellationToken);
                var group = groups[channel.Id]; await group.Gate.WaitAsync(linked.Token);
                try
                {
                    Check(linked.Token);
                    if (active[channel.Id].TryGetValue(setId, out int count)) active[channel.Id][setId] = count + 1;
                    else
                    {
                        var before = current[channel.Id];
                        var candidate = await Extend(channel, before.Snapshot, setId, linked.Token);
                        try { Check(linked.Token); }
                        catch { await candidate.ReleaseOwner(); throw; }
                        active[channel.Id].Add(setId, 1); current[channel.Id] = new(candidate);
                        Notify(Array.AsReadOnly(new[] { channel.Id })); await ReleaseAfterCommit(new[] { before });
                    }
                    result = new LocalizationSetLease(() => ReleaseSet(channel.Id, setId));
                }
                finally { group.Gate.Release(); }
            });
            return Result();
            async Task<LocalizationSetLease> Result() { await task; return result; }
        }
        private Task ReleaseSet(string channelId, string setId)
        {
            EnsureThread();
            if (released || session != LocalizationResourceLease.Generation) return Task.CompletedTask;
            return RunTracked(async () =>
            {
                var group = groups[channelId];
                try { await group.Gate.WaitAsync(lifetime.Token); }
                catch (OperationCanceledException) when (released) { return; }
                try
                {
                    if (released) return;
                    if (--active[channelId][setId] > 0) return;
                    active[channelId].Remove(setId);
                    var before = current[channelId]; var state = before.Snapshot;
                    var retained = state.Sets.Where(p => p.Key != setId).ToDictionary(p => p.Key, p => p.Value.Retain(), StringComparer.Ordinal);
                    var appearances = retained.Count == 0 ? new Dictionary<string, LocalizationSourceLease>() : state.Appearances.ToDictionary(p => p.Key, p => p.Value.Retain(), StringComparer.Ordinal);
                    current[channelId] = new(new LocalizationChannelSnapshot(channelId, state.RequestedLanguage, state.ResolvedLanguage, state.DefaultSetId, retained, appearances));
                    Notify(Array.AsReadOnly(new[] { channelId })); await ReleaseOwners(new[] { before });
                }
                finally { group.Gate.Release(); }
            });
        }
        private async Task<LocalizationChannelSnapshot> Extend(LocalizationRuntimeDefinition.Channel channel, LocalizationChannelSnapshot before, string setId, CancellationToken token)
        {
            var sets = before.Sets.ToDictionary(p => p.Key, p => p.Value.Retain(), StringComparer.Ordinal);
            var appearances = before.Appearances.ToDictionary(p => p.Key, p => p.Value.Retain(), StringComparer.Ordinal);
            try
            {
                string[] chain = channel.Chain(before.RequestedLanguage);
                sets.Add(setId, await PrepareSet(channel, definition.Sets[setId], before.RequestedLanguage, chain, token));
                if (before.Sets.Count == 0) await PrepareAppearances(channel, before.RequestedLanguage, chain, appearances, token);
                return new(channel.Id, before.RequestedLanguage, chain[0], channel.DefaultSetId, sets, appearances);
            }
            catch (Exception error)
            {
                try { await LocalizationChannelSnapshot.ReleaseSources(sets.Values.SelectMany(s => s.Sources).Concat(appearances.Values)); }
                catch (Exception cleanup) { throw new AggregateException("Channel preparation and cleanup failed.", error, cleanup); }
                throw;
            }
        }
        private async Task<LocalizationChannelSnapshot> Prepare(LocalizationRuntimeDefinition.Channel channel, string requested, IEnumerable<string> setIds, CancellationToken token)
        {
            var sets = new Dictionary<string, LocalizationPreparedSet>(StringComparer.Ordinal);
            var appearances = new Dictionary<string, LocalizationSourceLease>(StringComparer.Ordinal);
            try
            {
                string[] chain = channel.Chain(requested);
                foreach (string id in setIds) sets.Add(id, await PrepareSet(channel, definition.Sets[id], requested, chain, token));
                if (sets.Count != 0) await PrepareAppearances(channel, requested, chain, appearances, token);
                return new(channel.Id, requested, chain[0], channel.DefaultSetId, sets, appearances);
            }
            catch (Exception error)
            {
                try { await LocalizationChannelSnapshot.ReleaseSources(sets.Values.SelectMany(s => s.Sources).Concat(appearances.Values)); }
                catch (Exception cleanup) { throw new AggregateException("Channel preparation and cleanup failed.", error, cleanup); }
                throw;
            }
        }
        private async Task<LocalizationPreparedSet> PrepareSet(LocalizationRuntimeDefinition.Channel channel, LocalizationRuntimeDefinition.Set set, string requested, string[] chain, CancellationToken token)
        {
            var result = new LocalizationPreparedSet();
            long textEntries = 0, assetEntries = 0, textCharacters = 0;
            try
            {
                foreach (string language in chain)
                {
                    if (!set.Sources.TryGetValue(language, out var descriptors)) continue;
                    var seen = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var source in descriptors)
                    {
                        var keys = set.Keys.Where(p => p.Value == (source.Kind == LocalizationSourceKind.TextTable ? LocalizationEntryKind.Text : LocalizationEntryKind.Asset)).Select(p => p.Key).ToArray();
                        if (keys.Length == 0) continue;
                        var lease = await sources.LoadAsync(Request(channel, source, language, requested, keys), token); result.Sources.Add(lease);
                        if (source.Kind == LocalizationSourceKind.TextTable) textEntries += lease.Data.SourceEntryCount;
                        else assetEntries += lease.Data.SourceEntryCount;
                        textCharacters += lease.Data.Text.Sum(p => (long)p.Key.Length + (p.Value?.Length ?? 0));
                        if (textEntries > options.MaxTextEntries || assetEntries > options.MaxAssetEntries || textCharacters > options.MaxTextCharacters)
                            throw new InvalidOperationException("Content set exceeds configured limits: " + set.Id);
                        foreach (var pair in lease.Data.Text.Where(p => set.Keys.TryGetValue(p.Key, out var kind) && kind == LocalizationEntryKind.Text))
                        {
                            if (!seen.Add(pair.Key)) throw new InvalidOperationException("Multiple sources define the same key in one set/language: " + pair.Key);
                            if (pair.Value != null && !result.Text.ContainsKey(pair.Key)) result.Text.Add(pair.Key, new(pair.Value, language, requested));
                        }
                        foreach (var pair in lease.Data.Assets.Where(p => set.Keys.TryGetValue(p.Key, out var kind) && kind == LocalizationEntryKind.Asset))
                        {
                            if (!seen.Add(pair.Key)) throw new InvalidOperationException("Multiple sources define the same key in one set/language: " + pair.Key);
                            if (pair.Value != null && !result.Assets.ContainsKey(pair.Key)) result.Assets.Add(pair.Key, new(pair.Value, language, requested, lease));
                        }
                    }
                }
                return result;
            }
            catch (Exception error)
            {
                try { await LocalizationChannelSnapshot.ReleaseSources(result.Sources); }
                catch (Exception cleanup) { throw new AggregateException("Set preparation and cleanup failed.", error, cleanup); }
                throw;
            }
        }
        private async Task PrepareAppearances(LocalizationRuntimeDefinition.Channel channel, string requested, string[] chain, Dictionary<string, LocalizationSourceLease> target, CancellationToken token)
        {
            foreach (string language in chain)
                if (definition.Appearances.TryGetValue(language, out var source)) target.Add(language, await sources.LoadAsync(Request(channel, source, language, requested, null), token));
        }
        private LocalizationSourceRequest Request(LocalizationRuntimeDefinition.Channel channel, LocalizationSourceReference source, string language, string requested, string[] keys) =>
            new(source, language, keys, options, ownerId, (options.DiagnosticName ?? "Localization runtime") + "/" + channel.Name, requested);
        private LocalizationRuntimeDefinition.Channel Channel(string id)
        { EnsureUsable(); return definition.Channels.TryGetValue(id ?? MainChannelId, out var channel) ? channel : throw new ArgumentException("Unknown channel identity.", nameof(id)); }
        private static string NormalizeSelection(LocalizationRuntimeDefinition.Channel channel, string language)
        {
            string code = LocalizationLanguageCode.Normalize(language ?? channel.DefaultLanguage);
            return channel.Languages.Contains(code) ? code : throw new ArgumentException("Language is not available to channel " + channel.Name + ": " + code, nameof(language));
        }
        private void Notify(IReadOnlyList<string> channels)
        {
            foreach (Action<LocalizationRuntime, IReadOnlyList<string>> subscriber in Changed?.GetInvocationList() ?? Array.Empty<Delegate>())
                try { subscriber(this, channels); } catch (Exception error) { Report(error); }
        }
        private void Report(Exception error)
        {
            if (ObserverError == null) { UnityEngine.Debug.LogException(error); return; }
            foreach (Action<Exception> observer in ObserverError.GetInvocationList())
                try { observer(error); } catch (Exception nested) { UnityEngine.Debug.LogException(nested); }
        }
        private async Task ReleaseAfterCommit(IEnumerable<LocalizationChannelLease> old)
        { try { await ReleaseOwners(old); } catch (Exception error) { Report(error); } }
        private static async Task ReleaseOwners(IEnumerable<LocalizationChannelLease> owners, Exception preparationError = null)
        {
            List<Exception> errors = null;
            foreach (var owner in owners) try { await owner.ReleaseAsync(); } catch (Exception error) { (errors ??= new()).Add(error); }
            if (errors != null)
            {
                if (preparationError != null) errors.Insert(0, preparationError);
                throw new AggregateException(errors);
            }
        }
        private Task RunTracked(Func<Task> action)
        {
            var completion = new TaskCompletionSource<bool>(); operations.Add(completion.Task);
            Run(); return completion.Task;
            async void Run()
            {
                try { await action(); operations.Remove(completion.Task); completion.TrySetResult(true); }
                catch (OperationCanceledException) { operations.Remove(completion.Task); completion.TrySetCanceled(); }
                catch (Exception error) { operations.Remove(completion.Task); completion.TrySetException(error); }
            }
        }
        public Task ReleaseAsync()
        {
            EnsureThread(); if (release != null) return release;
            var completion = new TaskCompletionSource<bool>(); release = completion.Task; released = true;
            Cancel(lifetime); Finish(); return release;
            async void Finish()
            {
                try
                {
                    try { await Task.WhenAll(operations.ToArray()); } catch { /* Each operation keeps its own failure. */ }
                    var owners = current.Values.ToArray(); current.Clear(); IsInitialized = false;
                    Changed = null;
                    await ReleaseOwners(owners); completion.TrySetResult(true);
                }
                catch (Exception error) { completion.TrySetException(error); }
                finally { lifetime.Dispose(); }
            }
        }
        public void Dispose() => LocalizationChannelLease.Observe(ReleaseAsync());
        private void EnsureThread()
        { if (thread != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Use the runtime on its owning Unity thread."); }
        private void EnsureUsable()
        {
            EnsureThread(); if (released) throw new ObjectDisposedException(nameof(LocalizationRuntime));
            if (session != LocalizationResourceLease.Generation) throw new OperationCanceledException("Runtime belongs to a previous play session.");
        }
        private void RequireReady() { EnsureUsable(); if (!IsInitialized) throw new InvalidOperationException("Await InitializeAsync before using channel state."); }
        private void Check(CancellationToken token) { token.ThrowIfCancellationRequested(); EnsureUsable(); }
        private void Cancel(CancellationTokenSource source)
        { if (source != null) try { source.Cancel(); } catch (Exception error) { Report(error); } }
    }

    /// <summary>One active-set registration. Startup registrations remain active until runtime release.</summary>
    public sealed class LocalizationSetLease : IDisposable
    {
        private readonly int thread = Environment.CurrentManagedThreadId;
        private Func<Task> cleanup;
        private Task release;
        internal LocalizationSetLease(Func<Task> cleanup) => this.cleanup = cleanup;
        public Task ReleaseAsync()
        {
            if (thread != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Release set owners on their owning Unity thread.");
            if (release != null) return release;
            var completion = new TaskCompletionSource<bool>(); release = completion.Task;
            Finish(); return release;
            async void Finish()
            {
                try { var action = cleanup; cleanup = null; await action(); completion.TrySetResult(true); }
                catch (Exception error) { completion.TrySetException(error); }
            }
        }
        public void Dispose() => LocalizationChannelLease.Observe(ReleaseAsync());
    }
}
