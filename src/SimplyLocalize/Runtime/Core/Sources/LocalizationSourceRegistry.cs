using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SimplyLocalize
{
    /// <summary>Instance-local providers. No global registry, implicit scans or cross-provider cache keys.</summary>
    public sealed class LocalizationSourceRegistry
    {
        private readonly Dictionary<string, ILocalizationSourceProvider> providers = new(StringComparer.Ordinal);
        private readonly int thread = Environment.CurrentManagedThreadId;
        private readonly int session = LocalizationResourceLease.Generation;
        public LocalizationSourceRegistry(bool includeResources = true)
        { if (includeResources) providers.Add(LocalizationSourceReference.ResourcesProvider, new ResourcesLocalizationSourceProvider()); }
        public void Register(string id, ILocalizationSourceProvider provider)
        {
            EnsureUsable();
            if (!LocalizationIdentity.IsProviderId(id)) throw new ArgumentException("Invalid provider identifier.", nameof(id));
            providers[id] = provider ?? throw new ArgumentNullException(nameof(provider));
        }
        public LocalizationSourceRegistry Copy()
        {
            EnsureUsable(); var copy = new LocalizationSourceRegistry(false);
            foreach (var pair in providers) copy.providers.Add(pair.Key, pair.Value);
            return copy;
        }
        public LocalizationProviderCapabilities GetCapabilities(LocalizationSourceReference source) => Provider(source).GetCapabilities(source.Kind);
        public bool HasSeparateMetadata(LocalizationSourceReference source) => Provider(source) is ILocalizationSourceMetadataProvider;
        public async Task<LocalizationSourceMetadata> ReadMetadataAsync(LocalizationSourceReference source, string language, CancellationToken cancellationToken = default)
        {
            Provider(source).GetCapabilities(source.Kind);
            var provider = Provider(source) as ILocalizationSourceMetadataProvider
                ?? throw new NotSupportedException("This provider cannot read metadata separately from content: " + source.ProviderId);
            var frozen = new LocalizationSourceReference(source.ProviderId, source.Address, source.Kind);
            cancellationToken.ThrowIfCancellationRequested();
            var result = await provider.ReadMetadataAsync(frozen, LocalizationLanguageCode.Normalize(language), cancellationToken);
            EnsureUsable(); cancellationToken.ThrowIfCancellationRequested();
            return result ?? throw new InvalidOperationException("Metadata provider returned null.");
        }
        public async Task<LocalizationSourceLease> LoadAsync(LocalizationSourceRequest request, CancellationToken cancellationToken = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            var provider = Provider(request.Source); provider.GetCapabilities(request.Source.Kind);
            if (request.SessionId != session) throw new OperationCanceledException("Request belongs to another play session.");
            cancellationToken.ThrowIfCancellationRequested();
            var lease = await provider.LoadAsync(request, cancellationToken);
            if (lease == null) throw new InvalidOperationException("Source provider returned a null lease.");
            try
            {
                EnsureUsable(); cancellationToken.ThrowIfCancellationRequested();
                if (lease.Data.Kind != request.Source.Kind) throw new InvalidOperationException("Provider returned data of the wrong source kind.");
                if ((lease.Data.Kind == LocalizationSourceKind.TextTable && (lease.Data.SourceEntryCount > request.MaxTextEntries
                    || lease.Data.Text.Sum(p => (long)p.Key.Length + (p.Value?.Length ?? 0)) > request.MaxTextCharacters))
                    || (lease.Data.Kind == LocalizationSourceKind.AssetTable && lease.Data.SourceEntryCount > request.MaxAssetEntries))
                    throw new InvalidOperationException("Source result exceeds configured limits.");
                return lease;
            }
            catch (Exception loadError)
            {
                try { await lease.ReleaseAsync(); }
                catch (Exception cleanupError) { throw new AggregateException("Rejected source failed to release.", loadError, cleanupError); }
                throw;
            }
        }
        private ILocalizationSourceProvider Provider(LocalizationSourceReference source)
        {
            EnsureUsable(); if (source == null) throw new ArgumentNullException(nameof(source));
            if (!providers.TryGetValue(source.ProviderId ?? "", out var provider)) throw new InvalidOperationException("No localization provider registered for '" + source.ProviderId + "'.");
            return provider;
        }
        private void EnsureUsable()
        {
            if (thread != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Use the source registry on its owning Unity thread.");
            if (session != LocalizationResourceLease.Generation) throw new OperationCanceledException("Source registry belongs to a previous play session.");
        }
    }
}
