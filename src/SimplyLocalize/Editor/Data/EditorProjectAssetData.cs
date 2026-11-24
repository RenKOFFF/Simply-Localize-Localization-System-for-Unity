using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using UnityEditor;
using Object = UnityEngine.Object;

namespace SimplyLocalize.Editor.Data
{
    public interface ILocalizationEditorAssetReader
    {
        LocalizationEditorAssetSource Read(LocalizationSourceReference source);
    }
    /// <summary>Opt in to transactional editing of a native asset table at the resolved path.</summary>
    public interface ILocalizationEditorTableWriter : ILocalizationEditorAssetReader
    {
        string ResolveTablePath(LocalizationSourceReference source);
    }
    public sealed class LocalizationEditorAssetSource
    {
        public string AssetPath { get; }
        public IReadOnlyDictionary<string, Object> Values { get; }
        public LocalizationEditorAssetSource(IDictionary<string, Object> values, string assetPath = null)
        {
            Values = new ReadOnlyDictionary<string, Object>(new Dictionary<string, Object>(values ?? throw new ArgumentNullException(nameof(values)), StringComparer.Ordinal));
            AssetPath = assetPath;
        }
    }
    public sealed class LocalizationEditorAssetCell
    {
        public string Key { get; }
        public Object Asset { get; }
        public string Language { get; }
        public string AssetPath { get; }
        public LocalizationSourceReference Source { get; }
        internal LocalizationEditorAssetCell(string key, Object asset, string language, string path, LocalizationSourceReference source)
        { Key = key; Asset = asset; Language = language; AssetPath = path; Source = source; }
    }
    /// <summary>Editor-only preview of one channel/set. Copies entry mappings; Unity objects stay borrowed.
    /// Reading tables may load their direct dependencies in the Editor. This is not a runtime memory diagnostic.</summary>
    public sealed class EditorProjectAssetData
    {
        private readonly Dictionary<string, Dictionary<string, LocalizationEditorAssetCell>> cells = new(StringComparer.Ordinal);
        private readonly Dictionary<string, IReadOnlyList<LocalizationEditorAssetCell>> extras = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> fallbacks;
        private readonly string defaultLanguage, fallbackLanguage;
        public string ChannelId { get; }
        public string SetId { get; }
        public IReadOnlyList<string> Languages { get; }
        public IReadOnlyDictionary<string, string> Entries { get; }
        private EditorProjectAssetData(LocalizationChannelDefinition channel, LocalizationContentSet set)
        {
            ChannelId = channel.Id; SetId = set.Id; Languages = Array.AsReadOnly(channel.Languages.ToArray());
            Entries = new ReadOnlyDictionary<string, string>(set.Entries.Where(e => e.Kind == LocalizationEntryKind.Asset).ToDictionary(e => e.Id, e => e.Key, StringComparer.Ordinal));
            defaultLanguage = channel.DefaultLanguage; fallbackLanguage = channel.FallbackLanguage;
            fallbacks = channel.Fallbacks.ToDictionary(f => f.Language, f => f.Fallback, StringComparer.Ordinal);
        }
        public static EditorProjectAssetData Load(LocalizationProjectConfig config, string channelId = null, string setId = null, ILocalizationEditorAssetReader reader = null)
        {
            if (config == null) throw new ArgumentNullException(nameof(config)); config.ValidateOrThrow();
            var channel = config.Channels.SingleOrDefault(c => c.Id == (channelId ?? config.MainChannelId)) ?? throw new ArgumentException("Unknown channel identity.");
            setId ??= channel.DefaultSetId;
            if (!channel.SetIds.Contains(setId)) throw new ArgumentException("Set is not available in this channel.", nameof(setId));
            var set = config.Sets.Single(s => s.Id == setId); var result = new EditorProjectAssetData(channel, set);
            reader ??= new ResourcesEditorAssetReader(); var registered = new HashSet<string>(result.Entries.Values, StringComparer.Ordinal);
            foreach (string language in result.Languages)
            {
                var values = new Dictionary<string, LocalizationEditorAssetCell>(StringComparer.Ordinal);
                var unregistered = new List<LocalizationEditorAssetCell>();
                foreach (var source in set.Sources.FirstOrDefault(s => s.Language == language)?.Sources.Where(s => s.Kind == LocalizationSourceKind.AssetTable) ?? Enumerable.Empty<LocalizationSourceReference>())
                {
                    var address = new LocalizationSourceReference(source.ProviderId, source.Address, source.Kind);
                    LocalizationEditorAssetSource table;
                    try { table = reader.Read(address) ?? throw new InvalidOperationException("Reader returned no data."); }
                    catch (Exception failure) { throw new InvalidOperationException("Cannot read " + source.ProviderId + ":" + source.Address + " for " + language + ": " + failure.Message, failure); }
                    foreach (var pair in table.Values)
                    {
                        var cell = new LocalizationEditorAssetCell(pair.Key, pair.Value, language, table.AssetPath, address);
                        if (!registered.Contains(pair.Key)) { unregistered.Add(cell); continue; }
                        if (values.TryGetValue(pair.Key, out var previous)) throw new InvalidOperationException("Duplicate asset key '" + pair.Key + "' for " + language + ": " + previous.Source.Address + " and " + source.Address);
                        values.Add(pair.Key, cell);
                    }
                }
                result.cells.Add(language, values); result.extras.Add(language, unregistered.AsReadOnly());
            }
            return result;
        }
        public bool TryGetDirect(string entryId, string language, out LocalizationEditorAssetCell cell) => cells[Language(language)].TryGetValue(Key(entryId), out cell);
        public bool TryResolve(string entryId, string language, out LocalizationEditorAssetCell cell, Type expectedType = null)
        {
            expectedType ??= typeof(Object);
            if (!typeof(Object).IsAssignableFrom(expectedType)) throw new ArgumentException("Expected a Unity asset type.", nameof(expectedType));
            string key = Key(entryId); language = Language(language); var visited = new HashSet<string>(StringComparer.Ordinal);
            while (!string.IsNullOrEmpty(language) && visited.Add(language))
            {
                if (cells[language].TryGetValue(key, out cell) && cell.Asset != null && expectedType.IsInstanceOfType(cell.Asset)) return true;
                language = fallbacks.TryGetValue(language, out var next) ? next : language == fallbackLanguage ? null : fallbackLanguage;
            }
            cell = null; return false;
        }
        public IReadOnlyList<LocalizationEditorAssetCell> GetUnregistered(string language) => extras[Language(language)];
        private string Key(string id) => id != null && Entries.TryGetValue(id, out var key) ? key : throw new ArgumentException("Unknown asset entry identity in this set.", nameof(id));
        private string Language(string language)
        {
            string code = language == null ? defaultLanguage : LocalizationLanguageCode.Normalize(language);
            if (!cells.ContainsKey(code)) throw new ArgumentException("Language is not selected in this channel: " + code, nameof(language)); return code;
        }
    }
    public sealed class ResourcesEditorAssetReader : ILocalizationEditorAssetReader
    {
        private readonly ResourcesEditorTextReader index = new();
        public LocalizationEditorAssetSource Read(LocalizationSourceReference source)
        {
            if (source == null || source.Kind != LocalizationSourceKind.AssetTable) throw new ArgumentException("Expected an asset table source.", nameof(source));
            string path = index.ResolveSourceAssetPath(source); var table = AssetDatabase.LoadAssetAtPath<LocalizationAssetTable>(path);
            if (table == null) throw new InvalidOperationException("Cannot load asset table: " + path);
            return ReadTable(path, table);
        }
        internal static LocalizationEditorAssetSource ReadTable(string path, LocalizationAssetTable table)
        {
            var values = new Dictionary<string, Object>(StringComparer.Ordinal);
            foreach (var entry in table.Entries)
            {
                if (entry == null || string.IsNullOrEmpty(entry.key)) throw new InvalidOperationException("Empty asset table entry at " + path);
                if (values.ContainsKey(entry.key)) throw new InvalidOperationException("Duplicate asset key '" + entry.key + "' at " + path);
                values.Add(entry.key, entry.asset);
            }
            return new LocalizationEditorAssetSource(values, path);
        }
    }
}
