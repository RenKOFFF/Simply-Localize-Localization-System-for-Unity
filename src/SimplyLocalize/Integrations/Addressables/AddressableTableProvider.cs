using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using Object = UnityEngine.Object;

[assembly: UnityEngine.Scripting.AlwaysLinkAssembly]

namespace SimplyLocalize.Integrations
{
    public sealed class AddressableTableProvider : ILocalizationSourceProvider
    {
        public const string Id = "addressables";

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#endif
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void Install()
        {
            LocalizationSourceRegistry.InstallOptionalProviders -= Register;
            LocalizationSourceRegistry.InstallOptionalProviders += Register;
        }

        private static void Register(LocalizationSourceRegistry registry) => registry.Register(Id, new AddressableTableProvider());

        public LocalizationProviderCapabilities GetCapabilities(LocalizationSourceKind kind)
        {
            if (kind != LocalizationSourceKind.AssetTable)
                throw new NotSupportedException("Addressables localization requires an asset table.");
            return new LocalizationProviderCapabilities(LocalizationLoadUnit.Table, true, false);
        }

        public async Task<LocalizationSourceLease> LoadAsync(LocalizationSourceRequest request, CancellationToken cancellationToken)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            GetCapabilities(request.Source.Kind);
            cancellationToken.ThrowIfCancellationRequested();
            var handle = Addressables.LoadAssetAsync<LocalizationAssetTable>(request.Source.Address);
            try
            {
                if (!handle.IsDone)
                {
                    var ready = new TaskCompletionSource<bool>();
                    handle.Completed += _ => ready.TrySetResult(true);
                    await ready.Task;
                }
                cancellationToken.ThrowIfCancellationRequested();
                if (handle.Status != AsyncOperationStatus.Succeeded || handle.Result == null)
                    throw new InvalidOperationException("Could not load Addressables table: " + request.Source.Address, handle.OperationException);
                var table = handle.Result;
                if (table.Count > request.MaxAssetEntries) throw new InvalidOperationException("Addressables table exceeds the configured entry limit.");
                var keys = request.RequestedKeys == null ? null : new HashSet<string>(request.RequestedKeys, StringComparer.Ordinal);
                var assets = new Dictionary<string, Object>(StringComparer.Ordinal);
                foreach (var entry in table.Entries)
                    if (keys == null || keys.Contains(entry.key)) assets.Add(entry.key, entry.asset);
                if (request.PrepareAudio)
                    await LocalizationResourceLease.PrepareAudioAsync(assets.Values.OfType<AudioClip>().Distinct(), request.SessionId, request.AudioTimeoutSeconds, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                return new LocalizationSourceLease(LocalizationSourceData.FromAssets(assets, table.Count), () =>
                {
                    if (handle.IsValid()) Addressables.Release(handle);
                    return Task.CompletedTask;
                });
            }
            catch
            {
                if (handle.IsValid()) Addressables.Release(handle);
                throw;
            }
        }
    }
}
