using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SimplyLocalize.Data
{
    /// <summary>
    /// Minimal JSON parser for localization files.
    /// Parses the "translations" dictionary from a JSON string without external dependencies.
    ///
    /// Expected file format:
    /// {
    ///   "translations": {
    ///     "key": "value",
    ///     "other/key": "other value with {0} params"
    ///   }
    /// }
    /// </summary>
    public static class LocalizationFileParser
    {
        /// <summary>
        /// Parses a localization JSON file and returns the translations dictionary.
        /// </summary>
        public static Dictionary<string, string> Parse(string json)
        {
            if (string.IsNullOrEmpty(json))
                return new Dictionary<string, string>();

            var parser = new JsonTokenizer(json);
            var root = parser.ParseObject();

            if (root.TryGetValue("translations", out var translationsObj)
                && translationsObj is Dictionary<string, object> translations)
            {
                var result = new Dictionary<string, string>(translations.Count);

                foreach (var kvp in translations)
                {
                    if (kvp.Value is string strValue)
                        result[kvp.Key] = strValue;
                    else if (kvp.Value != null)
                        result[kvp.Key] = kvp.Value.ToString();
                }

                return result;
            }

            return new Dictionary<string, string>();
        }

        private class JsonTokenizer
        {
            private readonly string _json;
            private int _pos;

            public JsonTokenizer(string json)
            {
                _json = json;
                _pos = 0;
            }

            public Dictionary<string, object> ParseObject()
            {
                SkipWhitespace();
                Expect('{');

                var dict = new Dictionary<string, object>();
                SkipWhitespace();

                if (Peek() == '}')
                {
                    _pos++;
                    return dict;
                }

                while (true)
                {
                    SkipWhitespace();
                    string key = ParseString();
                    SkipWhitespace();
                    Expect(':');
                    SkipWhitespace();
                    object value = ParseValue();
                    dict[key] = value;
                    SkipWhitespace();

                    if (Peek() == ',')
                    {
                        _pos++;
                        continue;
                    }

                    break;
                }

                SkipWhitespace();
                Expect('}');
                return dict;
            }

            private object ParseValue()
            {
                SkipWhitespace();
                char c = Peek();

                return c switch
                {
                    '"' => ParseString(),
                    '{' => ParseObject(),
                    '[' => ParseArray(),
                    't' or 'f' => ParseBool(),
                    'n' => ParseNull(),
                    _ => ParseNumber()
                };
            }

            private string ParseString()
            {
                Expect('"');
                var sb = new StringBuilder();

                while (_pos < _json.Length)
                {
                    char c = _json[_pos++];

                    if (c == '"')
                        return sb.ToString();

                    if (c == '\\')
                    {
                        if (_pos >= _json.Length)
                            throw new FormatException("Unexpected end of string escape");

                        char escaped = _json[_pos++];

                        switch (escaped)
                        {
                            case '"': sb.Append('"'); break;
                            case '\\': sb.Append('\\'); break;
                            case '/': sb.Append('/'); break;
                            case 'b': sb.Append('\b'); break;
                            case 'f': sb.Append('\f'); break;
                            case 'n': sb.Append('\n'); break;
                            case 'r': sb.Append('\r'); break;
                            case 't': sb.Append('\t'); break;
                            case 'u':
                                if (_pos + 4 > _json.Length)
                                    throw new FormatException("Invalid unicode escape");
                                string hex = _json.Substring(_pos, 4);
                                sb.Append((char)int.Parse(hex, NumberStyles.HexNumber));
                                _pos += 4;
                                break;
                            default:
                                sb.Append(escaped);
                                break;
                        }
                    }
                    else
                    {
                        sb.Append(c);
                    }
                }

                throw new FormatException("Unterminated string");
            }

            private List<object> ParseArray()
            {
                Expect('[');
                var list = new List<object>();
                SkipWhitespace();

                if (Peek() == ']')
                {
                    _pos++;
                    return list;
                }

                while (true)
                {
                    SkipWhitespace();
                    list.Add(ParseValue());
                    SkipWhitespace();

                    if (Peek() == ',')
                    {
                        _pos++;
                        continue;
                    }

                    break;
                }

                SkipWhitespace();
                Expect(']');
                return list;
            }

            private object ParseNumber()
            {
                int start = _pos;

                while (_pos < _json.Length && IsNumberChar(_json[_pos]))
                    _pos++;

                string numStr = _json.Substring(start, _pos - start);

                if (numStr.Contains('.') || numStr.Contains('e') || numStr.Contains('E'))
                    return double.Parse(numStr, CultureInfo.InvariantCulture);

                return long.Parse(numStr, CultureInfo.InvariantCulture);
            }

            private bool ParseBool()
            {
                if (_json.Substring(_pos, 4) == "true")
                {
                    _pos += 4;
                    return true;
                }

                if (_json.Substring(_pos, 5) == "false")
                {
                    _pos += 5;
                    return false;
                }

                throw new FormatException($"Expected bool at position {_pos}");
            }

            private object ParseNull()
            {
                if (_json.Substring(_pos, 4) == "null")
                {
                    _pos += 4;
                    return null;
                }

                throw new FormatException($"Expected null at position {_pos}");
            }

            private void SkipWhitespace()
            {
                while (_pos < _json.Length && char.IsWhiteSpace(_json[_pos]))
                    _pos++;
            }

            private char Peek()
            {
                if (_pos >= _json.Length)
                    throw new FormatException("Unexpected end of JSON");
                return _json[_pos];
            }

            private void Expect(char expected)
            {
                if (_pos >= _json.Length || _json[_pos] != expected)
                    throw new FormatException(
                        $"Expected '{expected}' at position {_pos}, got '{(_pos < _json.Length ? _json[_pos] : '?')}'");
                _pos++;
            }

            private static bool IsNumberChar(char c) =>
                c is >= '0' and <= '9' or '-' or '+' or '.' or 'e' or 'E';
        }
    }
}
