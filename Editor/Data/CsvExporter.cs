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

            // Header
            sb.Append("Key,File");
            foreach (var lang in languageCodes)
                sb.Append(',').Append(lang);
            sb.AppendLine();

            // Rows
            var sortedKeys = data.AllKeys.OrderBy(k => k).ToList();

            foreach (var key in sortedKeys)
            {
                sb.Append(CsvEscape(key));
                sb.Append(',');
                sb.Append(CsvEscape(data.GetFileForKey(key) ?? ""));

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
            File.WriteAllText(filePath, csv, Encoding.UTF8);
        }

        /// <summary>
        /// Imports a CSV string and merges into the data.
        /// Returns the number of keys updated.
        /// </summary>
        public static int Import(EditorLocalizationData data, string csv)
        {
            var lines = ParseCsvLines(csv);
            if (lines.Count < 2) return 0;

            var header = lines[0];
            if (header.Count < 3) return 0;

            // header[0] = "Key", header[1] = "File", header[2..] = language codes
            var languageCodes = new List<string>();
            for (int i = 2; i < header.Count; i++)
                languageCodes.Add(header[i].Trim());

            int updated = 0;

            for (int row = 1; row < lines.Count; row++)
            {
                var fields = lines[row];
                if (fields.Count < 3) continue;

                string key = fields[0].Trim();
                if (string.IsNullOrEmpty(key)) continue;

                string file = fields[1].Trim();

                // Ensure key exists
                if (data.GetFileForKey(key) == null)
                    data.AddKey(key, string.IsNullOrEmpty(file) ? "global" : file);

                for (int i = 0; i < languageCodes.Count && i + 2 < fields.Count; i++)
                {
                    string value = fields[i + 2];
                    data.SetTranslation(key, languageCodes[i], value);
                }

                updated++;
            }

            return updated;
        }

        /// <summary>
        /// Imports from a file on disk.
        /// </summary>
        public static int ImportFromFile(EditorLocalizationData data, string filePath)
        {
            if (!File.Exists(filePath)) return 0;
            string csv = File.ReadAllText(filePath, Encoding.UTF8);
            return Import(data, csv);
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

        private static List<List<string>> ParseCsvLines(string csv)
        {
            var result = new List<List<string>>();
            var currentLine = new List<string>();
            var currentField = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < csv.Length; i++)
            {
                char c = csv[i];

                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < csv.Length && csv[i + 1] == '"')
                        {
                            currentField.Append('"');
                            i++;
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        currentField.Append(c);
                    }
                }
                else
                {
                    if (c == '"')
                    {
                        inQuotes = true;
                    }
                    else if (c == ',')
                    {
                        currentLine.Add(currentField.ToString());
                        currentField.Clear();
                    }
                    else if (c == '\n')
                    {
                        currentLine.Add(currentField.ToString());
                        currentField.Clear();
                        result.Add(currentLine);
                        currentLine = new List<string>();
                    }
                    else if (c != '\r')
                    {
                        currentField.Append(c);
                    }
                }
            }

            // Last field/line
            if (currentField.Length > 0 || currentLine.Count > 0)
            {
                currentLine.Add(currentField.ToString());
                result.Add(currentLine);
            }

            return result;
        }
    }
}
