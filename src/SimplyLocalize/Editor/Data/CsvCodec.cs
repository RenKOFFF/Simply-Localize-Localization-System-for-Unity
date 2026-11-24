using System;
using System.Collections.Generic;
using System.Text;

namespace SimplyLocalize.Editor.Data
{
    internal static class CsvCodec
    {
        internal static List<List<string>> ParseCsvLines(string csv, Action<float> progress = null)
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
                if ((i & 65535) == 0) progress?.Invoke((float)i / csv.Length);
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
