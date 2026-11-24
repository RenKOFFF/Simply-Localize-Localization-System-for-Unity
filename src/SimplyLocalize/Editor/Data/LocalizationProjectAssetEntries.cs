using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SimplyLocalize.Editor.Data
{
    public sealed partial class LocalizationProjectAssetEdit
    {
        private readonly Dictionary<string, string> entryRoutes = new(StringComparer.Ordinal);
        private readonly Dictionary<string, (LocalizationSourceReference source, string path)> sourcePaths = new(StringComparer.Ordinal);
        private ResourcesEditorTextReader sourceIndex;

        /// <summary>Opens a writable native source and revalidates its address before commit.</summary>
        public T EditSource<T>(LocalizationSourceReference source) where T : ScriptableObject
        {
            EnsureOpen(); if (source == null) throw new ArgumentNullException(nameof(source));
            bool resources = source.ProviderId == LocalizationSourceReference.ResourcesProvider;
            Type expected = source.Kind == LocalizationSourceKind.AssetTable ? typeof(LocalizationAssetTable)
                : source.Kind == LocalizationSourceKind.Appearance ? typeof(LocalizationAppearance)
                : throw new ArgumentException("Expected an asset table or appearance source.", nameof(source));
            var pending = resources ? assets.Values.SingleOrDefault(a => a.Bytes == null && string.Equals(ResourcesEditorTextReader.ResourceAddress(a.Path), source.Address, StringComparison.OrdinalIgnoreCase)) : null;
            ScriptableObject draft; string path;
            if (pending != null) { draft = pending.Draft; path = pending.Path; }
            else
            {
                path = resources ? (sourceIndex ??= new ResourcesEditorTextReader()).ResolveSourceAssetPath(source) : LocalizationEditorReaders.ResolveWritableTable(source);
                draft = Edit(AssetDatabase.LoadAssetAtPath<ScriptableObject>(path));
            }
            if (!expected.IsInstanceOfType(draft) || !(draft is T typed)) throw new ArgumentException("Source asset type does not match the requested type.", nameof(source));
            sourcePaths[source.ProviderId + ":" + source.Kind + ":" + source.Address] = (source, path); return typed;
        }
        public void SetAsset(string entryId, string language, Object value, string destinationAddress = null)
        {
            var (set, entry) = AssetEntry(entryId); var tables = Tables(set, language); var present = Locate(tables, entry.Key);
            Destination(tables, entry.Key, destinationAddress, present).Set(entry.Key, value);
        }
        public void RemoveAsset(string entryId, string language)
        { var (set, entry) = AssetEntry(entryId); Locate(Tables(set, language), entry.Key)?.Remove(entry.Key); }
        public void RenameAssetEntry(string entryId, string key)
        {
            var (set, entry) = AssetEntry(entryId); string before = entry.Key;
            _ = new LocalizationEntryDefinition(key, LocalizationEntryKind.Asset);
            if (key == before) return;
            if (set.Entries.Any(e => e.Key == key)) throw new ArgumentException("Target key already exists in this set.");
            var locations = Locations(set, before);
            if (locations.Any(t => t.HasKey(key))) throw new InvalidOperationException("Target key already exists in a source. Nothing was overwritten.");
            set.RenameEntry(entryId, key);
            foreach (var table in locations)
            {
                table.Set(key, table.Get(before));
                if (!OtherEntryUses(table, before, entryId)) table.Remove(before);
            }
        }
        public void RemoveAssetEntry(string entryId)
        {
            var (set, entry) = AssetEntry(entryId); var locations = Locations(set, entry.Key); set.RemoveEntry(entryId);
            foreach (var table in locations) if (!OtherEntryUses(table, entry.Key, entryId)) table.Remove(entry.Key);
        }
        public void MoveAssetEntry(string entryId, string targetSetId, IReadOnlyDictionary<string, string> destinationAddresses = null)
        {
            var (set, entry) = AssetEntry(entryId);
            var target = Draft.Sets.SingleOrDefault(s => s.Id == targetSetId) ?? throw new ArgumentException("Unknown target set.", nameof(targetSetId));
            if (set == target) return;
            TrackRoutes(target);
            if (target.Entries.Any(e => e.Key == entry.Key || e.Id == entry.Id)) throw new InvalidOperationException("Target set already contains this key or identity.");
            var moves = new List<(LocalizationAssetTable from, LocalizationAssetTable to, Object asset)>();
            foreach (string language in set.Sources.Select(s => s.Language))
            {
                var from = Locate(Tables(set, language), entry.Key); if (from == null) continue;
                var targets = Tables(target, language); var present = Locate(targets, entry.Key);
                string selected = destinationAddresses != null && destinationAddresses.TryGetValue(language, out var address) ? address : null;
                var to = Destination(targets, entry.Key, selected, present);
                if (from == to) continue;
                if (present != null) throw new InvalidOperationException("Target source already contains '" + entry.Key + "' for " + language + ". Nothing was overwritten.");
                if (moves.Any(m => m.to == to && m.asset != from.Get(entry.Key))) throw new InvalidOperationException("Different language assets would overwrite the same target source.");
                moves.Add((from, to, from.Get(entry.Key)));
            }
            Draft.MoveEntry(entryId, targetSetId);
            foreach (var move in moves) move.to.Set(entry.Key, move.asset);
            foreach (var from in moves.Select(m => m.from).Distinct())
                if (!moves.Any(m => m.to == from) && !Uses(target, from) && !OtherEntryUses(from, entry.Key, entryId)) from.Remove(entry.Key);
        }
        private (LocalizationContentSet set, LocalizationEntryDefinition entry) AssetEntry(string id)
        {
            EnsureOpen();
            var set = Draft.Sets.SingleOrDefault(s => s.Entries.Any(e => e.Id == id)) ?? throw new ArgumentException("Unknown entry identity.", nameof(id));
            var entry = set.Entries.Single(e => e.Id == id);
            if (entry.Kind != LocalizationEntryKind.Asset) throw new ArgumentException("Expected an asset entry.", nameof(id));
            TrackRoutes(set); return (set, entry);
        }
        private LocalizationAssetTable[] Tables(LocalizationContentSet set, string language)
        {
            TrackRoutes(set); language = LocalizationLanguageCode.Normalize(language);
            var sources = set.Sources.FirstOrDefault(s => s.Language == language)?.Sources.Where(s => s.Kind == LocalizationSourceKind.AssetTable).ToArray()
                ?? Array.Empty<LocalizationSourceReference>();
            var tables = sources.Select(EditSource<LocalizationAssetTable>).ToArray();
            if (tables.Distinct().Count() != tables.Length) throw new InvalidOperationException("The same asset table is connected more than once for " + language + ".");
            return tables;
        }
        private LocalizationAssetTable[] Locations(LocalizationContentSet set, string key) => set.Sources.Select(s => Locate(Tables(set, s.Language), key)).Where(t => t != null).Distinct().ToArray();
        private static LocalizationAssetTable Locate(LocalizationAssetTable[] tables, string key)
        {
            foreach (var table in tables)
                if (table.Entries.Count(e => e != null && e.key == key) > 1) throw new InvalidOperationException("Duplicate asset key '" + key + "' inside a table.");
            var found = tables.Where(t => t.HasKey(key)).ToArray();
            if (found.Length > 1) throw new InvalidOperationException("Asset key '" + key + "' exists in multiple sources."); return found.SingleOrDefault();
        }
        private LocalizationAssetTable Destination(LocalizationAssetTable[] tables, string key, string address, LocalizationAssetTable present)
        {
            if (address == null)
            {
                if (present != null) return present;
                if (tables.Length == 1) return tables[0];
                throw new InvalidOperationException("Choose an explicit asset source for '" + key + "'; register one if none exists.");
            }
            var target = tables.SingleOrDefault(t => SourcesFor(t).Any(s => SameAddress(s, address)));
            if (target == null) throw new ArgumentException("Destination is not connected for this set/language.", nameof(address));
            if (present != null && present != target) throw new InvalidOperationException("Key already belongs to another asset source."); return target;
        }
        private IEnumerable<LocalizationSourceReference> SourcesFor(LocalizationAssetTable table) =>
            sourcePaths.Values.Where(p => assets[p.path].Draft == table).Select(p => p.source);
        private static bool SameAddress(LocalizationSourceReference source, string address) =>
            string.Equals(source.Address, address, source.ProviderId == "resources" ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        private bool Uses(LocalizationContentSet set, LocalizationAssetTable table) =>
            set.Sources.SelectMany(s => s.Sources).Any(s => s.Kind == LocalizationSourceKind.AssetTable &&
                SourcesFor(table).Any(actual => actual.ProviderId == s.ProviderId && SameAddress(actual, s.Address)));
        private bool OtherEntryUses(LocalizationAssetTable table, string key, string excluded) => Draft.Sets.Any(s => s.Entries.Any(e => e.Kind == LocalizationEntryKind.Asset && e.Key == key && e.Id != excluded) && Uses(s, table));
        private void TrackRoutes(LocalizationContentSet set)
        {
            string routes = AssetRoutes(set);
            if (entryRoutes.TryGetValue(set.Id, out var before) && before != routes) throw new InvalidOperationException("Source routing changed. Save routing separately before editing assets.");
            entryRoutes[set.Id] = routes;
        }
        private void ValidateEntryRoutes()
        {
            foreach (var pair in entryRoutes)
            {
                var set = Draft.Sets.SingleOrDefault(s => s.Id == pair.Key);
                if (set == null || AssetRoutes(set) != pair.Value) throw new InvalidOperationException("Source routing changed while editing assets. Save routing separately.");
            }
            var index = sourcePaths.Count == 0 ? null : new ResourcesEditorTextReader();
            foreach (var source in sourcePaths.Values)
                if (assets[source.path].Bytes != null && (source.source.ProviderId == "resources" ? index.ResolveSourceAssetPath(source.source) : LocalizationEditorReaders.ResolveWritableTable(source.source)) != source.path)
                    throw new InvalidOperationException("Source address changed. Reload before saving: " + source.source.Address);
        }
        private static string AssetRoutes(LocalizationContentSet set) => JsonConvert.SerializeObject(set.Sources.OrderBy(s => s.Language, StringComparer.Ordinal)
            .Select(s => new { s.Language, Sources = s.Sources.Where(r => r.Kind == LocalizationSourceKind.AssetTable).Select(r => new { r.ProviderId, r.Address }) }));
    }
}
