using System.Collections.Generic;

namespace SimplyLocalize.TextProcessing
{
    /// <summary>
    /// Parses translation strings containing tokens into reusable ParsedTemplate objects.
    ///
    /// Supported token syntax:
    ///
    ///   {0}                              — indexed parameter, simple substitution
    ///   {playerName}                     — named parameter, simple substitution
    ///   {0|coin|coins}                   — indexed + plural forms (English: 2 forms)
    ///   {0|монету|монеты|монет}          — indexed + plural forms (Russian: 3 forms)
    ///   {count|apple|apples}             — named + plural forms
    ///   {gender|He|She}                  — named + selector (form chosen by integer value)
    ///   {class|Warrior|Mage|Rogue}       — named + selector (any number of forms)
    ///
    /// The distinction between indexed and named is automatic:
    ///   - If the identifier is a valid integer → indexed parameter
    ///   - Otherwise → named parameter
    ///
    /// Pluralization and selection use the same syntax: {identifier|form0|form1|form2...}
    /// For numeric values, the language's plural rule picks the form.
    /// For non-numeric values, the integer value selects the form directly (0-based index).
    ///
    /// Escaped braces: {{ → literal {, }} → literal }
    /// </summary>
    public static class TokenParser
    {
        /// <summary>
        /// Parses a raw translation string into a ParsedTemplate.
        /// Returns null if the input is null or empty.
        /// </summary>
        public static ParsedTemplate Parse(string rawText)
        {
            if (string.IsNullOrEmpty(rawText))
                return null;

            var tokens = new List<Token>(4);
            int pos = 0;
            int length = rawText.Length;
            int textStart = 0;

            while (pos < length)
            {
                if (rawText[pos] == '{')
                {
                    // Escaped brace: {{
                    if (pos + 1 < length && rawText[pos + 1] == '{')
                    {
                        if (pos > textStart)
                            tokens.Add(Token.MakeText(rawText.Substring(textStart, pos - textStart)));

                        tokens.Add(Token.MakeText("{"));
                        pos += 2;
                        textStart = pos;
                        continue;
                    }

                    // Flush preceding text
                    if (pos > textStart)
                        tokens.Add(Token.MakeText(rawText.Substring(textStart, pos - textStart)));

                    // Find closing brace
                    int closeIndex = rawText.IndexOf('}', pos + 1);

                    if (closeIndex == -1)
                    {
                        // No closing brace — treat rest as text
                        tokens.Add(Token.MakeText(rawText.Substring(pos)));
                        pos = length;
                        textStart = length;
                        break;
                    }

                    string content = rawText.Substring(pos + 1, closeIndex - pos - 1);
                    tokens.Add(ParseTokenContent(content));

                    pos = closeIndex + 1;
                    textStart = pos;
                }
                else if (rawText[pos] == '}')
                {
                    // Escaped closing brace: }}
                    if (pos + 1 < length && rawText[pos + 1] == '}')
                    {
                        if (pos > textStart)
                            tokens.Add(Token.MakeText(rawText.Substring(textStart, pos - textStart)));

                        tokens.Add(Token.MakeText("}"));
                        pos += 2;
                        textStart = pos;
                        continue;
                    }

                    pos++;
                }
                else
                {
                    pos++;
                }
            }

            // Flush remaining text
            if (textStart < length)
                tokens.Add(Token.MakeText(rawText.Substring(textStart)));

            return new ParsedTemplate(tokens);
        }

        private static Token ParseTokenContent(string content)
        {
            int pipeIndex = content.IndexOf('|');

            if (pipeIndex == -1)
            {
                // No pipe: simple parameter {0} or {playerName}
                string identifier = content.Trim();

                if (int.TryParse(identifier, out int index))
                    return Token.MakeIndexed(index);

                if (identifier.Length > 0)
                    return Token.MakeNamed(identifier);

                // Empty braces — treat as text
                return Token.MakeText("{}");
            }

            // Has pipe: parameter with forms
            string id = content.Substring(0, pipeIndex).Trim();
            string formsStr = content.Substring(pipeIndex + 1);
            string[] forms = SplitForms(formsStr);

            if (int.TryParse(id, out int paramIndex))
                return Token.MakeIndexed(paramIndex, forms);

            if (id.Length > 0)
                return Token.MakeNamed(id, forms);

            return Token.MakeText("{" + content + "}");
        }

        private static string[] SplitForms(string formsStr)
        {
            var forms = new List<string>(4);
            int start = 0;

            for (int i = 0; i <= formsStr.Length; i++)
            {
                if (i == formsStr.Length || formsStr[i] == '|')
                {
                    forms.Add(formsStr.Substring(start, i - start));
                    start = i + 1;
                }
            }

            return forms.ToArray();
        }
    }
}