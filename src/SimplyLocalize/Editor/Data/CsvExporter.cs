using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace SimplyLocalize.Editor.Data
{
    /// <summary>
    /// Imports and exports localization data as CSV files.
    /// Format: Key,File,en,ru,ja,...
    /// </summary>
    public static class CsvExporter
    {
        /// <summary>
        /// Exports all translation data to a CSV string.
        /// </summary>
        public static string Export(EditorLocalizationData data, List<string> languageCodes)
        {
            var sb = new StringBuilder();

            // Header: Key, File, Description, lang1, lang2, ...
            sb.Append("Key,File,Description");
            foreach (var lang in languageCodes)
                sb.Append(',').Append(CsvEscape(lang));
            sb.AppendLine();

            // Rows
            var sortedKeys = data.AllKeys.OrderBy(k => k).ToList();

            foreach (var key in sortedKeys)
            {
                sb.Append(CsvEscape(key));
                sb.Append(',');
                sb.Append(CsvEscape(data.GetFileForKey(key) ?? ""));
                sb.Append(',');
                sb.Append(CsvEscape(data.GetDescription(key) ?? ""));

                foreach (var lang in languageCodes)
                {
                    sb.Append(',');
                    string value = data.GetTranslation(key, lang) ?? "";
                    sb.Append(CsvEscape(value));
                }

                sb.AppendLine();
            }

            return sb.ToString();
        }

        /// <summary>
        /// Exports to a file on disk.
        /// </summary>
        public static void ExportToFile(EditorLocalizationData data, List<string> languageCodes, string filePath)
        {
            string csv = Export(data, languageCodes);
            using var transaction = new EditorFileTransaction();
            transaction.Add(filePath, EditorFileTransaction.Read(filePath), EditorLocalizationData.EncodeFile(csv));
            transaction.Execute();
        }

        /// <summary>
        /// Imports a CSV string and merges into the data.
        /// Returns the number of keys updated.
        /// </summary>
        public static int Import(EditorLocalizationData data, string csv) => Import(data, csv, CsvEmptyCellPolicy.Skip);

        public static int Import(EditorLocalizationData data, string csv, CsvEmptyCellPolicy emptyCells)
        {
            var plan = Preview(data, csv, emptyCells);
            plan.Apply(data);
            return plan.RowCount;
        }

        public static CsvImportPlan Preview(EditorLocalizationData data, string csv, CsvEmptyCellPolicy emptyCells = CsvEmptyCellPolicy.Skip)
            => CsvImportPlan.Create(data, csv, emptyCells);

        public static int ImportFromFile(EditorLocalizationData data, string filePath) => ImportFromFile(data, filePath, CsvEmptyCellPolicy.Skip);

        public static int ImportFromFile(EditorLocalizationData data, string filePath, CsvEmptyCellPolicy emptyCells)
        {
            if (!File.Exists(filePath)) return 0;
            return Import(data, File.ReadAllText(filePath, new UTF8Encoding(false, true)), emptyCells);
        }

        private static string CsvEscape(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";

            bool needsQuoting = value.Contains(',') || value.Contains('"')
                || value.Contains('\n') || value.Contains('\r');

            if (!needsQuoting)
                return value;

            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        internal static List<List<string>> ParseCsvLines(string csv)
        {
            if (csv == null) throw new ArgumentNullException(nameof(csv));
            if (csv.Length > 0 && csv[0] == '\uFEFF') csv = csv.Substring(1);
            var rows = new List<List<string>>();
            var row = new List<string>();
            var field = new StringBuilder();
            bool quoted = false, closedQuote = false, started = false;
            int line = 1;
            for (int i = 0; i < csv.Length; i++)
            {
                char c = csv[i];
                if (quoted)
                {
                    if (c == '"')
                    {
                        if (i + 1 < csv.Length && csv[i + 1] == '"') { field.Append('"'); i++; }
                        else { quoted = false; closedQuote = true; }
                    }
                    else { field.Append(c); if (c == '\n' || c == '\r' && (i + 1 == csv.Length || csv[i + 1] != '\n')) line++; }
                    continue;
                }
                if (c == ',' || c == '\r' || c == '\n')
                {
                    row.Add(field.ToString()); field.Clear(); closedQuote = false;
                    if (c == ',') { started = true; continue; }
                    if (started || row.Count > 1 || !string.IsNullOrWhiteSpace(row[0])) rows.Add(row);
                    row = new List<string>(); started = false;
                    if (c == '\r' && i + 1 < csv.Length && csv[i + 1] == '\n') i++;
                    line++; continue;
                }
                if (closedQuote) throw new FormatException($"CSV line {line}, field {row.Count + 1}: unexpected character after closing quote.");
                if (c == '"')
                {
                    if (field.Length != 0) throw new FormatException($"CSV line {line}, field {row.Count + 1}: quote inside an unquoted field.");
                    quoted = true;
                }
                else field.Append(c);
                started = true;
            }
            if (quoted) throw new FormatException($"CSV line {line}, field {row.Count + 1}: unterminated quoted field.");
            if (started || row.Count > 0)
            {
                row.Add(field.ToString());
                if (row.Count > 1 || !string.IsNullOrWhiteSpace(row[0])) rows.Add(row);
            }
            return rows;
        }
    }
}
