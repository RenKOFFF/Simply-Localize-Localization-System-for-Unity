using System;
using System.Collections.Generic;
using System.Linq;

namespace SimplyLocalize.Editor.Data
{
    public enum CsvEmptyCellPolicy { Skip, Clear }
    public enum CsvChangeKind { AddKey, Translation, Description, AddLanguage }

    public sealed class CsvImportChange
    {
        public CsvChangeKind Kind { get; }
        public string Key { get; }
        public string Language { get; }
        public string Before { get; }
        public string After { get; }
        internal CsvImportChange(CsvChangeKind kind, string key, string language, string before, string after)
        { Kind = kind; Key = key; Language = language; Before = before; After = after; }
    }

    /// <summary>Validated, immutable preview. Applying a stale preview fails without changing data.</summary>
    public sealed class CsvImportPlan
    {
        private readonly EditorLocalizationData _owner, _candidate;
        private readonly string _signature;
        public IReadOnlyList<CsvImportChange> Changes { get; }
        public IReadOnlyList<string> NewLanguages { get; }
        public int RowCount { get; }
        public int SkippedEmptyCells { get; }

        private CsvImportPlan(EditorLocalizationData owner, EditorLocalizationData candidate,
            List<CsvImportChange> changes, List<string> newLanguages, int rows, int skipped)
        {
            _owner = owner; _candidate = candidate; _signature = owner.StateSignature();
            Changes = changes.AsReadOnly(); NewLanguages = newLanguages.AsReadOnly(); RowCount = rows; SkippedEmptyCells = skipped;
        }

        public void Apply(EditorLocalizationData data, bool saveToDisk = false) => Apply(data, saveToDisk, null);

        /// <summary>Applies and saves the preview, creating any new language profiles in the config.</summary>
        public void ApplyAndSave(EditorLocalizationData data, LocalizationConfig config) => CsvProfileImport.Apply(this, data, config);

        internal void Apply(EditorLocalizationData data, bool saveToDisk, Action afterWrite, Action<EditorFileTransaction> enlist = null)
        {
            if (!ReferenceEquals(data, _owner) || data.StateSignature() != _signature)
                throw new InvalidOperationException("Localization data changed after CSV preview. Create a new preview before applying.");
            if (RowCount == 0) return;
            var candidate = _candidate.Clone();
            if (saveToDisk) candidate.SaveAll(afterWrite, enlist);
            data.CopyFrom(candidate);
        }

        internal static CsvImportPlan Create(EditorLocalizationData data, string csv, CsvEmptyCellPolicy policy)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (!Enum.IsDefined(typeof(CsvEmptyCellPolicy), policy)) throw new ArgumentOutOfRangeException(nameof(policy));
            var rows = CsvExporter.ParseCsvLines(csv);
            var candidate = data.Clone();
            var changes = new List<CsvImportChange>();
            var newLanguages = new List<string>();
            if (rows.Count == 0) return new CsvImportPlan(data, candidate, changes, newLanguages, 0, 0);
            var header = rows[0];
            if (header.Count < 3 || !header[0].Trim().Equals("Key", StringComparison.OrdinalIgnoreCase)
                || !header[1].Trim().Equals("File", StringComparison.OrdinalIgnoreCase))
                throw new FormatException("CSV header must start with Key,File, followed by optional Description and language codes.");
            bool description = header[2].Trim().Equals("Description", StringComparison.OrdinalIgnoreCase);
            int firstLanguage = description ? 3 : 2;
            if (header.Count <= firstLanguage) throw new FormatException("CSV must contain at least one language column.");
            var languages = header.Skip(firstLanguage).Select(c => c.Trim()).ToList();
            var uniqueLanguages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string language in languages)
            {
                EditorDataPath.ValidateName(language, "language code");
                if (!uniqueLanguages.Add(language) || new[] { "Key", "File", "Description" }.Contains(language, StringComparer.OrdinalIgnoreCase))
                    throw new FormatException("Duplicate or reserved CSV language column: " + language);
                string existing = data.LoadedLanguages.FirstOrDefault(code => code.Equals(language, StringComparison.OrdinalIgnoreCase));
                if (existing != null && existing != language)
                    throw new FormatException($"Language '{language}' differs only in case from '{existing}'. Use the configured spelling.");
            }
            var keys = new HashSet<string>(StringComparer.Ordinal);
            var files = new HashSet<string>(data.SourceFiles, StringComparer.Ordinal);
            int skipped = 0, imported = 0;
            for (int row = 1; row < rows.Count; row++)
            {
                var fields = rows[row];
                if (fields.Count != header.Count) throw new FormatException($"CSV row {row + 1}: expected {header.Count} fields, got {fields.Count}.");
                string key = fields[0];
                if (string.IsNullOrWhiteSpace(key) || !keys.Add(key)) throw new FormatException($"CSV row {row + 1}: empty or duplicate key '{key}'.");
                string file = fields[1].Trim();
                string previousFile = data.GetFileForKey(key);
                if (file.Length == 0) file = previousFile ?? "global";
                data.ValidateSourceFileName(file);
                if (previousFile != null && previousFile != file)
                    throw new FormatException($"CSV row {row + 1}: key '{key}' belongs to '{previousFile}', not '{file}'. Move it explicitly in the editor first.");
                if (files.Any(f => f != file && f.Equals(file, StringComparison.OrdinalIgnoreCase)))
                    throw new FormatException("Source file names differ only in case: " + file);
                files.Add(file);
                if (previousFile == null)
                {
                    candidate.AddKey(key, file);
                    changes.Add(new CsvImportChange(CsvChangeKind.AddKey, key, null, null, file));
                }
                if (description)
                {
                    string value = fields[2];
                    if (value.Length == 0 && policy == CsvEmptyCellPolicy.Skip) skipped++;
                    else if ((candidate.GetDescription(key) ?? "") != value)
                    {
                        changes.Add(new CsvImportChange(CsvChangeKind.Description, key, null, candidate.GetDescription(key), value));
                        candidate.SetDescription(key, value);
                    }
                }
                for (int column = 0; column < languages.Count; column++)
                {
                    string language = languages[column], value = fields[firstLanguage + column];
                    if (!candidate.LoadedLanguages.Contains(language))
                    {
                        candidate.EnsureLanguage(language);
                        newLanguages.Add(language);
                        changes.Add(new CsvImportChange(CsvChangeKind.AddLanguage, null, language, null, language));
                    }
                    if (value.Length == 0 && policy == CsvEmptyCellPolicy.Skip) { skipped++; continue; }
                    if ((candidate.GetTranslation(key, language) ?? "") == value) continue;
                    changes.Add(new CsvImportChange(CsvChangeKind.Translation, key, language, candidate.GetTranslation(key, language), value));
                    candidate.SetTranslation(key, language, value);
                }
                imported++;
            }
            return new CsvImportPlan(data, candidate, changes, newLanguages, imported, skipped);
        }
    }
}
