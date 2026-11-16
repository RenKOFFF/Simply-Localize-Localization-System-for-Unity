using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SimplyLocalize
{
    public enum LocalizationChannelMode { Independent, FollowMain }
    public enum LocalizationEntryKind { Text, Asset }
    public enum LocalizationSourceKind { TextTable, AssetTable, Appearance }

    /// <summary>Shows a generated identity in Inspector without making it editable.</summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class LocalizationIdAttribute : PropertyAttribute { }

    internal static class LocalizationIdentity
    {
        internal static IReadOnlyList<T> Read<T>(List<T> values) => values == null ? Array.Empty<T>() : values.AsReadOnly();
        internal static string Create(string id = null)
        {
            if (id == null) return Guid.NewGuid().ToString("N");
            if (!IsValid(id)) throw new ArgumentException("Expected a canonical 32-character GUID identity.", nameof(id));
            return id;
        }
        internal static bool IsValid(string id) => Guid.TryParseExact(id, "N", out var value) && value != Guid.Empty && value.ToString("N") == id;
        internal static bool IsProviderId(string id) => !string.IsNullOrWhiteSpace(id)
            && id.All(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.')
            && (id == "resources" || !string.Equals(id, "resources", StringComparison.OrdinalIgnoreCase));
        internal static string Name(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl)) throw new ArgumentException("A non-empty name without control characters is required.");
            return value.Trim();
        }
        internal static string Key(string value)
        {
            string trimmed = Name(value);
            if (value != trimmed) throw new ArgumentException("Translation keys cannot start or end with whitespace.", nameof(value));
            return value;
        }
        internal static string OptionalLanguage(string value) => string.IsNullOrEmpty(value) ? "" : LocalizationLanguageCode.Normalize(value);
    }

    [Serializable]
    public sealed class LocalizationSourceReference
    {
        public const string ResourcesProvider = "resources";
        [SerializeField] private string _providerId;
        [SerializeField] private string _address;
        [SerializeField] private LocalizationSourceKind _kind;
        public string ProviderId => _providerId;
        public string Address => _address;
        public LocalizationSourceKind Kind => _kind;
        public LocalizationSourceReference(string providerId, string address, LocalizationSourceKind kind)
        { _providerId = providerId; _address = address; _kind = kind; }
    }

    [Serializable]
    public sealed class LocalizationLanguageDefinition
    {
        [SerializeField] private string _code;
        [SerializeField] private string _displayName;
        [SerializeField] private SystemLanguage _systemLanguage = SystemLanguage.Unknown;
        [SerializeReference] private LocalizationSourceReference _appearance;
        public string Code => _code;
        public string DisplayName { get => _displayName; set => _displayName = LocalizationIdentity.Name(value); }
        public SystemLanguage SystemLanguage { get => _systemLanguage; set => _systemLanguage = value; }
        public LocalizationSourceReference Appearance { get => _appearance; set => _appearance = value; }
        public LocalizationLanguageDefinition(string code, string displayName = null)
        { _code = LocalizationLanguageCode.Normalize(code); _displayName = LocalizationIdentity.Name(displayName ?? _code); }
    }

    [Serializable]
    public sealed class LocalizationFallbackDefinition
    {
        [SerializeField] private string _language;
        [SerializeField] private string _fallback;
        public string Language => _language;
        public string Fallback => _fallback;
        public LocalizationFallbackDefinition(string language, string fallback)
        { _language = LocalizationLanguageCode.Normalize(language); _fallback = LocalizationLanguageCode.Normalize(fallback); }
    }

    [Serializable]
    public sealed class LocalizationChannelDefinition
    {
        [SerializeField, LocalizationId] private string _id;
        [SerializeField] private string _name;
        [SerializeField] private LocalizationChannelMode _mode;
        [SerializeField] private List<string> _languages = new();
        [SerializeField] private string _defaultLanguage;
        [SerializeField] private string _fallbackLanguage;
        [SerializeField] private List<LocalizationFallbackDefinition> _fallbacks = new();
        [SerializeField] private List<string> _setIds = new();
        [SerializeField, LocalizationId] private string _defaultSetId;
        [SerializeField] private List<string> _startupSetIds = new();
        public string Id => _id;
        public string Name { get => _name; set => _name = LocalizationIdentity.Name(value); }
        public LocalizationChannelMode Mode { get => _mode; set => _mode = value; }
        public IReadOnlyList<string> Languages => LocalizationIdentity.Read(_languages);
        public string DefaultLanguage { get => _defaultLanguage; set => _defaultLanguage = LocalizationIdentity.OptionalLanguage(value); }
        public string FallbackLanguage { get => _fallbackLanguage; set => _fallbackLanguage = LocalizationIdentity.OptionalLanguage(value); }
        public IReadOnlyList<LocalizationFallbackDefinition> Fallbacks => LocalizationIdentity.Read(_fallbacks);
        public IReadOnlyList<string> SetIds => LocalizationIdentity.Read(_setIds);
        public string DefaultSetId => _defaultSetId;
        public IReadOnlyList<string> StartupSetIds => LocalizationIdentity.Read(_startupSetIds);
        public LocalizationChannelDefinition(string name, IEnumerable<string> languages, string id = null)
        {
            _id = LocalizationIdentity.Create(id); Name = name; SetLanguages(languages);
            _defaultLanguage = _fallbackLanguage = _languages.FirstOrDefault() ?? "";
        }
        public void SetLanguages(IEnumerable<string> languages)
        { _languages = (languages ?? throw new ArgumentNullException(nameof(languages))).Select(LocalizationLanguageCode.Normalize).Distinct(StringComparer.Ordinal).ToList(); }
        public void SetFallback(string language, string fallback)
        {
            string code = LocalizationLanguageCode.Normalize(language);
            var replacement = string.IsNullOrEmpty(fallback) ? null : new LocalizationFallbackDefinition(code, fallback);
            _fallbacks.RemoveAll(f => f.Language == code);
            if (replacement != null) _fallbacks.Add(replacement);
        }
        public void IncludeSet(string setId, bool atStartup = false)
        {
            if (!LocalizationIdentity.IsValid(setId)) throw new ArgumentException("Expected a content set identity.", nameof(setId));
            if (!_setIds.Contains(setId)) _setIds.Add(setId);
            if (string.IsNullOrEmpty(_defaultSetId)) _defaultSetId = setId;
            if (atStartup && !_startupSetIds.Contains(setId)) _startupSetIds.Add(setId);
        }
        public void RemoveSet(string setId)
        {
            _setIds.Remove(setId); _startupSetIds.Remove(setId);
            if (_defaultSetId == setId) _defaultSetId = "";
        }
        public void SetDefaultSet(string setId)
        {
            if (!_setIds.Contains(setId)) throw new ArgumentException("The default set must belong to this channel.", nameof(setId));
            _defaultSetId = setId;
        }
        public void SetStartup(string setId, bool enabled)
        {
            if (!_setIds.Contains(setId)) throw new ArgumentException("The set must belong to this channel.", nameof(setId));
            if (enabled && !_startupSetIds.Contains(setId)) _startupSetIds.Add(setId);
            if (!enabled) _startupSetIds.Remove(setId);
        }
    }

    [Serializable]
    public sealed class LocalizationEntryDefinition
    {
        [SerializeField, LocalizationId] private string _id;
        [SerializeField] private string _key;
        [SerializeField] private LocalizationEntryKind _kind;
        public string Id => _id;
        public string Key => _key;
        public LocalizationEntryKind Kind => _kind;
        public LocalizationEntryDefinition(string key, LocalizationEntryKind kind, string id = null)
        { _id = LocalizationIdentity.Create(id); _key = LocalizationIdentity.Key(key); _kind = kind; }
        internal void Rename(string key) => _key = LocalizationIdentity.Key(key);
    }

    [Serializable]
    public sealed class LocalizationSetSources
    {
        [SerializeField] private string _language;
        [SerializeField] private List<LocalizationSourceReference> _sources;
        public string Language => _language;
        public IReadOnlyList<LocalizationSourceReference> Sources => LocalizationIdentity.Read(_sources);
        public LocalizationSetSources(string language, IEnumerable<LocalizationSourceReference> sources)
        { _language = LocalizationLanguageCode.Normalize(language); _sources = sources?.ToList() ?? throw new ArgumentNullException(nameof(sources)); }
    }

    [Serializable]
    public sealed class LocalizationContentSet
    {
        [SerializeField, LocalizationId] private string _id;
        [SerializeField] private string _name;
        [SerializeField] private List<LocalizationEntryDefinition> _entries = new();
        [SerializeField] private List<LocalizationSetSources> _sources = new();
        public string Id => _id;
        public string Name { get => _name; set => _name = LocalizationIdentity.Name(value); }
        public IReadOnlyList<LocalizationEntryDefinition> Entries => LocalizationIdentity.Read(_entries);
        public IReadOnlyList<LocalizationSetSources> Sources => LocalizationIdentity.Read(_sources);
        public LocalizationContentSet(string name, string id = null) { _id = LocalizationIdentity.Create(id); Name = name; }
        public LocalizationEntryDefinition AddEntry(string key, LocalizationEntryKind kind = LocalizationEntryKind.Text, string id = null)
        {
            var entry = new LocalizationEntryDefinition(key, kind, id);
            if (_entries.Any(e => e.Key == entry.Key || e.Id == entry.Id)) throw new ArgumentException("Duplicate entry key or identity in this set.");
            _entries.Add(entry); return entry;
        }
        public void RenameEntry(string entryId, string key)
        {
            key = LocalizationIdentity.Key(key);
            var entry = _entries.SingleOrDefault(e => e.Id == entryId) ?? throw new ArgumentException("Unknown entry identity.", nameof(entryId));
            if (_entries.Any(e => e.Id != entryId && e.Key == key)) throw new ArgumentException("Duplicate key in this set.", nameof(key));
            entry.Rename(key);
        }
        public bool RemoveEntry(string entryId) => _entries.RemoveAll(e => e.Id == entryId) > 0;
        public void SetSources(string language, params LocalizationSourceReference[] sources)
        {
            var replacement = new LocalizationSetSources(language, sources);
            _sources.RemoveAll(s => s.Language == replacement.Language); _sources.Add(replacement);
        }
        internal void Accept(LocalizationEntryDefinition entry)
        {
            if (_entries.Any(e => e.Id == entry.Id || e.Key == entry.Key)) throw new ArgumentException("Target set already contains this identity or key.");
            _entries.Add(entry);
        }
    }
}
