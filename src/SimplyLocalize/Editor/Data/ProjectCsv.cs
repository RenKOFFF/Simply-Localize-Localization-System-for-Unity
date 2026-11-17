using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;

namespace SimplyLocalize.Editor.Data
{
    public enum ProjectCsvChangeKind { AddEntry, RenameEntry, MoveEntry, Translation, Description, AddLanguage, EnableLanguage, CreateSource }
    public sealed class ProjectCsvChange
    {
        public ProjectCsvChangeKind Kind { get; }
        public string SetId { get; }
        public string EntryId { get; }
        public string Key { get; }
        public string Language { get; }
        public string Before { get; }
        public string After { get; }
        internal ProjectCsvChange(ProjectCsvChangeKind kind, string set, string entry, string key, string language, string before, string after)
        { Kind = kind; SetId = set; EntryId = entry; Key = key; Language = language; Before = before; After = after; }
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
            internal string Set, Id, Key, Description;
            internal Dictionary<string, string> Values = new(), Sources = new();
        }
        internal static ProjectCsvImportPlan Create(LocalizationProjectConfig config, string csv, string channelId, string setId,
            CsvEmptyCellPolicy policy, Func<string, string, string> newSourcePath)
        {
            if (config == null) throw new ArgumentNullException(nameof(config)); config.ValidateOrThrow();
            if (!Enum.IsDefined(typeof(CsvEmptyCellPolicy), policy)) throw new ArgumentOutOfRangeException(nameof(policy));
            var channel = config.Channels.SingleOrDefault(c => c.Id == (channelId ?? config.MainChannelId)) ?? throw new ArgumentException("Unknown channel identity.");
            setId ??= channel.DefaultSetId;
            if (!channel.SetIds.Contains(setId)) throw new ArgumentException("Default import set is not available in this channel.");
            var parsed = CsvExporter.ParseCsvLines(csv); if (parsed.Count == 0) throw new FormatException("CSV needs a header with Key and language columns; SetId and EntryId are optional.");
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
            var rows = new List<Row>(); var ids = new HashSet<string>(); var keys = new HashSet<string>();
            var included = new HashSet<string>();
            for (int i = 1; i < parsed.Count; i++)
            {
                var fields = parsed[i]; if (fields.Count != header.Count) throw new FormatException("CSV row " + (i + 1) + ": field count differs from header.");
                string Optional(string name) => columns.TryGetValue(name, out var index) ? fields[index] : "";
                var row = new Row { Set = Optional("SetId"), Id = Optional("EntryId"), Key = fields[columns["Key"]], Description = columns.ContainsKey("Description") ? Optional("Description") : null }; if (row.Set.Length == 0) row.Set = setId;
                if (!channel.SetIds.Contains(row.Set)) throw new FormatException("CSV target set is not available in this channel: " + row.Set);
                var target = config.Sets.Single(s => s.Id == row.Set);
                var owner = row.Id.Length == 0 ? target : config.Sets.SingleOrDefault(s => s.Entries.Any(e => e.Id == row.Id));
                var existing = row.Id.Length == 0 ? target.Entries.SingleOrDefault(e => e.Key == row.Key) : owner?.Entries.Single(e => e.Id == row.Id);
                if (existing != null)
                {
                    if (existing.Kind != LocalizationEntryKind.Text) throw new FormatException("CSV text key conflicts with an asset entry: " + row.Key);
                    if (!channel.SetIds.Contains(owner.Id)) throw new FormatException("Existing entry belongs to a set outside this channel.");
                    row.Id = existing.Id; included.Add(owner.Id);
                }
                else row.Id = new LocalizationEntryDefinition(row.Key, LocalizationEntryKind.Text, row.Id.Length == 0 ? null : row.Id).Id;
                _ = new LocalizationEntryDefinition(row.Key, LocalizationEntryKind.Text, row.Id);
                if (!ids.Add(row.Id) || !keys.Add(row.Set + ":" + row.Key)) throw new FormatException("Duplicate CSV entry identity or target key: " + row.Key);
                foreach (string code in languages)
                { row.Values[code] = fields[languageColumns[code]]; if (sourceColumns.TryGetValue(code, out int column) && fields[column].Length > 0) row.Sources[code] = fields[column]; }
                included.Add(row.Set); rows.Add(row);
            }
            var result = new ProjectCsvImportPlan { RowCount = rows.Count };
            try
            {
                result.edit = new LocalizationProjectEdit(config, included.ToArray()); var draft = result.edit.Draft;
                var snapshots = included.ToDictionary(id => id, id => EditorProjectTextData.Load(config, channel.Id, id));
                var changes = new List<ProjectCsvChange>(); var newLanguages = new List<string>();
                void Change(ProjectCsvChangeKind kind, Row row = null, string language = null, string before = null, string after = null) =>
                    changes.Add(new ProjectCsvChange(kind, row?.Set, row?.Id, row?.Key, language, before, after));
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
                foreach (var row in rows)
                {
                    var target = draft.Sets.Single(s => s.Id == row.Set);
                    var owner = draft.Sets.SingleOrDefault(s => s.Entries.Any(e => e.Id == row.Id));
                    var entry = owner?.Entries.Single(e => e.Id == row.Id);
                    var originalOwner = config.Sets.SingleOrDefault(s => s.Entries.Any(e => e.Id == row.Id));
                    string originalKey = originalOwner?.Entries.Single(e => e.Id == row.Id).Key ?? row.Key;
                    var originalData = snapshots[originalOwner?.Id ?? row.Set];
                    foreach (string code in languages.Concat(owner?.Sources.Select(s => s.Language) ?? Array.Empty<string>()).Distinct())
                    {
                        bool needsSource = newLanguages.Contains(code) || owner != null && owner.Id != target.Id
                            || row.Values.TryGetValue(code, out var value) && (value.Length > 0 || policy == CsvEmptyCellPolicy.Clear);
                        if (!needsSource || target.Sources.Any(s => s.Language == code && s.Sources.Any(r => r.Kind == LocalizationSourceKind.TextTable))) continue;
                        if (row.Sources.ContainsKey(code)) throw new FormatException("CSV destination is not connected for " + row.Key + " / " + code + ". Connect it in Settings first.");
                        string path = newSourcePath != null ? newSourcePath(target.Id, code) : LocalizationProjectAuthoring.SuggestTextSourcePath(config, target, code, reserved);
                        result.edit.CreateTextSource(target.Id, code, path); reserved.Add(ResourcesEditorTextReader.ResourceAddress(path));
                        Change(ProjectCsvChangeKind.CreateSource, row, code, after: path);
                    }
                    if (entry == null)
                    {
                        foreach (string code in originalData.Languages)
                            if (originalData.GetUnregistered(code).Count(c => c.Key == row.Key) > 1) throw new FormatException("Key exists in multiple sources: " + row.Key + " / " + code);
                        target.AddEntry(row.Key, LocalizationEntryKind.Text, row.Id); Change(ProjectCsvChangeKind.AddEntry, row, after: row.Key);
                    }
                    else
                    {
                        if (entry.Key != row.Key) { Change(ProjectCsvChangeKind.RenameEntry, row, before: entry.Key, after: row.Key); result.edit.RenameTextEntry(row.Id, row.Key); }
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
                        string value = row.Values[code]; if (value.Length == 0 && policy == CsvEmptyCellPolicy.Skip) { result.SkippedEmptyCells++; continue; }
                        LocalizationEditorTextCell cell = null;
                        if (originalData.Languages.Contains(code))
                        {
                            if (originalOwner != null) originalData.TryGetDirect(row.Id, code, out cell);
                            else cell = originalData.GetUnregistered(code).SingleOrDefault(c => c.Key == originalKey);
                        }
                        string address = row.Sources.TryGetValue(code, out var selectedAddress) ? selectedAddress : null;
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
                        if (cell == null || cell.Value != value) Change(ProjectCsvChangeKind.Translation, row, code, cell?.Value, value);
                    }
                }
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
