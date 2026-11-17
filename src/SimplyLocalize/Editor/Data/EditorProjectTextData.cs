using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using SimplyLocalize.Data;
using UnityEditor;

namespace SimplyLocalize.Editor.Data
{
    /// <summary>Editor integration for an external text provider. Read must not change game/runtime state.</summary>
    public interface ILocalizationEditorTextReader
    {
        LocalizationEditorTextSource Read(LocalizationSourceReference source);
    }

    public sealed class LocalizationEditorTextSource
    {
        public string AssetPath { get; }
        public IReadOnlyDictionary<string, string> Values { get; }
        public LocalizationEditorTextSource(IDictionary<string, string> values, string assetPath = null)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            Values = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(values, StringComparer.Ordinal));
            AssetPath = assetPath;
        }
    }

    public sealed class LocalizationEditorTextCell
    {
        public string Key { get; }
        public string Value { get; }
        public string Language { get; }
        public string AssetPath { get; }
        public LocalizationSourceReference Source { get; }
        internal LocalizationEditorTextCell(string key, string value, string language,
            LocalizationSourceReference source, string assetPath)
        { Key = key; Value = value; Language = language; Source = source; AssetPath = assetPath; }
    }

    /// <summary>Immutable text authoring snapshot for one channel/set. Reads exact registered sources,
    /// including files edited outside Unity; never initializes or switches the game runtime.</summary>
    public sealed class EditorProjectTextData
    {
        private readonly Dictionary<string, Dictionary<string, LocalizationEditorTextCell>> cells = new(StringComparer.Ordinal);
        private readonly Dictionary<string, IReadOnlyList<LocalizationEditorTextCell>> unregistered = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> fallbacks;
        private readonly string defaultLanguage, fallbackLanguage;
        public string ChannelId { get; }
        public string SetId { get; }
        public IReadOnlyList<string> Languages { get; }
        /// <summary>Stable entry identity to key. Asset entries are deliberately excluded.</summary>
        public IReadOnlyDictionary<string, string> Entries { get; }

        private EditorProjectTextData(LocalizationChannelDefinition channel, LocalizationContentSet set)
        {
            ChannelId = channel.Id; SetId = set.Id;
            Languages = Array.AsReadOnly(channel.Languages.ToArray());
            defaultLanguage = channel.DefaultLanguage; fallbackLanguage = channel.FallbackLanguage;
            fallbacks = channel.Fallbacks.ToDictionary(f => f.Language, f => f.Fallback, StringComparer.Ordinal);
            Entries = new ReadOnlyDictionary<string, string>(set.Entries.Where(e => e.Kind == LocalizationEntryKind.Text)
                .ToDictionary(e => e.Id, e => e.Key, StringComparer.Ordinal));
        }

        /// <summary>A failed read returns no partial snapshot. Unknown providers need an explicit Editor reader;
        /// they are never silently treated as empty data. The reader remains caller-owned.</summary>
        public static EditorProjectTextData Load(LocalizationProjectConfig config, string channelId = null,
            string setId = null, ILocalizationEditorTextReader reader = null)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            config.ValidateOrThrow();
            var channel = config.Channels.SingleOrDefault(c => c.Id == (channelId ?? config.MainChannelId))
                ?? throw new ArgumentException("Unknown channel identity.", nameof(channelId));
            setId ??= channel.DefaultSetId;
            if (!channel.SetIds.Contains(setId)) throw new ArgumentException("Set is not available in this channel.", nameof(setId));
            var set = config.Sets.Single(s => s.Id == setId);
            var result = new EditorProjectTextData(channel, set);
            reader ??= new ResourcesEditorTextReader();
            var registered = new HashSet<string>(result.Entries.Values, StringComparer.Ordinal);
            foreach (string language in result.Languages)
            {
                var values = new Dictionary<string, LocalizationEditorTextCell>(StringComparer.Ordinal);
                var extras = new List<LocalizationEditorTextCell>();
                var sources = set.Sources.FirstOrDefault(s => s.Language == language);
                if (sources != null)
                    foreach (var source in sources.Sources.Where(s => s.Kind == LocalizationSourceKind.TextTable))
                    {
                        // Copy routing metadata so later config edits cannot retarget this snapshot.
                        var address = new LocalizationSourceReference(source.ProviderId, source.Address, source.Kind);
                        LocalizationEditorTextSource data;
                        try { data = reader.Read(address) ?? throw new InvalidOperationException("Reader returned no data."); }
                        catch (Exception error)
                        { throw new InvalidOperationException($"Cannot read {source.ProviderId}:{source.Address} for {language}: {error.Message}", error); }
                        foreach (var pair in data.Values)
                        {
                            var cell = new LocalizationEditorTextCell(pair.Key, pair.Value, language, address, data.AssetPath);
                            if (!registered.Contains(pair.Key)) { extras.Add(cell); continue; }
                            if (values.TryGetValue(pair.Key, out var previous))
                                throw new InvalidOperationException($"Duplicate key '{pair.Key}' in set '{set.Name}', language '{language}': {previous.Source.Address} and {source.Address}.");
                            values.Add(pair.Key, cell);
                        }
                    }
                result.cells.Add(language, values);
                result.unregistered.Add(language, extras.AsReadOnly());
            }
            return result;
        }

        /// <summary>Preserves empty/null original values for coverage and editing; no fallback is applied.</summary>
        public bool TryGetDirect(string entryId, string language, out LocalizationEditorTextCell cell)
        {
            string key = Key(entryId); language = Language(language);
            return cells[language].TryGetValue(key, out cell);
        }
        public bool TryResolve(string entryId, string language, out LocalizationEditorTextCell cell)
        {
            string key = Key(entryId); language = Language(language);
            var visited = new HashSet<string>(StringComparer.Ordinal);
            while (!string.IsNullOrEmpty(language) && visited.Add(language))
            {
                if (cells[language].TryGetValue(key, out cell) && !string.IsNullOrEmpty(cell.Value)) return true;
                language = fallbacks.TryGetValue(language, out var next) ? next : language == fallbackLanguage ? null : fallbackLanguage;
            }
            cell = null; return false;
        }
        /// <summary>File entries ignored by runtime until explicitly registered. Kept separate per source.</summary>
        public IReadOnlyList<LocalizationEditorTextCell> GetUnregistered(string language) => unregistered[Language(language)];
        private string Key(string entryId) => entryId != null && Entries.TryGetValue(entryId, out var key)
            ? key : throw new ArgumentException("Unknown text entry identity in this set.", nameof(entryId));
        private string Language(string language)
        {
            string code = language == null ? defaultLanguage : LocalizationLanguageCode.Normalize(language);
            if (!cells.ContainsKey(code)) throw new ArgumentException("Language is not selected in this channel: " + code, nameof(language));
            return code;
        }
    }

    /// <summary>Direct Editor reads, without Resources.Load or a game runtime. AssetDatabase indexes paths only;
    /// JSON contents come from disk so reloading observes external edits before Unity reimports them.</summary>
    public sealed class ResourcesEditorTextReader : ILocalizationEditorTextReader
    {
        private readonly Dictionary<string, string[]> paths;
        public ResourcesEditorTextReader()
        {
            paths = AssetDatabase.GetAllAssetPaths().Where(p => !AssetDatabase.IsValidFolder(p))
                .Select(p => new { Path = p, Address = ResourceAddress(p) }).Where(p => p.Address != null)
                .GroupBy(p => p.Address, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Select(p => p.Path).OrderBy(p => p, StringComparer.Ordinal).ToArray(), StringComparer.OrdinalIgnoreCase);
        }
        public LocalizationEditorTextSource Read(LocalizationSourceReference source)
        {
            string path = ResolveAssetPath(source);
            return new LocalizationEditorTextSource(LocalizationFileParser.Parse(File.ReadAllText(ResolveFilePath(path))), path);
        }
        /// <summary>Packages/... is an AssetDatabase address, not necessarily a physical project directory.</summary>
        public static string ResolveFilePath(string assetPath)
        {
            if (!assetPath.StartsWith("Packages/", StringComparison.Ordinal)) return assetPath;
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(assetPath);
            if (package == null || !assetPath.StartsWith(package.assetPath + "/", StringComparison.Ordinal))
                throw new IOException("Cannot resolve the package source: " + assetPath);
            return Path.Combine(package.resolvedPath, assetPath.Substring(package.assetPath.Length + 1));
        }
        public string ResolveAssetPath(LocalizationSourceReference source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (source.ProviderId != LocalizationSourceReference.ResourcesProvider)
                throw new NotSupportedException("Register an Editor text reader for provider '" + source.ProviderId + "'.");
            if (source.Kind != LocalizationSourceKind.TextTable || !LocalizationResourceAddress.IsValid(source.Address))
                throw new ArgumentException("Expected an exact Resources text table address.", nameof(source));
            if (!paths.TryGetValue(source.Address, out var matches)) throw new FileNotFoundException("Missing Resources source: " + source.Address);
            if (matches.Length != 1) throw new InvalidOperationException("Ambiguous Resources address '" + source.Address + "': " + string.Join(", ", matches));
            string path = matches[0];
            if (AssetDatabase.GetMainAssetTypeAtPath(path) != typeof(UnityEngine.TextAsset))
                throw new InvalidOperationException("Expected TextAsset at " + path);
            return path;
        }
        public static string ResourceAddress(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath) || assetPath.Split('/').Contains("Editor")) return null;
            int start = assetPath.LastIndexOf("/Resources/", StringComparison.Ordinal);
            return start < 0 ? null : Path.ChangeExtension(assetPath.Substring(start + 11), null);
        }
    }
}
