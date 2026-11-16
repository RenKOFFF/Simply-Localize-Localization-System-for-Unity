using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace SimplyLocalize.Data
{
    /// <summary>
    /// Opt-in Resources provider that loads only explicitly listed JSON files and tables, in declared order.
    /// No folder scans or implicit sync fallback in async methods. Unlisted languages intentionally return empty data;
    /// a listed but missing/wrong-type asset throws. Direct dependencies of a selected table still load with it.
    /// Create, use, await and dispose on the Unity thread. JSON parsing remains on that thread.
    /// Dispose cancels waiting and prevents new reads; ResourceRequest itself cannot be aborted.
    /// All returned Unity assets are borrowed: this provider never unloads them, even on failure/cancellation.
    /// The owner must keep resources valid while consumers use them. Closing one provider cannot invalidate another.
    /// </summary>
    public sealed class ScopedResourcesDataProvider : IAsyncLocalizationDataProvider, IDisposable
    {
        private readonly Dictionary<string, ResourcesLanguageSources> _sources = new(StringComparer.OrdinalIgnoreCase);
        private readonly CancellationTokenSource _lifetime = new();
        internal int MaxSourceCharacters; // Zero keeps the standalone provider unrestricted.
        private readonly int _thread = Environment.CurrentManagedThreadId;
        private readonly Action<string, string, UnityEngine.Object> _loaded;
        private readonly Action<string, string> _requestStarted;
        private readonly Action<string> _requestFinished;
        private int _nativeLoads;
        private TaskCompletionSource<bool> _idle;
        internal Task NativeLoadsFinished => _nativeLoads == 0 ? Task.CompletedTask : _idle.Task;
        public bool IsDisposed { get; private set; }
        /// <summary>Outstanding waits, not outstanding native Unity loads after cancellation.</summary>
        public int PendingLoadCount { get; private set; }

        public ScopedResourcesDataProvider(IEnumerable<ResourcesLanguageSources> sources) : this(sources, null) { }

        internal ScopedResourcesDataProvider(IEnumerable<ResourcesLanguageSources> sources, Action<string, string, UnityEngine.Object> loaded, Action<string, string> requestStarted = null, Action<string> requestFinished = null)
        {
            _loaded = loaded; _requestStarted = requestStarted; _requestFinished = requestFinished;
            if (sources == null) throw new ArgumentNullException(nameof(sources));
            foreach (var source in sources)
            {
                if (source == null) throw new ArgumentException("A language source cannot be null.", nameof(sources));
                if (_sources.ContainsKey(source.LanguageCode))
                    throw new ArgumentException("Duplicate language source: " + source.LanguageCode, nameof(sources));
                _sources.Add(source.LanguageCode, source);
            }
        }

        private ResourcesLanguageSources Source(string code)
        {
            EnsureThread();
            if (IsDisposed) throw new ObjectDisposedException(nameof(ScopedResourcesDataProvider));
            if (!LocalizationConfig.IsLanguageCodeValid(code)) throw new ArgumentException("Invalid language code.", nameof(code));
            return _sources.TryGetValue(code, out var source) ? source : null;
        }

        public Dictionary<string, string> LoadTextData(string languageCode)
        {
            var source = Source(languageCode);
            var merged = new Dictionary<string, string>();
            if (source != null)
                foreach (string address in source.TextFiles)
                    Merge(merged, Required(Resources.Load<TextAsset>(address), address, languageCode), address, languageCode);
            return merged;
        }

        public List<LocalizationAssetTable> LoadAssetTables(string languageCode)
        {
            var source = Source(languageCode);
            var tables = new List<LocalizationAssetTable>();
            if (source != null)
                foreach (string address in source.AssetTables)
                    tables.Add(Required(Resources.Load<LocalizationAssetTable>(address), address, languageCode));
            return tables;
        }

        /// <summary>Checks listed addresses only; may load TextAsset objects, but does not parse JSON.</summary>
        public bool HasTextData(string languageCode)
        {
            var source = Source(languageCode);
            if (source != null)
                foreach (string address in source.TextFiles)
                    if (Resources.Load<TextAsset>(address) != null) return true;
            return false;
        }

        public async Task<Dictionary<string, string>> LoadTextDataAsync(string languageCode, CancellationToken cancellationToken)
        {
            var source = Source(languageCode);
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
            var token = cancellation.Token;
            token.ThrowIfCancellationRequested();
            var merged = new Dictionary<string, string>();
            if (source != null)
                foreach (string address in source.TextFiles)
                {
                    var asset = await LoadAsync<TextAsset>(address, languageCode, token);
                    Merge(merged, asset, address, languageCode);
                    token.ThrowIfCancellationRequested();
                }
            return merged;
        }

        public async Task<List<LocalizationAssetTable>> LoadAssetTablesAsync(string languageCode, CancellationToken cancellationToken)
        {
            var source = Source(languageCode);
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
            var token = cancellation.Token;
            token.ThrowIfCancellationRequested();
            var tables = new List<LocalizationAssetTable>();
            if (source != null)
                foreach (string address in source.AssetTables)
                    tables.Add(await LoadAsync<LocalizationAssetTable>(address, languageCode, token));
            token.ThrowIfCancellationRequested();
            return tables;
        }

        private async Task<T> LoadAsync<T>(string address, string languageCode, CancellationToken token) where T : UnityEngine.Object
        {
            EnsureThread(); token.ThrowIfCancellationRequested();
            if (SynchronizationContext.Current == null)
                throw new InvalidOperationException("Async Resources loading requires the Unity synchronization context.");
            _requestStarted?.Invoke(address, languageCode);
            ResourceRequest request;
            try { request = Resources.LoadAsync<T>(address); }
            catch { _requestFinished?.Invoke(address); throw; }
            var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (_nativeLoads++ == 0) _idle = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Action<AsyncOperation> onCompleted = null;
            onCompleted = _ =>
            {
                try
                {
                    // Even after cancellation, register late objects so an owning context can release them.
                    _loaded?.Invoke(address, languageCode, request.asset);
                    completed.TrySetResult(true);
                }
                catch (Exception error) { completed.TrySetException(error); }
                finally
                {
                    request.completed -= onCompleted;
                    _requestFinished?.Invoke(address);
                    if (--_nativeLoads == 0) _idle.TrySetResult(true);
                }
            };
            PendingLoadCount++;
            request.completed += onCompleted;
            try
            {
                using (token.Register(() => completed.TrySetCanceled()))
                {
                    await completed.Task;
                    EnsureThread(); token.ThrowIfCancellationRequested();
                    return Required(request.asset as T, address, languageCode);
                }
            }
            finally { PendingLoadCount--; }
        }

        private T Required<T>(T asset, string address, string languageCode) where T : UnityEngine.Object
        {
            if (asset == null)
                throw new InvalidOperationException("Missing or wrong-type localization resource: Resources/" + address + " (" + typeof(T).Name + ").");
            _loaded?.Invoke(address, languageCode, asset);
            return asset;
        }

        private void Merge(Dictionary<string, string> merged, TextAsset asset, string address, string code)
        {
            Dictionary<string, string> parsed;
            string json = asset.text;
            if (MaxSourceCharacters > 0 && json.Length > MaxSourceCharacters)
                throw new InvalidOperationException("Localization source exceeds context limits: Resources/" + address);
            try { parsed = LocalizationFileParser.Parse(json); }
            catch (Exception error) when (error is FormatException || error is OverflowException || error is ArgumentOutOfRangeException)
            { throw new FormatException("Invalid localization JSON at Resources/" + address + " (language '" + code + "'): " + error.Message, error); }
            foreach (var pair in parsed)
            {
                if (merged.ContainsKey(pair.Key))
                    LocalizationLogger.LogWarning("Duplicate key '" + pair.Key + "' for '" + code + "' at Resources/" + address + ". Last declared file wins.");
                merged[pair.Key] = pair.Value;
            }
        }

        public void Dispose()
        {
            EnsureThread();
            if (IsDisposed) return;
            IsDisposed = true;
            _sources.Clear();
            _lifetime.Cancel();
            _lifetime.Dispose();
        }

        private void EnsureThread()
        {
            if (Environment.CurrentManagedThreadId != _thread)
                throw new InvalidOperationException("Use the Resources provider on its owning Unity thread.");
        }
    }
}
