using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SimplyLocalize.Editor.Data
{
    /// <summary>
    /// Writes translation dictionaries back to JSON format.
    /// Produces clean, human-readable JSON with sorted keys.
    /// Does not use Unity's JsonUtility (which doesn't support Dictionary).
    /// </summary>
    public static class EditorJsonWriter
    {
        /// <summary>
        /// Serializes a translation dictionary to a formatted JSON string.
        /// Keys are sorted alphabetically for clean diffs.
        /// </summary>
        public static string WriteTranslations(Dictionary<string, string> translations)
        {
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.Append("  \"translations\": {");

            var sortedKeys = translations.Keys.OrderBy(k => k).ToList();

            for (int i = 0; i < sortedKeys.Count; i++)
            {
                string key = sortedKeys[i];
                string value = translations[key];

                sb.AppendLine();
                sb.Append("    \"");
                sb.Append(EscapeJson(key));
                sb.Append("\": \"");
                sb.Append(EscapeJson(value));
                sb.Append('"');

                if (i < sortedKeys.Count - 1)
                    sb.Append(',');
            }

            if (sortedKeys.Count > 0)
                sb.AppendLine();

            sb.AppendLine("  }");
            sb.Append('}');

            return sb.ToString();
        }

        public static string EscapeJsonPublic(string value) => EscapeJson(value);

        private static string EscapeJson(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";

            var sb = new StringBuilder(value.Length);

            foreach (char c in value)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20)
                            sb.Append($"\\u{(int)c:X4}");
                        else
                            sb.Append(c);
                        break;
                }
            }

            return sb.ToString();
        }
    }
}