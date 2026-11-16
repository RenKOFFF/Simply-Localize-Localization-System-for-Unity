using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SimplyLocalize.Data;
using UnityEngine;

namespace SimplyLocalize
{
    /// <summary>
    /// A bounded, ready-only Resources context. Preparation, audio readiness and publication are atomic to readers.
    /// All methods run on the Unity thread. Dispose starts cleanup; await ReleaseAsync to wait for native release.
    /// Owned resources must not be used outside live contexts. Borrowed is the compatible default.
    /// </summary>
    public sealed class LocalizationContext : IDisposable
    {
        private sealed class State
        {
            internal LocalizationManager Manager;
            internal ScopedResourcesDataProvider Provider;
            internal LocalizationResourceLease Lease;
            internal int TextEntries, AssetEntries;
            internal long TextCharacters;
            internal async Task ReleaseAsync()
            {
                Provider.Dispose();
                await Provider.NativeLoadsFinished;
                Manager.ClearCaches();
                await Lease.ReleaseAsync();
            }
        }
        private readonly LocalizationConfig config;
        private readonly ResourcesLanguageSources[] sources;
        private readonly LocalizationContextOptions options;
        private readonly int thread = Environment.CurrentManagedThreadId;
        private readonly List<Task> operations = new();
        private CancellationTokenSource pending;
        private State active;
        private long revision;
        private readonly int generation = LocalizationResourceLease.Generation;
        private Task release;
        public bool IsDisposed { get; private set; }
        public string CurrentLanguage => active?.Manager.CurrentLanguage;
        public LanguageProfile CurrentProfile => active?.Manager.CurrentProfile;
        public int TextEntryCount => active?.TextEntries ?? 0;
        public long TextCharacterCount => active?.TextCharacters ?? 0;
        public int AssetEntryCount => active?.AssetEntries ?? 0;
        public int CachedTemplateCount => active?.Manager.CachedTemplateCount ?? 0;
        public event Action<string> OnLanguageChanged;
        public int ResourceRequestCount { get; private set; }
        private Exception cleanupFailure;

        public LocalizationContext(LocalizationConfig config, IEnumerable<ResourcesLanguageSources> sources,
            LocalizationContextOptions options = null)
        {
            this.config = config != null ? config : throw new ArgumentNullException(nameof(config));
            config.ValidateOrThrow();
            this.sources = sources?.ToArray() ?? throw new ArgumentNullException(nameof(sources));
            this.options = (options ?? new LocalizationContextOptions()).Copy();
            // Validate descriptors immediately without issuing any loads.
            using var validation = new ScopedResourcesDataProvider(this.sources);
            if (this.options.Ownership == LocalizationResourceOwnership.Owned && !Application.isPlaying)
                throw new InvalidOperationException("Owned Resources contexts are runtime-only. Use Borrowed in EditMode.");
        }

        public Task ChangeLanguageAsync(string languageCode, CancellationToken cancellationToken = default)
        {
            EnsureUsable(); cancellationToken.ThrowIfCancellationRequested();
            config.ValidateOrThrow();
            if (config.GetProfile(languageCode) == null) throw new ArgumentException("Language is not configured: " + languageCode, nameof(languageCode));
            pending?.Cancel();
            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            pending = cancellation;
            var prior = operations.LastOrDefault(t => !t.IsCompleted);
            var operation = ChangeAsync(languageCode, ++revision, cancellation, prior);
            foreach (var completed in operations.Where(t => t.IsFaulted)) _ = completed.Exception;
            operations.RemoveAll(t => t.IsCompleted);
            operations.Add(operation);
            return operation;
        }

