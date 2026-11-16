using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SimplyLocalize
{
    public enum LocalizationLoadUnit { ContentSet, Table, Asset }

    public readonly struct LocalizationProviderCapabilities
    {
        public LocalizationLoadUnit LoadUnit { get; }
        public bool LoadsDirectDependencies { get; }
        public bool CanCancelUnderlyingLoad { get; }
        public LocalizationProviderCapabilities(LocalizationLoadUnit loadUnit, bool loadsDirectDependencies, bool canCancelUnderlyingLoad)
        {
            if (!Enum.IsDefined(typeof(LocalizationLoadUnit), loadUnit)) throw new ArgumentOutOfRangeException(nameof(loadUnit));
            LoadUnit = loadUnit; LoadsDirectDependencies = loadsDirectDependencies; CanCancelUnderlyingLoad = canCancelUnderlyingLoad;
        }
    }

    /// <summary>Implement in the game project. Calls and releases run on the owning Unity thread.
    /// Each successful load returns one independently releasable owner. Retain a cached lease to share it.
    /// A failed load must clean its partial work; a successful late result is released by the registry.</summary>
    public interface ILocalizationSourceProvider
    {
        LocalizationProviderCapabilities GetCapabilities(LocalizationSourceKind kind);
        Task<LocalizationSourceLease> LoadAsync(LocalizationSourceRequest request, CancellationToken cancellationToken);
    }

    /// <summary>Optional: metadata must be read without loading localized Unity assets.</summary>
    public interface ILocalizationSourceMetadataProvider
    {
        Task<LocalizationSourceMetadata> ReadMetadataAsync(LocalizationSourceReference source, string language, CancellationToken cancellationToken);
    }

    public sealed class LocalizationSourceMetadata
    {
        public ReadOnlyCollection<string> Keys { get; }
        public LocalizationSourceMetadata(IEnumerable<string> keys)
        {
            var copy = (keys ?? throw new ArgumentNullException(nameof(keys))).ToArray();
            if (copy.Any(string.IsNullOrEmpty) || copy.Distinct(StringComparer.Ordinal).Count() != copy.Length)
                throw new ArgumentException("Metadata keys must be non-empty and unique.", nameof(keys));
            Keys = Array.AsReadOnly(copy);
        }
    }

    /// <summary>Frozen request. Null RequestedKeys means all entries; a selection does not change provider granularity.</summary>
    public sealed class LocalizationSourceRequest
    {
        public LocalizationSourceReference Source { get; }
        public string Language { get; }
        public string RequestedLanguage { get; }
        public ReadOnlyCollection<string> RequestedKeys { get; }
        public long OwnerId { get; }
        public string OwnerName { get; }
        public int SessionId { get; }
        public LocalizationResourceOwnership Ownership => Limits.Ownership;
        public bool PrepareAudio => Limits.PrepareAudio;
        public int MaxSourceCharacters => Limits.MaxSourceCharacters;
        public int MaxTextEntries => Limits.MaxTextEntries;
        public long MaxTextCharacters => Limits.MaxTextCharacters;
        public int MaxAssetEntries => Limits.MaxAssetEntries;
        public double AudioTimeoutSeconds => Limits.AudioTimeoutSeconds;
        internal LocalizationContextOptions Limits { get; }

        public LocalizationSourceRequest(LocalizationSourceReference source, string language,
            IEnumerable<string> requestedKeys = null, LocalizationContextOptions options = null,
            long ownerId = 0, string ownerName = null, string requestedLanguage = null)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (!LocalizationIdentity.IsProviderId(source.ProviderId) || string.IsNullOrWhiteSpace(source.Address)
                || source.Address != source.Address.Trim() || source.Address.Any(char.IsControl)
                || !Enum.IsDefined(typeof(LocalizationSourceKind), source.Kind)) throw new ArgumentException("Invalid source descriptor.", nameof(source));
            Source = new LocalizationSourceReference(source.ProviderId, source.Address, source.Kind);
            Language = LocalizationLanguageCode.Normalize(language);
            RequestedLanguage = LocalizationLanguageCode.Normalize(requestedLanguage ?? language);
            Limits = (options ?? new LocalizationContextOptions()).Copy();
            if (requestedKeys != null)
            {
                var keys = requestedKeys.ToArray();
                if (keys.Any(string.IsNullOrEmpty)) throw new ArgumentException("Requested keys must be non-empty.", nameof(requestedKeys));
                RequestedKeys = Array.AsReadOnly(keys.Distinct(StringComparer.Ordinal).ToArray());
            }
            if (ownerId < 0) throw new ArgumentOutOfRangeException(nameof(ownerId));
            OwnerId = ownerId == 0 ? LocalizationResourceDiagnostics.NextId() : ownerId;
            OwnerName = ownerName ?? Limits.DiagnosticName ?? "Localization source";
            SessionId = LocalizationResourceLease.Generation;
        }
    }
}
