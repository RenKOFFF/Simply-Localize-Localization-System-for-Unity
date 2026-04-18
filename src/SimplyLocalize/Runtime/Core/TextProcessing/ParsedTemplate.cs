using System.Collections.Generic;

namespace SimplyLocalize.TextProcessing
{
    /// <summary>
    /// A pre-parsed translation template.
    /// Parsed once from the raw translation string, then reused for formatting with different parameter values.
    /// </summary>
    public class ParsedTemplate
    {
        public readonly List<Token> Tokens;

        public ParsedTemplate(List<Token> tokens)
        {
            Tokens = tokens;
        }
    }

    public enum TokenType
    {
        /// <summary>Static text, output as-is.</summary>
        Text,

        /// <summary>Indexed parameter: {0}, {1}, etc. Optionally with plural forms: {0|form1|form2}.</summary>
        IndexedParam,

        /// <summary>Named parameter: {playerName}, {level}, etc. Optionally with plural forms: {count|form1|form2}.</summary>
        NamedParam
    }

    public struct Token
    {
        public TokenType Type;

        /// <summary>For Text tokens: the literal string content.</summary>
        public string Text;

        /// <summary>For IndexedParam tokens: the parameter index (0, 1, 2...).</summary>
        public int Index;

        /// <summary>For NamedParam tokens: the parameter name ("playerName", "count", "gender"...).</summary>
        public string Name;

        /// <summary>
        /// For IndexedParam and NamedParam with plural forms: the available forms.
        /// Null if no forms specified (simple substitution).
        ///
        /// When forms are present, the parameter's numeric value determines which form is used
        /// via the current language's plural rule.
        ///
        /// If the parameter is non-numeric (e.g. a string), the forms are selected by
        /// the parameter's integer value directly (0 = first form, 1 = second, etc.).
        /// This covers gender, status switches, and any other selector pattern.
        /// </summary>
        public string[] Forms;

        public bool HasForms => Forms != null && Forms.Length > 0;

        public static Token MakeText(string text) =>
            new() { Type = TokenType.Text, Text = text };

        public static Token MakeIndexed(int index, string[] forms = null) =>
            new() { Type = TokenType.IndexedParam, Index = index, Forms = forms };

        public static Token MakeNamed(string name, string[] forms = null) =>
            new() { Type = TokenType.NamedParam, Name = name, Forms = forms };
    }
}