        private async Task ChangeAsync(string code, long version, CancellationTokenSource cancellation, Task prior)
        {
            var token = cancellation.Token;
            State candidate = null;
            try
            {
                // Drain a canceled predecessor before starting another native load. At most one candidate per context.
                if (prior != null) try { await prior; } catch (Exception) { }
                token.ThrowIfCancellationRequested();
                var manager = new LocalizationManager(config) { TemplateCacheLimit = options.MaxTemplates };
                var lease = new LocalizationResourceLease(options.Ownership == LocalizationResourceOwnership.Owned);
                candidate = new State { Lease = lease, Provider = new ScopedResourcesDataProvider(sources, lease.TrackLoaded, address => { lease.BeginRequest(address); ResourceRequestCount++; }, lease.EndRequest) { MaxSourceCharacters = options.MaxSourceCharacters }, Manager = manager };
                candidate.Manager.SetDataProvider(candidate.Provider);
                using var prepared = await candidate.Manager.PrepareLanguageAsync(code, cancellationToken: token);
                candidate.TextEntries = prepared.Text.Values.Sum(t => t.Count);
                candidate.TextCharacters = prepared.Text.Values.SelectMany(t => t).Sum(p => (long)p.Key.Length + (p.Value?.Length ?? 0));
                candidate.AssetEntries = prepared.Assets.Values.SelectMany(t => t).Where(t => t != null).Sum(t => t.Count);
                if (candidate.TextEntries > options.MaxTextEntries || candidate.TextCharacters > options.MaxTextCharacters
                    || candidate.AssetEntries > options.MaxAssetEntries)
                    throw new InvalidOperationException("Prepared localization exceeds context limits. Split the selected sources or raise the explicit limits.");
                if (options.PrepareAudio) await lease.PrepareAudioAsync(options.AudioTimeoutSeconds, token);
                token.ThrowIfCancellationRequested();
                if (IsDisposed || version != revision || generation != LocalizationResourceLease.Generation) throw new OperationCanceledException(token);
                candidate.Manager.ApplyPreparedLanguage(prepared, token);
                var previous = active; active = candidate; candidate = null;
                // Publish only a ready state. Cleanup never invalidates resources retained by the new state.
                try { OnLanguageChanged?.Invoke(code); }
                finally { if (previous != null) await CleanupAsync(previous); }
            }
            finally
            {
                if (candidate != null) await CleanupAsync(candidate);
                if (ReferenceEquals(pending, cancellation)) pending = null;
                cancellation.Dispose();
            }
        }

        public LocalizationResolveResult<string> ResolveText(string key)
        { EnsureUsable(); return active == null ? new(LocalizationResolveStatus.NotPrepared, null, null) : active.Manager.ResolvePreparedText(key); }
        public LocalizationResolveResult<T> ResolveAsset<T>(string key) where T : UnityEngine.Object
        { EnsureUsable(); return active == null ? new(LocalizationResolveStatus.NotPrepared, null, null) : active.Manager.ResolvePreparedAsset<T>(key); }
        public string Get(string key, params object[] args)
        {
            EnsureUsable();
            if (ResolveText(key).Status == LocalizationResolveStatus.NotPrepared)
                throw new InvalidOperationException("Text is not prepared. Prepare the context again after config changes.");
            return active.Manager.Get(key, args);
        }

        public Task ReleaseAsync()
        {
            EnsureThread();
            if (release != null) return release;
            IsDisposed = true; revision++; pending?.Cancel(); OnLanguageChanged = null;
            var previous = active; active = null;
            release = ReleaseAllAsync(previous);
            return release;
        }
        private async Task ReleaseAllAsync(State previous)
        {
            // Each operation cleans its candidate in finally; observe all failures without rethrowing old load failures.
            foreach (var operation in operations.ToArray())
                try { await operation; } catch (Exception) { }
            operations.Clear();
            if (previous != null) await CleanupAsync(previous);
            if (cleanupFailure != null) throw new InvalidOperationException("Localization resource cleanup failed.", cleanupFailure);
        }
        private async Task CleanupAsync(State state)
        { try { await state.ReleaseAsync(); } catch (Exception error) { cleanupFailure = error; throw; } }

        public void Dispose()
        {
            var cleanup = ReleaseAsync();
            // Dispose cannot await. Observe a cleanup failure; callers needing completion use ReleaseAsync.
            if (!cleanup.IsCompletedSuccessfully) ObserveCleanup(cleanup);
        }
        private static async void ObserveCleanup(Task cleanup)
        { try { await cleanup; } catch (Exception error) { Debug.LogException(error); } }
        private void EnsureUsable()
        { EnsureThread(); if (IsDisposed || generation != LocalizationResourceLease.Generation) throw new ObjectDisposedException(nameof(LocalizationContext)); }
        private void EnsureThread()
        {
            if (Environment.CurrentManagedThreadId != thread)
                throw new InvalidOperationException("Use the localization context on its owning Unity thread.");
        }
    }
}
