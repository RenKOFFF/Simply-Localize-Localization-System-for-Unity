using System.Text;

namespace SimplyLocalize.Editor.Utilities
{
    /// <summary>
    /// Colorizes localization parameters in translation strings using Unity rich text.
    /// 
    /// Examples:
    ///   "{0} coins"           → "<color=#5BA4CF>{0}</color> coins"
    ///   "{playerName} won"    → "<color=#5BA4CF>{playerName}</color> won"
    ///   "{gender|He|She} was" → "<color=#C78FD1>{gender|He|She}</color> was"
    ///   "{0|coin|coins}"      → "<color=#C78FD1>{0|coin|coins}</color>"
    /// </summary>
    public static class ParameterHighlighter
    {
        // Simple params: {0}, {name}
        private const string SimpleColor = "#5BA4CF";

        // Plural/gender params: {0|coin|coins}, {gender|He|She}
        private const string PluralColor = "#C78FD1";

        /// <summary>
        /// Returns the input string with all {param} tokens wrapped in rich text color tags.
        /// Safe to pass to EditorGUILayout.LabelField with a richText style.
        /// </summary>
        public static string Highlight(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            var sb = new StringBuilder(text.Length + 32);
            int i = 0;

            while (i < text.Length)
            {
                if (text[i] == '{' && i + 1 < text.Length && text[i + 1] != '{')
                {
                    int close = text.IndexOf('}', i + 1);

                    if (close > i)
                    {
                        string content = text.Substring(i + 1, close - i - 1);
                        bool hasPlural = content.Contains("|");
                        string color = hasPlural ? PluralColor : SimpleColor;

                        sb.Append("<color=");
                        sb.Append(color);
                        sb.Append(">{");
                        sb.Append(content);
                        sb.Append("}</color>");

                        i = close + 1;
                        continue;
                    }
                }

                // Escape < and > that aren't part of our tags (for safety in rich text)
                if (text[i] == '<')
                    sb.Append("&lt;");
                else if (text[i] == '>')
                    sb.Append("&gt;");
                else
                    sb.Append(text[i]);

                i++;
            }

            return sb.ToString();
        }
    }
}