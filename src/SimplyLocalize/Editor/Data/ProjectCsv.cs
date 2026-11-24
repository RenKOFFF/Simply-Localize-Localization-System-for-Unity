using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;

namespace SimplyLocalize.Editor.Data
{
    public enum CsvEmptyCellPolicy { Skip, Clear }
    public enum ProjectCsvChangeKind { AddEntry, RenameEntry, MoveEntry, Translation, Description, AddLanguage, EnableLanguage, CreateSource }
    public sealed class ProjectCsvChange
    {
        public ProjectCsvChangeKind Kind { get; }
        public string SetId { get; }
        public string EntryId { get; }
        public string Key { get; }
        public string Language { get; }
        public string FilePath { get; }
        public string Before { get; }
        public string After { get; }
        internal ProjectCsvChange(ProjectCsvChangeKind kind, string set, string entry, string key, string language, string before, string after, string filePath)
        { FilePath = filePath; Kind = kind; SetId = set; EntryId = entry; Key = key; Language = language; Before = before; After = after; }
    }
    /// <summary>CSV for registered text entries. Optional SetId/EntryId columns preserve identity across moves and renames.</summary>
    public static class ProjectCsv
    {
        public static string Export(LocalizationProjectConfig config, string channelId = null, string setId = null,
            bool includeSources = false, ILocalizationEditorTextReader reader = null)
        {
            var data = EditorProjectTextData.Load(config, channelId, setId, reader);
            var notes = LocalizationProjectNotes.Load(config);
            var output = new StringBuilder(); var header = new List<string> { "SetId", "EntryId", "Key", "Description" };
            foreach (string code in data.Languages) { header.Add(code); if (includeSources) header.Add("Source:" + code); }
            Line(output, header);
            foreach (var entry in data.Entries.OrderBy(p => p.Value, StringComparer.Ordinal))
            {
                var row = new List<string> { data.SetId, entry.Key, entry.Value, notes.TryGetValue(entry.Key, out var description) ? description : "" };
                foreach (string code in data.Languages)
                {
                    data.TryGetDirect(entry.Key, code, out var cell); row.Add(cell?.Value ?? "");
                    if (includeSources) row.Add(cell?.Source.Address ?? "");
                }
                Line(output, row);
            }
            return output.ToString();
        }
        public static ProjectCsvImportPlan Preview(LocalizationProjectConfig config, string csv, string channelId = null, string setId = null,
            CsvEmptyCellPolicy emptyCells = CsvEmptyCellPolicy.Skip, Func<string, string, string> newSourcePath = null)
            => ProjectCsvImportPlan.Create(config, csv, channelId, setId, emptyCells, newSourcePath);
        /// <summary>Reports preparation progress on the calling thread. Throw OperationCanceledException to discard the draft.</summary>
        public static ProjectCsvImportPlan Preview(LocalizationProjectConfig config, string csv, string channelId, string setId,
            CsvEmptyCellPolicy emptyCells, Func<string, string, string> newSourcePath, Action<float, string> progress)
            => ProjectCsvImportPlan.Create(config, csv, channelId, setId, emptyCells, newSourcePath, progress);
        private static void Line(StringBuilder output, IEnumerable<string> values) => output.AppendLine(string.Join(",", values.Select(Escape)));
        private static string Escape(string value) => value != null && value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value ?? "";
    }
    /// <summary>Owns an isolated edit captured before preview. Dispose on Cancel; Apply validates the original files again.</summary>
    public sealed class ProjectCsvImportPlan : IDisposable
    {
        private LocalizationProjectEdit edit;
        public IReadOnlyList<ProjectCsvChange> Changes { get; private set; }
        public IReadOnlyList<string> NewLanguages { get; private set; }
        public IReadOnlyList<string> Warnings { get; private set; }
        public int RowCount { get; private set; }
        public int SkippedEmptyCells { get; private set; }
        public bool IsApplied { get; private set; }
        private sealed class Row
        {
            internal string Set, Id, Key, Description, OriginalSet, OriginalKey;
            internal List<string> Fields;
            internal Dictionary<string, string> Sources;
        }
        internal static ProjectCsvImportPlan Create(LocalizationProjectConfig config, string csv, string channelId, string setId,
            CsvEmptyCellPolicy policy, Func<string, string, string> newSourcePath, Action<float, string> progress = null)
        {
            progress?.Invoke(0, "Reading CSV");
            if (config == null) throw new ArgumentNullException(nameof(config)); config.ValidateOrThrow();
            if (!Enum.IsDefined(typeof(CsvEmptyCellPolicy), policy)) throw new ArgumentOutOfRangeException(nameof(policy));
            var channel = config.Channels.SingleOrDefault(c => c.Id == (channelId ?? config.MainChannelId)) ?? throw new ArgumentException("Unknown channel identity.");
            setId ??= channel.DefaultSetId;
            if (!channel.SetIds.Contains(setId)) throw new ArgumentException("Default import set is not available in this channel.");
            var parsed = CsvCodec.ParseCsvLines(csv, progress == null ? (Action<float>)null : fraction => progress(.05f * fraction, "Reading CSV")); if (parsed.Count == 0) throw new FormatException("CSV needs a header with Key and language columns; SetId and EntryId are optional.");
            var header = parsed[0]; var columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var languages = new List<string>(); var languageColumns = new Dictionary<string, int>(); var sourceColumns = new Dictionary<string, int>();
            for (int i = 0; i < header.Count; i++)
            {
                string label = header[i].Trim(); if (!columns.TryAdd(label, i)) throw new FormatException("Duplicate CSV column: " + label);
                if (new[] { "Key", "SetId", "EntryId", "Description" }.Contains(label, StringComparer.OrdinalIgnoreCase)) continue;
                bool source = label.StartsWith("Source:", StringComparison.OrdinalIgnoreCase);
                string code;
                try { code = LocalizationLanguageCode.Normalize(source ? label.Substring(7) : label); }
                catch (ArgumentException failure) { throw new FormatException("Invalid CSV column '" + label + "'. Use Key, optional SetId/EntryId/Description, language codes and optional Source:code columns.", failure); }
                var destination = source ? sourceColumns : languageColumns;
                if (!destination.TryAdd(code, i)) throw new FormatException("Duplicate normalized language column: " + label);
                if (!source) languages.Add(code);
            }
            if (!columns.ContainsKey("Key") || languages.Count == 0) throw new FormatException("CSV needs Key and at least one language column.");
            if (sourceColumns.Keys.Any(code => !languageColumns.ContainsKey(code))) throw new FormatException("Each Source:code column needs its language column.");
            var rows = new List<Row>(); var ids = new HashSet<string>(); var keys = new HashSet<(string set, string key)>();
            var included = new HashSet<string>();
            var byId = config.Sets.SelectMany(set => set.Entries.Select(entry => (set, entry)))
                .ToDictionary(pair => pair.entry.Id, StringComparer.Ordinal);
            var byKey = byId.Values.ToDictionary(pair => (pair.set.Id, pair.entry.Key), pair => pair.entry);
            bool hasMovesOrRenames = false;
            for (int i = 1; i < parsed.Count; i++)
            {
                if (i % 128 == 0) progress?.Invoke(.05f + .2f * i / parsed.Count, "Matching row " + i + " of " + (parsed.Count - 1));
                var fields = parsed[i]; if (fields.Count != header.Count) throw new FormatException("CSV row " + (i + 1) + ": field count differs from header.");
                string Optional(string name) => columns.TryGetValue(name, out var index) ? fields[index] : "";
                var row = new Row { Set = Optional("SetId"), Id = Optional("EntryId"), Key = fields[columns["Key"]], Description = columns.ContainsKey("Description") ? Optional("Description") : null }; if (row.Set.Length == 0) row.Set = setId;
                if (!channel.SetIds.Contains(row.Set)) throw new FormatException("CSV target set is not available in this channel: " + row.Set);
                LocalizationEntryDefinition existing = null;
                string owner = row.Set;
                if (row.Id.Length == 0) byKey.TryGetValue((row.Set, row.Key), out existing);
                else if (byId.TryGetValue(row.Id, out var located)) { owner = located.set.Id; existing = located.entry; }
                if (existing != null)
                {
                    if (existing.Kind != LocalizationEntryKind.Text) throw new FormatException("CSV text key conflicts with an asset entry: " + row.Key);
                    if (!channel.SetIds.Contains(owner)) throw new FormatException("Existing entry belongs to a set outside this channel.");
                    row.Id = existing.Id; row.OriginalSet = owner; row.OriginalKey = existing.Key; included.Add(owner);
                    hasMovesOrRenames |= owner != row.Set || existing.Key != row.Key;
                }
                else row.Id = new LocalizationEntryDefinition(row.Key, LocalizationEntryKind.Text, row.Id.Length == 0 ? null : row.Id).Id;
                if (existing != null && row.Key != existing.Key) _ = new LocalizationEntryDefinition(row.Key, LocalizationEntryKind.Text, row.Id);
                if (!ids.Add(row.Id) || !keys.Add((row.Set, row.Key))) throw new FormatException("Duplicate CSV entry identity or target key: " + row.Key);
                row.Fields = fields;
                foreach (var column in sourceColumns)
                    if (fields[column.Value].Length > 0) (row.Sources ??= new Dictionary<string, string>())[column.Key] = fields[column.Value];
                included.Add(row.Set); rows.Add(row);
            }
            var result = new ProjectCsvImportPlan { RowCount = rows.Count };
            try
            {
                progress?.Invoke(.25f, "Reading saved tables");
                result.edit = new LocalizationProjectEdit(config, included.ToArray()); var draft = result.edit.Draft;
                var snapshots = included.ToDictionary(id => id, id => EditorProjectTextData.Load(config, channel.Id, id));
                var sets = draft.Sets.ToDictionary(set => set.Id);
                if (!hasMovesOrRenames)
                    foreach (var group in rows.Where(row => row.OriginalSet == null).GroupBy(row => row.Set))
                        sets[group.Key].AddEntries(group.Select(row => new LocalizationEntryDefinition(row.Key, LocalizationEntryKind.Text, row.Id)));
                result.edit.IndexImportEntries();
                var extras = new Dictionary<(string, string), ILookup<string, LocalizationEditorTextCell>>();
                IEnumerable<LocalizationEditorTextCell> Unregistered(EditorProjectTextData data, string language, string key)
                {
                    if (!extras.TryGetValue((data.SetId, language), out var lookup))
                        extras[(data.SetId, language)] = lookup = data.GetUnregistered(language).ToLookup(cell => cell.Key, StringComparer.Ordinal);
                    return lookup[key];
                }
                var changes = new List<ProjectCsvChange>(); var newLanguages = new List<string>();
                string configFile = AssetDatabase.GetAssetPath(config);
                string notesFile = null;
                void Change(ProjectCsvChangeKind kind, Row row = null, string language = null, string before = null, string after = null, string file = null) =>
                    changes.Add(new ProjectCsvChange(kind, row?.Set, row?.Id, row?.Key, language, before, after, file ?? (kind == ProjectCsvChangeKind.Description ? (notesFile ??= LocalizationProjectNotes.GetAssetPath(config)) : configFile)));
                if (rows.Count > 0)
                    foreach (string code in languages)
                    {
                        if (!draft.Languages.Any(l => l.Code == code))
                        {
                            var language = draft.AddLanguage(code); var catalog = LanguageCatalog.Find(code);
                            if (catalog != null) { language.DisplayName = catalog.NativeName; language.SystemLanguage = catalog.SystemLanguage; }
                            newLanguages.Add(code); Change(ProjectCsvChangeKind.AddLanguage, language: code, after: code);
                        }
                        var selectedChannel = draft.Channels.Single(c => c.Id == channel.Id);
                        if (!selectedChannel.Languages.Contains(code))
                        { selectedChannel.SetLanguages(selectedChannel.Languages.Concat(new[] { code })); Change(ProjectCsvChangeKind.EnableLanguage, language: code, after: channel.Name); }
                    }
                var reserved = new HashSet<string>(AssetDatabase.GetAllAssetPaths().Where(p => !AssetDatabase.IsValidFolder(p)).Select(ResourcesEditorTextReader.ResourceAddress).Where(p => p != null), StringComparer.OrdinalIgnoreCase);
                var writes = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
                var warnings = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < rows.Count; i++)
                {
                    if (i % 128 == 0) progress?.Invoke(.35f + .6f * i / rows.Count, "Comparing row " + (i + 1) + " of " + rows.Count);
                    var row = rows[i];
                    var target = sets[row.Set];
                    var owner = row.OriginalSet == null ? null : sets[row.OriginalSet];
                    string originalKey = row.OriginalKey ?? row.Key;
                    var originalData = snapshots[row.OriginalSet ?? row.Set];
                    foreach (string code in languages.Concat(owner?.Sources.Select(s => s.Language) ?? Array.Empty<string>()).Distinct())
                    {
                        bool needsSource = newLanguages.Contains(code) || owner != null && owner.Id != target.Id
                            || languageColumns.TryGetValue(code, out var column) && (row.Fields[column].Length > 0 || policy == CsvEmptyCellPolicy.Clear);
                        if (!needsSource || target.Sources.Any(s => s.Language == code && s.Sources.Any(r => r.Kind == LocalizationSourceKind.TextTable))) continue;
                        if (row.Sources?.ContainsKey(code) == true) throw new FormatException("CSV destination is not connected for " + row.Key + " / " + code + ". Connect it in Settings first.");
                        string path = newSourcePath != null ? newSourcePath(target.Id, code) : LocalizationProjectAuthoring.SuggestTextSourcePath(config, target, code, reserved);
                        result.edit.CreateTextSource(target.Id, code, path); reserved.Add(ResourcesEditorTextReader.ResourceAddress(path));
                        Change(ProjectCsvChangeKind.CreateSource, row, code, after: path, file: path);
                    }
                    if (owner == null)
                    {
                        foreach (string code in originalData.Languages)
                            if (Unregistered(originalData, code, row.Key).Count() > 1) throw new FormatException("Key exists in multiple sources: " + row.Key + " / " + code);
                        if (hasMovesOrRenames)
                            result.edit.TrackImportEntry(target, target.AddEntry(row.Key, LocalizationEntryKind.Text, row.Id));
                        Change(ProjectCsvChangeKind.AddEntry, row, after: row.Key);
                    }
                    else
                    {
                        if (originalKey != row.Key) { Change(ProjectCsvChangeKind.RenameEntry, row, before: originalKey, after: row.Key); result.edit.RenameTextEntry(row.Id, row.Key); }
                        if (owner.Id != target.Id) { result.edit.MoveTextEntry(row.Id, target.Id, row.Sources); Change(ProjectCsvChangeKind.MoveEntry, row, before: owner.Id, after: target.Id); }
                    }
                    result.edit.ValidateTextEntrySources(row.Id);
                    if (row.Description != null)
                    {
                        string before = result.edit.GetDescription(row.Id);
                        if (row.Description.Length == 0 && policy == CsvEmptyCellPolicy.Skip) result.SkippedEmptyCells++;
                        else if (before != row.Description) { result.edit.SetDescription(row.Id, row.Description); Change(ProjectCsvChangeKind.Description, row, before: before, after: row.Description); }
                    }
                    foreach (string code in languages)
                    {
                        string value = row.Fields[languageColumns[code]]; if (value.Length == 0 && policy == CsvEmptyCellPolicy.Skip) { result.SkippedEmptyCells++; continue; }
                        LocalizationEditorTextCell cell = null;
                        if (originalData.Languages.Contains(code))
                        {
                            if (owner != null) originalData.TryGetDirect(row.Id, code, out cell);
                            else cell = Unregistered(originalData, code, originalKey).SingleOrDefault();
                        }
                        string address = null;
                        row.Sources?.TryGetValue(code, out address);
                        string path = result.edit.ResolveTextWritePath(row.Id, code, address);
                        if (!writes.TryGetValue(path, out var fileWrites)) writes.Add(path, fileWrites = new Dictionary<string, string>(StringComparer.Ordinal));
                        if (fileWrites.TryGetValue(row.Key, out var previous) && previous != value) throw new FormatException("Different CSV values target the same shared source key: " + row.Key + " at " + path);
                        fileWrites[row.Key] = value;
                        if (cell != null && cell.Value == value) continue;
                        string resource = ResourcesEditorTextReader.ResourceAddress(path);
                        var connections = draft.Sets.SelectMany(s => s.Sources.Where(l => l.Sources.Any(r => r.ProviderId == "resources" && r.Kind == LocalizationSourceKind.TextTable && string.Equals(r.Address, resource, StringComparison.OrdinalIgnoreCase)))
                            .Select(l => s.Name + " / " + l.Language)).ToArray();
                        if (connections.Length > 1) warnings.Add("Shared source " + resource + ": changes also affect " + string.Join(", ", connections) + ".");
                        result.edit.SetTranslation(row.Id, code, value, address);
                        if (cell == null || cell.Value != value) Change(ProjectCsvChangeKind.Translation, row, code, cell?.Value, value, path);
                    }
                }
                progress?.Invoke(.97f, "Validating changes");
                draft.ValidateOrThrow(); result.Changes = changes.AsReadOnly(); result.NewLanguages = newLanguages.AsReadOnly(); result.Warnings = warnings.OrderBy(w => w, StringComparer.Ordinal).ToList().AsReadOnly(); return result;
            }
            catch { result.Dispose(); throw; }
        }
        public LocalizationProjectChange Apply()
        {
            if (edit == null || IsApplied) throw new InvalidOperationException("This CSV preview is closed. Create a new preview.");
            var change = edit.Commit(); IsApplied = true; Dispose(); return change;
        }
        public void Dispose() { edit?.Dispose(); edit = null; }
    }
}
