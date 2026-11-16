using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SimplyLocalize.Data;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SimplyLocalize
{
    /// <summary>Exact-address Resources loading. Tables bring their direct dependencies, even for one selected key.
    /// Cancellation waits for the native request to finish and cleans the late result before completing.</summary>
    public sealed class ResourcesLocalizationSourceProvider : ILocalizationSourceProvider
    {
        private readonly int thread = Environment.CurrentManagedThreadId;
        public LocalizationProviderCapabilities GetCapabilities(LocalizationSourceKind kind) => kind switch
        {
            LocalizationSourceKind.TextTable => new(LocalizationLoadUnit.Table, false, false),
            LocalizationSourceKind.AssetTable => new(LocalizationLoadUnit.Table, true, false),
            LocalizationSourceKind.Appearance => new(LocalizationLoadUnit.Asset, true, false),
            _ => throw new NotSupportedException("Unknown Resources source kind.")
        };
        public async Task<LocalizationSourceLease> LoadAsync(LocalizationSourceRequest request, CancellationToken cancellationToken)
        {
            if (thread != Environment.CurrentManagedThreadId || SynchronizationContext.Current == null)
                throw new InvalidOperationException("Resources loading requires the owning Unity thread and synchronization context.");
            if (request == null) throw new ArgumentNullException(nameof(request));
            GetCapabilities(request.Source.Kind);
            string address = request.Source.Address;
            if (!LocalizationConfig.IsResourcePathValid(address) || string.IsNullOrEmpty(address) || address.EndsWith("/", StringComparison.Ordinal))
                throw new ArgumentException("Expected an exact relative Resources address.");
            if (request.Ownership == LocalizationResourceOwnership.Owned && !Application.isPlaying)
                throw new InvalidOperationException("Owned Resources loading is runtime-only; use Borrowed in EditMode.");
            if (request.SessionId != LocalizationResourceLease.Generation) throw new OperationCanceledException("Source request belongs to a previous play session.");
            cancellationToken.ThrowIfCancellationRequested();
            var native = new LocalizationResourceLease(request.Ownership == LocalizationResourceOwnership.Owned,
                request.OwnerId, request.OwnerName, request.RequestedLanguage);
            try
            {
                native.BeginRequest(address, request.Language);
                Object root;
                try
                {
                    var operation = Resources.LoadAsync<Object>(address);
                    if (!operation.isDone)
                    {
                        var completion = new TaskCompletionSource<bool>();
                        Action<AsyncOperation> finished = _ => completion.TrySetResult(true);
                        operation.completed += finished;
                        try { await completion.Task; }
                        finally { operation.completed -= finished; }
                    }
                    root = operation.asset;
                    native.TrackLoaded(address, request.Language, root);
                }
                finally { native.EndRequest(address); }
                cancellationToken.ThrowIfCancellationRequested();
                if (root == null) throw new InvalidOperationException("Missing localization resource: Resources/" + address);
                var selection = request.RequestedKeys == null ? null : new HashSet<string>(request.RequestedKeys, StringComparer.Ordinal);
                LocalizationSourceData data;
                switch (request.Source.Kind)
                {
                    case LocalizationSourceKind.TextTable:
                        if (root is not TextAsset text) throw WrongType(address, "TextAsset");
                        string json = text.text;
                        if (json.Length > request.MaxSourceCharacters) throw Limit(address);
                        Dictionary<string, string> parsed;
                        try { parsed = LocalizationFileParser.Parse(json); }
                        catch (Exception error) when (error is FormatException || error is OverflowException || error is ArgumentOutOfRangeException)
                        { throw new FormatException("Invalid localization JSON at Resources/" + address + ": " + error.Message, error); }
                        if (parsed.Count > request.MaxTextEntries || parsed.Sum(p => (long)p.Key.Length + (p.Value?.Length ?? 0)) > request.MaxTextCharacters) throw Limit(address);
                        data = LocalizationSourceData.FromText(selection == null ? parsed : parsed.Where(p => selection.Contains(p.Key)).ToDictionary(p => p.Key, p => p.Value), parsed.Count);
                        break;
                    case LocalizationSourceKind.AssetTable:
                        if (root is not LocalizationAssetTable table) throw WrongType(address, "LocalizationAssetTable");
                        if (table.Count > request.MaxAssetEntries) throw Limit(address);
                        var assets = new Dictionary<string, Object>(StringComparer.Ordinal);
                        foreach (var entry in table.Entries)
                            if (selection == null || selection.Contains(entry.key)) assets[entry.key] = entry.asset;
                        data = LocalizationSourceData.FromAssets(assets, table.Count);
                        break;
                    case LocalizationSourceKind.Appearance:
                        if (root is not LocalizationAppearance appearance) throw WrongType(address, "LocalizationAppearance");
                        data = LocalizationSourceData.FromAppearance(appearance);
                        break;
                    default: throw new NotSupportedException("Unknown source kind.");
                }
                if (request.PrepareAudio)
                {
                    native.SetStage(LocalizationResourceStage.PreparingAudio);
                    await native.PrepareAudioAsync(request.AudioTimeoutSeconds, cancellationToken);
                }
                cancellationToken.ThrowIfCancellationRequested();
                native.SetStage(LocalizationResourceStage.Ready);
                return new LocalizationSourceLease(data, native.ReleaseAsync);
            }
            catch (Exception error)
            {
                LocalizationResourceDiagnostics.Record(native, error is OperationCanceledException ? LocalizationResourceEventKind.Cancelled : LocalizationResourceEventKind.Failed, detail: error.Message);
                try { await native.ReleaseAsync(); }
                catch (Exception cleanup) { throw new AggregateException("Resources load and cleanup failed.", error, cleanup); }
                throw;
            }
        }
        private static Exception WrongType(string address, string type) => new InvalidOperationException("Resources/" + address + " is not a " + type + ".");
        private static Exception Limit(string address) => new InvalidOperationException("Localization source exceeds configured limits: Resources/" + address);
    }
}
