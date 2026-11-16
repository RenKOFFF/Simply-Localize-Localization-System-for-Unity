using System;
using System.Collections.Generic;
using UnityEngine;

namespace SimplyLocalize
{
    [Serializable]
    public struct LocalizationEntryReference
    {
        [SerializeField, LocalizationId] private string _channelId;
        [SerializeField, LocalizationId] private string _entryId;
        public string ChannelId => _channelId;
        public string EntryId => _entryId;
        public bool IsSet => LocalizationIdentity.IsValid(_channelId) && LocalizationIdentity.IsValid(_entryId);
        public LocalizationEntryReference(string channelId, string entryId)
        {
            if (!LocalizationIdentity.IsValid(channelId) || !LocalizationIdentity.IsValid(entryId))
                throw new ArgumentException("A reference requires existing canonical channel and entry identities.");
            _channelId = channelId; _entryId = entryId;
        }
    }

    public enum LocalizationReferenceStatus { Found, MissingChannel, MissingEntry, SetNotAvailable }

    public readonly struct LocalizationEntryLocation
    {
        public string EntryId { get; }
        public string SetId { get; }
        public string Key { get; }
        public LocalizationEntryKind Kind { get; }
        internal LocalizationEntryLocation(string entryId, string setId, string key, LocalizationEntryKind kind)
        { EntryId = entryId; SetId = setId; Key = key; Kind = kind; }
    }

    /// <summary>Immutable routing metadata. Keeps neither config nor Unity resource references alive.</summary>
    public sealed class LocalizationEntryIndex
    {
        private readonly Dictionary<string, LocalizationEntryLocation> entries = new(StringComparer.Ordinal);
        private readonly Dictionary<string, HashSet<string>> channels = new(StringComparer.Ordinal);
        internal LocalizationEntryIndex(LocalizationProjectConfig config)
        {
            foreach (var set in config.Sets)
                foreach (var entry in set.Entries) entries.Add(entry.Id, new(entry.Id, set.Id, entry.Key, entry.Kind));
            foreach (var channel in config.Channels) channels.Add(channel.Id, new(channel.SetIds, StringComparer.Ordinal));
        }
        public LocalizationReferenceStatus Resolve(LocalizationEntryReference reference, out LocalizationEntryLocation location)
        {
            location = default;
            if (reference.ChannelId == null || !channels.TryGetValue(reference.ChannelId, out var sets)) return LocalizationReferenceStatus.MissingChannel;
            if (reference.EntryId == null || !entries.TryGetValue(reference.EntryId, out var found)) return LocalizationReferenceStatus.MissingEntry;
            if (!sets.Contains(found.SetId)) return LocalizationReferenceStatus.SetNotAvailable;
            location = found; return LocalizationReferenceStatus.Found;
        }
    }
}
