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
    ///   {gender|He|She}                  — named + selector when passed a FormSelector value
    ///   {class|Warrior|Mage|Rogue}       — named + selector when passed a FormSelector value
    ///
    /// The distinction between indexed and named is automatic:
    ///   - If the identifier is a valid integer → indexed parameter
    ///   - Otherwise → named parameter
    ///
    /// Pluralization and selection use the same syntax: {identifier|form0|form1|form2...}
    /// For numeric values, the language's plural rule picks the form.
    /// For FormSelector values, Index selects the form directly (0-based).
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
                    int closeIndex = FindClosingBrace(rawText, pos);

                    if (closeIndex == -1)
                    {
                        // No closing brace — treat rest as text
                        tokens.Add(Token.MakeText(rawText.Substring(pos)));
                        pos = length;
                        textStart = length;
                        break;
                    }

                    string content = rawText.Substring(pos + 1, closeIndex - pos - 1);
                    var token = ParseTokenContent(content);
                    token.RawText = rawText.Substring(pos, closeIndex - pos + 1);
                    tokens.Add(token);

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
            // Nested expressions are not part of this syntax. Preserve them as literal text;
            // escaped braces within form text are supported and decoded exactly once.
            int pipeIndex = content.IndexOf('|');

            if (pipeIndex == -1)
            {
                // No pipe: simple parameter {0} or {playerName}
                string identifier = content.Trim();

                if (identifier.IndexOfAny(new[] { '{', '}' }) >= 0)
                    return Token.MakeText("{" + content + "}");

                if (int.TryParse(identifier, out int index))
                    return Token.MakeIndexed(index);

                if (identifier.Length > 0)
                    return Token.MakeNamed(identifier);

                // Empty braces — treat as text
                return Token.MakeText("{" + content + "}");
            }

            // Has pipe: parameter with forms
            string id = content.Substring(0, pipeIndex).Trim();
            string formsStr = content.Substring(pipeIndex + 1);
            if (id.IndexOfAny(new[] { '{', '}' }) >= 0 || !HasOnlyEscapedBraces(formsStr))
                return Token.MakeText("{" + content + "}");
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
                    forms.Add(formsStr.Substring(start, i - start).Replace("{{", "{").Replace("}}", "}"));
                    start = i + 1;
                }
            }

            return forms.ToArray();
        }

        private static int FindClosingBrace(string text, int start)
        {
            bool hasForms = false;
            int escapedClose = -1;
            for (int i = start + 1; i < text.Length; i++)
            {
                if (text[i] == '|') hasForms = true;
                else if (text[i] == '{')
                {
                    if (hasForms && i + 1 < text.Length && text[i + 1] == '{') { i++; continue; }
                    // Unsupported nested expression: consume its entire balanced block as text.
                    return FindBalancedClosingBrace(text, start);
                }
                else if (text[i] == '}')
                {
                    if (hasForms && i + 1 < text.Length && text[i + 1] == '}'
                        && (i + 2 >= text.Length || text[i + 2] != '}'))
                    {
                        if (escapedClose < 0) escapedClose = i;
                        i++;
                        continue;
                    }
                    // A run of three closes the token first, preserving {0|one|other}}}.
                    return i;
                }
            }
            // Preserve the legacy meaning of a token followed by an unmatched literal }.
            return escapedClose;
        }

        private static int FindBalancedClosingBrace(string text, int start)
        {
            int depth = 1;
            for (int i = start + 1; i < text.Length; i++)
            {
                if (text[i] == '{') depth++;
                else if (text[i] == '}' && --depth == 0) return i;
            }
            return -1;
        }

        private static bool HasOnlyEscapedBraces(string text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] != '{' && text[i] != '}') continue;
                if (i + 1 >= text.Length || text[i + 1] != text[i]) return false;
                i++;
            }
            return true;
        }
    }
}
