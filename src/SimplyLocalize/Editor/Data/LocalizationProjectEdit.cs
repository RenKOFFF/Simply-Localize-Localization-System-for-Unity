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
        private readonly HashSet<string> initialPaths, initialAddresses;
        private bool committed, disposed;
        private ProjectNotesDocument notes;
        public LocalizationProjectConfig Draft { get; }

        internal LocalizationDraftBaseline CaptureBaseline()
        {
            EnsureOpen(); var baseline = new LocalizationDraftBaseline(original, configPath, originalJson, originalBytes);
            foreach (var document in documents.Values) baseline.AddFile(document.ReadPath, document.Before);
            if (notes != null) baseline.AddFile(notes.Path, notes.Before);
            return baseline;
        }

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
                var reader = included.Count == 0 ? null : new ResourcesEditorTextReader();
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
            initialPaths = new HashSet<string>(documents.Keys, StringComparer.OrdinalIgnoreCase);
            initialAddresses = new HashSet<string>(addresses.Keys, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>Discards staged changes while retaining the original disk/config baselines.
        /// Use before replaying a corrected UI draft after a failed save; external changes still conflict.</summary>
        public void ResetChanges()
        {
            EnsureOpen(); EditorJsonUtility.FromJsonOverwrite(originalJson, Draft);
            foreach (string path in documents.Keys.Where(p => !initialPaths.Contains(p)).ToArray()) documents.Remove(path);
            foreach (string address in addresses.Keys.Where(a => !initialAddresses.Contains(a)).ToArray()) addresses.Remove(address);
            foreach (var document in documents.Values) document.Reset();
            notes?.Reset();
            originalRoutes.Clear(); foreach (string id in included) originalRoutes.Add(id, Routes(Set(id)));
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
        internal string ResolveTextWritePath(string entryId, string language, string destinationAddress)
        {
            EnsureOpen(); var (set, entry) = Entry(entryId); var candidates = Documents(set, language);
            return Destination(candidates, entry.Key, destinationAddress, Locate(candidates, entry.Key)).Path;
        }
        internal void ValidateTextEntrySources(string entryId)
        { EnsureOpen(); var (set, entry) = Entry(entryId); AllLocations(set, entry.Key); }
        internal bool HasStoredTextEntry(string entryId)
        { EnsureOpen(); var (set, entry) = Entry(entryId); return AllLocations(set, entry.Key).Length > 0; }
        public string GetDescription(string entryId)
        { EnsureOpen(); Entry(entryId); return (notes ??= new ProjectNotesDocument(original)).Get(entryId); }
        public void SetDescription(string entryId, string description)
        { EnsureOpen(); Entry(entryId); (notes ??= new ProjectNotesDocument(original)).Set(entryId, description); }
        /// <summary>Stages a new empty JSON and its exact source binding. Existing files/addresses are never adopted or overwritten.</summary>
        public void CreateTextSource(string setId, string language, string assetPath)
        {
            EnsureOpen(); RequireIncluded(setId); var set = Set(setId);
            language = LocalizationLanguageCode.Normalize(language);
            if (!Draft.Languages.Any(l => l.Code == language)) throw new ArgumentException("Add the language definition before its source.", nameof(language));
            if (Routes(set) != originalRoutes[setId]) throw new InvalidOperationException("Source routing changed. Open a fresh edit before adding a source.");
            LocalizationProjectChange.ValidateWritable(assetPath);
            foreach (string part in assetPath.Split('/')) EditorDataPath.ValidateName(part, "source path segment");
            string address = ResourcesEditorTextReader.ResourceAddress(assetPath);
            if (!assetPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || !LocalizationResourceAddress.IsValid(address))
                throw new ArgumentException("Choose a JSON file inside a non-Editor Resources folder.", nameof(assetPath));
            if (File.Exists(assetPath) || Directory.Exists(assetPath) || File.Exists(assetPath + ".meta") || addresses.ContainsKey(address))
                throw new IOException("Source destination already exists: " + assetPath);
            if (AssetDatabase.GetAllAssetPaths().Any(p => !AssetDatabase.IsValidFolder(p) && string.Equals(ResourcesEditorTextReader.ResourceAddress(p), address, StringComparison.OrdinalIgnoreCase)))
                throw new IOException("Resources address already exists: " + address);
            var document = new Document(assetPath, create: true); documents.Add(assetPath, document); addresses.Add(address, document);
            var sources = set.Sources.FirstOrDefault(s => s.Language == language)?.Sources ?? Array.Empty<LocalizationSourceReference>();
            set.SetSources(language, sources.Concat(new[] { new LocalizationSourceReference("resources", address, LocalizationSourceKind.TextTable) }).ToArray());
            originalRoutes[setId] = Routes(set);
        }
        public void RemoveTranslation(string entryId, string language)
        {
            EnsureOpen(); var (set, entry) = Entry(entryId);
            Locate(Documents(set, language), entry.Key)?.Text.Property(entry.Key)?.Remove();
        }
        /// <summary>Registers an existing exact text asset without changing its contents or inventing entry IDs.</summary>
        public void RegisterTextSource(string setId, string language, string assetPath)
        {
            EnsureOpen(); RequireIncluded(setId); var set = Set(setId); language = LocalizationLanguageCode.Normalize(language);
            if (!Draft.Languages.Any(l => l.Code == language)) throw new ArgumentException("Add the language definition before its source.");
            if (Routes(set) != originalRoutes[setId]) throw new InvalidOperationException("Source routing changed. Open a fresh edit.");
            string address = ResourcesEditorTextReader.ResourceAddress(assetPath);
            var source = new LocalizationSourceReference("resources", address, LocalizationSourceKind.TextTable);
            if (AssetDatabase.GetMainAssetTypeAtPath(assetPath) == null) AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            var reader = new ResourcesEditorTextReader();
            if (reader.ResolveAssetPath(source) != assetPath) throw new ArgumentException("Source path does not match its Resources address.");
            if (!documents.TryGetValue(assetPath, out var document)) documents.Add(assetPath, document = new Document(assetPath));
            addresses[address] = document;
            var sources = set.Sources.FirstOrDefault(s => s.Language == language)?.Sources ?? Array.Empty<LocalizationSourceReference>();
            if (sources.Any(s => s.ProviderId == source.ProviderId && s.Kind == source.Kind && string.Equals(s.Address, address, StringComparison.OrdinalIgnoreCase))) return;
            set.SetSources(language, sources.Concat(new[] { source }).ToArray()); originalRoutes[setId] = Routes(set);
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
            (notes ??= new ProjectNotesDocument(original)).Set(entryId, null);
            set.RemoveEntry(entryId);
            foreach (var document in locations)
                if (!OtherEntryUses(document, entry.Key, entryId)) document.Text.Property(entry.Key).Remove();
        }

        public LocalizationProjectChange Commit() => CommitWithNativeFiles(null, null);
        internal LocalizationProjectChange CommitWithNativeFiles(IReadOnlyDictionary<string, byte[]> nativeFiles, Action<EditorFileTransaction> writeNative)
        {
            EnsureOpen(); Draft.ValidateOrThrow();
            var currentPaths = addresses.Count == 0 ? null : new ResourcesEditorTextReader();
            foreach (var pair in addresses)
                if (pair.Value.Before == null)
                {
                    if (AssetDatabase.GetAllAssetPaths().Any(p => !AssetDatabase.IsValidFolder(p) && string.Equals(ResourcesEditorTextReader.ResourceAddress(p), pair.Key, StringComparison.OrdinalIgnoreCase)))
                        throw new IOException("Resources address appeared during the edit: " + pair.Key);
                }
                else if (currentPaths.ResolveAssetPath(new("resources", pair.Key, LocalizationSourceKind.TextTable)) != pair.Value.Path)
                    throw new InvalidOperationException("Source address changed. Reload before saving: " + pair.Key);
            var textFiles = documents.Values.Select(d => new LocalizationProjectChange.FileChange(d.ReadPath, d.Before, d.Encode())).ToArray();
            var files = notes == null ? textFiles : textFiles.Concat(new[] { new LocalizationProjectChange.FileChange(notes.Path, notes.Before, notes.Encode()) }).ToArray();
            if (textFiles.Any(f => !EditorFileTransaction.Equal(f.Before, f.After)))
                foreach (var route in originalRoutes)
                {
                    var set = Draft.Sets.SingleOrDefault(s => s.Id == route.Key);
                    if (set == null || Routes(set) != route.Value)
                        throw new InvalidOperationException("Source routing changed while editing text. Save routing separately, then open a fresh text edit.");
                }
            var change = new LocalizationProjectChange(original, configPath, originalJson, ConfigJson(Draft), originalBytes, files);
            change.Apply(nativeFiles, writeNative); committed = true; return change;
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
            internal string ReadPath => ResourcesEditorTextReader.ResolveFilePath(Path);
            internal readonly byte[] Before;
            internal readonly JObject Root, Original;
            private readonly Encoding encoding;
            private readonly bool bom;
            private readonly string newline;
            internal JObject Text => (JObject)Root["translations"];
            internal Document(string path, bool create = false)
            {
                Path = path;
                if (create)
                {
                    Root = new JObject { ["translations"] = new JObject() }; Original = (JObject)Root.DeepClone();
                    encoding = new UTF8Encoding(false); newline = "\n"; return;
                }
                Before = File.ReadAllBytes(ReadPath);
                string json = EditorTextEncoding.Decode(Before, path, out encoding, out bom);
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
                if (Before != null && JToken.DeepEquals(Root, Original)) return Before;
                var output = new StringWriter { NewLine = newline };
                using (var writer = new JsonTextWriter(output) { Formatting = Formatting.Indented }) Root.WriteTo(writer);
                byte[] body = encoding.GetBytes(output.ToString());
                return bom ? encoding.GetPreamble().Concat(body).ToArray() : body;
            }
            internal void Reset()
            {
                Root.RemoveAll();
                foreach (var property in Original.Properties()) Root.Add(property.Name, property.Value.DeepClone());
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
        internal void Apply(IReadOnlyDictionary<string, byte[]> nativeFiles = null, Action<EditorFileTransaction> writeNative = null)
        {
            Write(false, beforeConfig, beforeJson, afterJson, nativeFiles, writeNative, true);
        }
        internal static LocalizationProjectChange CaptureApplied(LocalizationProjectConfig config, string path, string beforeJson,
            string afterJson, byte[] beforeConfig, FileChange[] files)
        {
            var change = new LocalizationProjectChange(config, path, beforeJson, afterJson, beforeConfig, files);
            change.FinishApplied(); return change;
        }
        private void FinishApplied()
        {
            afterConfig = File.ReadAllBytes(path); isApplied = true;
            // Unity generated the GUIDs of new assets. Keep them with the file for native Undo/Redo.
            files = files.Concat(files.Where(f => f.Before == null && f.After != null && File.Exists(f.Path + ".meta"))
                .Select(f => new FileChange(f.Path + ".meta", null, File.ReadAllBytes(f.Path + ".meta")))).ToArray();
            // Initial save checks every loaded dependency. History only owns files it actually changed.
            files = files.Where(f => !EditorFileTransaction.Equal(f.Before, f.After)).ToArray();
        }
        public void Undo()
        { if (!IsApplied) return; Write(true, afterConfig, afterJson, beforeJson); undoneConfig = File.ReadAllBytes(path); isApplied = false; }
        public void Redo()
        { if (IsApplied) return; Write(false, undoneConfig, beforeJson, afterJson); afterConfig = File.ReadAllBytes(path); isApplied = true; }
        private void Write(bool undo, byte[] expectedConfig, string expectedJson, string targetJson,
            IReadOnlyDictionary<string, byte[]> nativeFiles = null, Action<EditorFileTransaction> writeNative = null, bool captureInitial = false)
        {
            if (config == null || AssetDatabase.GetAssetPath(config) != path || LocalizationProjectEdit.ConfigJson(config) != expectedJson)
                throw new InvalidOperationException("Config changed. Reload before applying this edit.");
            ValidateWritable(path); bool wasDirty = EditorUtility.IsDirty(config);
            using var transaction = new EditorFileTransaction();
            var generatedMeta = new List<string>();
            foreach (var file in files)
            {
                if (!EditorFileTransaction.Equal(file.Before, file.After)) ValidateWritable(file.Path);
                transaction.Add(file.Path, undo ? file.After : file.Before, undo ? file.Before : file.After);
                if (!undo && file.Before == null && file.After != null && !file.Path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)
                    && !files.Any(f => f.Path == file.Path + ".meta"))
                {
                    transaction.TrackExternalSnapshot(file.Path + ".meta", null); generatedMeta.Add(file.Path + ".meta");
                }
            }
            if (nativeFiles != null) foreach (var file in nativeFiles)
            { ValidateWritable(file.Key); transaction.TrackExternalSnapshot(file.Key, file.Value); }
            bool changeConfig = expectedJson != targetJson;
            if (changeConfig) transaction.TrackExternalSnapshot(path, expectedConfig); else transaction.Add(path, expectedConfig, expectedConfig);
            bool configTouched = false;
            try
            {
                transaction.Execute(() =>
                {
                    foreach (string meta in generatedMeta) transaction.TouchExternal(meta);
                    if (changeConfig)
                    {
                        if (LocalizationProjectEdit.ConfigJson(config) != expectedJson) throw new InvalidOperationException("Config changed while saving.");
                        transaction.TouchExternal(path); configTouched = true;
                        EditorJsonUtility.FromJsonOverwrite(targetJson, config); EditorUtility.SetDirty(config); AssetDatabase.SaveAssetIfDirty(config);
                        if (EditorUtility.IsDirty(config)) throw new IOException("Unity did not save the config: " + path);
                    }
                    writeNative?.Invoke(transaction);
                    AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                    if (captureInitial)
                    {
                        if (nativeFiles != null) files = files.Concat(nativeFiles.Select(f => new FileChange(f.Key, f.Value, File.ReadAllBytes(f.Key)))).ToArray();
                        FinishApplied();
                    }
                });
            }
            catch
            {
                if (configTouched)
                {
                    EditorJsonUtility.FromJsonOverwrite(expectedJson, config);
                    if (wasDirty) EditorUtility.SetDirty(config); else EditorUtility.ClearDirty(config);
                }
                if (nativeFiles != null) AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                throw;
            }
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
