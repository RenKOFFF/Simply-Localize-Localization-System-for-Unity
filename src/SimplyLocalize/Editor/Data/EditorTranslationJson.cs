using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SimplyLocalize.Editor.Data
{
    /// <summary>Shared validation for viewing and editing translation documents. Validate structure
    /// and depth before the dependency-free runtime parser converts supported scalar values.</summary>
    internal static class EditorTranslationJson
    {
        internal static JObject Read(string json, string path)
        {
            try
            {
                using var input = new JsonTextReader(new StringReader(json))
                { DateParseHandling = DateParseHandling.None, FloatParseHandling = FloatParseHandling.Decimal, MaxDepth = 64 };
                var root = JObject.Load(input, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                if (input.Read()) throw new FormatException("Trailing JSON content at " + path);
                if (!(root["translations"] is JObject)) throw new FormatException("Expected a translations object at " + path);
                return root;
            }
            catch (JsonException error) { throw new FormatException("Invalid translation file '" + path + "': " + error.Message, error); }
        }
    }
}
