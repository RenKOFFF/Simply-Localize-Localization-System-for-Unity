using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SimplyLocalize.Editor.Data
{
    /// <summary>Draft configuration and exact text documents. Include sets before editing their translations.
    /// Commands change only this draft; Commit validates original files/config and publishes them together.</summary>
    public sealed class LocalizationProjectEdit : IDisposable
    {
        private readonly LocalizationProjectConfig original;
        private readonly string configPath, originalJson;
        private readonly byte[] originalBytes;
        private readonly HashSet<string> included;
        private readonly Dictionary<string, Document> documents = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Document> addresses = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> originalRoutes = new(StringComparer.Ordinal);
        private bool committed, disposed;
        public LocalizationProjectConfig Draft { get; }

        public LocalizationProjectEdit(LocalizationProjectConfig config, params string[] setIds)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            config.ValidateOrThrow();
            configPath = AssetDatabase.GetAssetPath(config);
            LocalizationProjectChange.ValidateWritable(configPath);
            if (EditorUtility.IsDirty(config)) throw new InvalidOperationException("Save existing config edits before opening a project edit.");
            original = config; originalJson = ConfigJson(config); originalBytes = File.ReadAllBytes(configPath);
            included = new HashSet<string>(setIds ?? Array.Empty<string>(), StringComparer.Ordinal);
            Draft = ScriptableObject.CreateInstance<LocalizationProjectConfig>();
            EditorJsonUtility.FromJsonOverwrite(originalJson, Draft); Draft.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                var reader = new ResourcesEditorTextReader();
                foreach (string id in included)
                {
                    var set = Set(id);
                    originalRoutes.Add(id, Routes(set));
                    foreach (var source in set.Sources.SelectMany(s => s.Sources).Where(s => s.Kind == LocalizationSourceKind.TextTable))
                    {
                        string path = reader.ResolveAssetPath(source);
                        if (!documents.TryGetValue(path, out var document)) documents.Add(path, document = new Document(path));
                        addresses[source.Address] = document;
                    }
                }
            }
            catch { Object.DestroyImmediate(Draft); throw; }
        }

        public void SetTranslation(string entryId, string language, string value, string destinationAddress = null)
        {
            EnsureOpen();
            if (value == null) throw new ArgumentNullException(nameof(value), "Use RemoveTranslation for a missing translation.");
            var (set, entry) = Entry(entryId); var candidates = Documents(set, language);
            var present = Locate(candidates, entry.Key);
            var destination = Destination(candidates, entry.Key, destinationAddress, present);
            destination.Text[entry.Key] = value;
        }
        public void RemoveTranslation(string entryId, string language)
        {
            EnsureOpen(); var (set, entry) = Entry(entryId);
            Locate(Documents(set, language), entry.Key)?.Text.Property(entry.Key)?.Remove();
        }
        public void RenameTextEntry(string entryId, string key)
        {
            EnsureOpen(); var (set, entry) = Entry(entryId); string previous = entry.Key;
            // Validate key/identity conflicts without touching draft or files.
            _ = new LocalizationEntryDefinition(key, LocalizationEntryKind.Text);
            if (previous == key) return;
            if (set.Entries.Any(e => e.Key == key)) throw new ArgumentException("Target key already exists in this set.", nameof(key));
            var locations = AllLocations(set, previous);
            if (locations.Any(d => d.Text.Property(key) != null)) throw new InvalidOperationException("Target key already exists in a source. Resolve the conflict before renaming.");
            set.RenameEntry(entryId, key);
            foreach (var document in locations)
            {
                document.Text[key] = document.Text[previous].DeepClone();
                if (!OtherEntryUses(document, previous, entryId)) document.Text.Property(previous).Remove();
            }
        }
        public void MoveTextEntry(string entryId, string targetSetId, IReadOnlyDictionary<string, string> destinationAddresses = null)
        {
            EnsureOpen(); var (sourceSet, entry) = Entry(entryId); var target = Set(targetSetId);
            if (sourceSet.Id == target.Id) return;
            RequireIncluded(target.Id);
            if (target.Entries.Any(e => e.Id == entryId || e.Key == entry.Key)) throw new InvalidOperationException("Target set already contains this key or identity.");
            var moves = new List<(Document source, Document target, JToken value)>();
            foreach (var language in sourceSet.Sources.Select(s => s.Language))
            {
                var source = Locate(Documents(sourceSet, language), entry.Key); if (source == null) continue;
                var destinations = Documents(target, language);
                string address = destinationAddresses != null && destinationAddresses.TryGetValue(language, out var selected) ? selected : null;
                var existing = Locate(destinations, entry.Key);
                var destination = Destination(destinations, entry.Key, address, existing);
                if (source == destination) continue;
                if (existing != null) throw new InvalidOperationException("Target source already contains '" + entry.Key + "' for " + language + ". Nothing was overwritten.");
                var pending = moves.FirstOrDefault(m => m.target == destination);
                if (pending.target != null && !JToken.DeepEquals(pending.value, source.Text[entry.Key]))
                    throw new InvalidOperationException("Different language values would overwrite the same target source.");
                moves.Add((source, destination, source.Text[entry.Key].DeepClone()));
            }
            Draft.MoveEntry(entryId, targetSetId);
            foreach (var move in moves) move.target.Text[entry.Key] = move.value;
            foreach (var source in moves.Select(m => m.source).Distinct())
                if (!moves.Any(m => m.target == source) && !EntryUses(target, source) && !OtherEntryUses(source, entry.Key, entryId))
                    source.Text.Property(entry.Key)?.Remove();
        }
        public void RemoveTextEntry(string entryId)
        {
            EnsureOpen(); var (set, entry) = Entry(entryId); var locations = AllLocations(set, entry.Key);
            set.RemoveEntry(entryId);
            foreach (var document in locations)
                if (!OtherEntryUses(document, entry.Key, entryId)) document.Text.Property(entry.Key).Remove();
        }

        public LocalizationProjectChange Commit()
        {
            EnsureOpen(); Draft.ValidateOrThrow();
            var currentPaths = new ResourcesEditorTextReader();
            foreach (var pair in addresses)
                if (currentPaths.ResolveAssetPath(new("resources", pair.Key, LocalizationSourceKind.TextTable)) != pair.Value.Path)
                    throw new InvalidOperationException("Source address changed. Reload before saving: " + pair.Key);
            var files = documents.Values.Select(d => new LocalizationProjectChange.FileChange(d.Path, d.Before, d.Encode())).ToArray();
            if (files.Any(f => !EditorFileTransaction.Equal(f.Before, f.After)))
                foreach (var route in originalRoutes)
                {
                    var set = Draft.Sets.SingleOrDefault(s => s.Id == route.Key);
                    if (set == null || Routes(set) != route.Value)
                        throw new InvalidOperationException("Source routing changed while editing text. Save routing separately, then open a fresh text edit.");
                }
            var change = new LocalizationProjectChange(original, configPath, originalJson, ConfigJson(Draft), originalBytes, files);
            change.Apply(); committed = true; return change;
        }
        public void Dispose() { if (disposed) return; disposed = true; if (Draft != null) Object.DestroyImmediate(Draft); }
        private void EnsureOpen()
        { if (disposed || committed) throw new InvalidOperationException("This edit is closed. Open a fresh edit for further changes."); }
        private LocalizationContentSet Set(string id) => Draft.Sets.SingleOrDefault(s => s.Id == id) ?? throw new ArgumentException("Unknown set identity.", nameof(id));
        private void RequireIncluded(string id)
        { if (!included.Contains(id)) throw new InvalidOperationException("Include the content set when opening this edit: " + id); }
        private (LocalizationContentSet set, LocalizationEntryDefinition entry) Entry(string id)
        {
            foreach (var set in Draft.Sets)
                foreach (var entry in set.Entries)
                    if (entry.Id == id)
                    {
                        RequireIncluded(set.Id);
                        if (entry.Kind != LocalizationEntryKind.Text) throw new ArgumentException("Expected a text entry.", nameof(id));
                        return (set, entry);
                    }
            throw new ArgumentException("Unknown entry identity.", nameof(id));
        }
        private Document[] Documents(LocalizationContentSet set, string language)
        {
            RequireIncluded(set.Id); language = LocalizationLanguageCode.Normalize(language);
            var sources = set.Sources.FirstOrDefault(s => s.Language == language);
            if (sources == null) return Array.Empty<Document>();
            return sources.Sources.Where(s => s.Kind == LocalizationSourceKind.TextTable).Select(s =>
                s.ProviderId == "resources" && addresses.TryGetValue(s.Address, out var document) ? document
                : throw new InvalidOperationException("Source routing changed during the edit; open a fresh edit.")).Distinct().ToArray();
        }
        private Document[] AllLocations(LocalizationContentSet set, string key) => set.Sources
            .Select(s => Locate(Documents(set, s.Language), key)).Where(d => d != null).Distinct().ToArray();
        private static Document Locate(Document[] candidates, string key)
        {
            var matches = candidates.Where(d => d.Text.Property(key) != null).ToArray();
            if (matches.Length > 1) throw new InvalidOperationException("Duplicate key '" + key + "': " + string.Join(", ", matches.Select(d => d.Path)));
            return matches.SingleOrDefault();
        }
        private Document Destination(Document[] candidates, string key, string address, Document existing)
        {
            if (address != null)
            {
                if (!addresses.TryGetValue(address, out var selected) || !candidates.Contains(selected))
                    throw new ArgumentException("Destination is not a registered text source for this set/language.", nameof(address));
                if (existing != null && existing != selected) throw new InvalidOperationException("Key already belongs to another source: " + existing.Path);
                return selected;
            }
            if (existing != null) return existing;
            if (candidates.Length != 1) throw new InvalidOperationException("Choose an explicit text source for '" + key + "'; register one if none exists.");
            return candidates[0];
        }
        private bool EntryUses(LocalizationContentSet set, Document document) => set.Sources.SelectMany(s => s.Sources).Any(s =>
            s.ProviderId == "resources" && s.Kind == LocalizationSourceKind.TextTable &&
            string.Equals(s.Address, ResourcesEditorTextReader.ResourceAddress(document.Path), StringComparison.OrdinalIgnoreCase));
        private bool OtherEntryUses(Document document, string key, string excludedId) => Draft.Sets.Any(s =>
            s.Entries.Any(e => e.Id != excludedId && e.Kind == LocalizationEntryKind.Text && e.Key == key) && EntryUses(s, document));
        private static string Routes(LocalizationContentSet set) => JsonConvert.SerializeObject(set.Sources.OrderBy(s => s.Language, StringComparer.Ordinal)
            .Select(s => new { s.Language, Sources = s.Sources.Where(r => r.Kind == LocalizationSourceKind.TextTable).Select(r => new { r.ProviderId, r.Address }) }));
        internal static string ConfigJson(LocalizationProjectConfig config)
        {
            var json = JObject.Parse(EditorJsonUtility.ToJson(config)); json.Remove("m_ObjectHideFlags");
            return json.ToString(Formatting.None);
        }

        private sealed class Document
        {
            internal readonly string Path;
            internal readonly byte[] Before;
            internal readonly JObject Root, Original;
            private readonly Encoding encoding;
            private readonly bool bom;
            private readonly string newline;
            internal JObject Text => (JObject)Root["translations"];
            internal Document(string path)
            {
                Path = path; Before = File.ReadAllBytes(path);
                using var stream = new StreamReader(new MemoryStream(Before), Encoding.UTF8, true);
                string json = stream.ReadToEnd(); encoding = stream.CurrentEncoding;
                byte[] preamble = encoding.GetPreamble(); bom = preamble.Length > 0 && Before.Take(preamble.Length).SequenceEqual(preamble);
                newline = json.Contains("\r\n") ? "\r\n" : "\n";
                // Match runtime validation, then retain unknown JSON fields and untouched value types.
                SimplyLocalize.Data.LocalizationFileParser.Parse(json);
                using var input = new JsonTextReader(new StringReader(json)) { DateParseHandling = DateParseHandling.None, FloatParseHandling = FloatParseHandling.Decimal, MaxDepth = 64 };
                Root = JObject.Load(input, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                if (input.Read()) throw new FormatException("Trailing JSON content at " + path);
                if (!(Root["translations"] is JObject)) throw new FormatException("Expected a translations object at " + path);
                Original = (JObject)Root.DeepClone();
            }
            internal byte[] Encode()
            {
                if (JToken.DeepEquals(Root, Original)) return Before;
                var output = new StringWriter { NewLine = newline };
                using (var writer = new JsonTextWriter(output) { Formatting = Formatting.Indented }) Root.WriteTo(writer);
                byte[] body = encoding.GetBytes(output.ToString());
                return bom ? encoding.GetPreamble().Concat(body).ToArray() : body;
            }
        }
    }

    /// <summary>Explicit undo/redo for a committed multi-file edit. Refuses stale files/config instead of
    /// overwriting external changes. These methods do not register a native Unity Undo operation.</summary>
    [Serializable]
    public sealed class LocalizationProjectChange
    {
        [Serializable]
        internal struct FileChange
        {
            [SerializeField] internal string Path;
            [SerializeField] internal byte[] Before, After;
            internal FileChange(string path, byte[] before, byte[] after) { Path = path; Before = before; After = after; }
        }
        [SerializeField] private LocalizationProjectConfig config;
        [SerializeField] private string path, beforeJson, afterJson;
        [SerializeField] private byte[] beforeConfig, afterConfig, undoneConfig;
        [SerializeField] private FileChange[] files;
        [SerializeField] private bool isApplied;
        public bool IsApplied => isApplied;
        public bool HasChanges => beforeJson != afterJson || files.Any(f => !EditorFileTransaction.Equal(f.Before, f.After));
        internal LocalizationProjectChange(LocalizationProjectConfig config, string path, string beforeJson, string afterJson, byte[] beforeConfig, FileChange[] files)
        { this.config = config; this.path = path; this.beforeJson = beforeJson; this.afterJson = afterJson; this.beforeConfig = beforeConfig; this.files = files; }
        internal void Apply()
        {
            Write(false, beforeConfig, beforeJson, afterJson); afterConfig = File.ReadAllBytes(path); isApplied = true;
            // Initial save checks every loaded dependency. History only owns files it actually changed.
            files = files.Where(f => !EditorFileTransaction.Equal(f.Before, f.After)).ToArray();
        }
        public void Undo()
        { if (!IsApplied) return; Write(true, afterConfig, afterJson, beforeJson); undoneConfig = File.ReadAllBytes(path); isApplied = false; }
        public void Redo()
        { if (IsApplied) return; Write(false, undoneConfig, beforeJson, afterJson); afterConfig = File.ReadAllBytes(path); isApplied = true; }
        private void Write(bool undo, byte[] expectedConfig, string expectedJson, string targetJson)
        {
            if (config == null || AssetDatabase.GetAssetPath(config) != path || LocalizationProjectEdit.ConfigJson(config) != expectedJson)
                throw new InvalidOperationException("Config changed. Reload before applying this edit.");
            ValidateWritable(path); bool wasDirty = EditorUtility.IsDirty(config);
            using var transaction = new EditorFileTransaction();
            foreach (var file in files)
            {
                if (!EditorFileTransaction.Equal(file.Before, file.After)) ValidateWritable(file.Path);
                transaction.Add(file.Path, undo ? file.After : file.Before, undo ? file.Before : file.After);
            }
            bool changeConfig = expectedJson != targetJson;
            if (changeConfig) transaction.TrackExternalSnapshot(path, expectedConfig); else transaction.Add(path, expectedConfig, expectedConfig);
            bool configTouched = false;
            try
            {
                transaction.Execute(() =>
                {
                    if (!changeConfig) return;
                    if (LocalizationProjectEdit.ConfigJson(config) != expectedJson) throw new InvalidOperationException("Config changed while saving.");
                    transaction.TouchExternal(path);
                    configTouched = true;
                    EditorJsonUtility.FromJsonOverwrite(targetJson, config); EditorUtility.SetDirty(config); AssetDatabase.SaveAssetIfDirty(config);
                    if (EditorUtility.IsDirty(config)) throw new IOException("Unity did not save the config: " + path);
                });
            }
            catch
            {
                if (configTouched)
                {
                    EditorJsonUtility.FromJsonOverwrite(expectedJson, config);
                    if (wasDirty) EditorUtility.SetDirty(config); else EditorUtility.ClearDirty(config);
                }
                throw;
            }
            AssetDatabase.Refresh();
        }
        internal static void ValidateWritable(string path)
        {
            if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/", StringComparison.Ordinal))
                throw new InvalidOperationException("Save an editable asset under Assets before making changes.");
            EditorDataPath.Resolve(Application.dataPath, path.Substring(7));
            if (!AssetDatabase.IsOpenForEdit(path)) throw new IOException("Asset is not editable: " + path);
        }
    }
}
