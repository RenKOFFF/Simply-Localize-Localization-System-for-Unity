using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SimplyLocalize.Editor.Data
{
    /// <summary>Editor-only codec for the existing key -> { description, previousKeys } format.</summary>
    internal static class EditorMetadataJson
    {
        internal static Dictionary<string, EditorLocalizationData.KeyMeta> Parse(string json)
        {
            try
            {
                using var input = new StringReader(json);
                using var reader = new JsonTextReader(input)
                {
                    DateParseHandling = DateParseHandling.None,
                    FloatParseHandling = FloatParseHandling.Decimal,
                    MaxDepth = 64,
                    SupportMultipleContent = true
                };
                var token = JToken.ReadFrom(reader, new JsonLoadSettings
                {
                    DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
                    LineInfoHandling = LineInfoHandling.Load
                });
                if (!(token is JObject root)) throw Invalid(token, "Expected a metadata object");
                if (reader.Read()) throw new FormatException($"Unexpected content after metadata at line {reader.LineNumber}, position {reader.LinePosition}.");

                var result = new Dictionary<string, EditorLocalizationData.KeyMeta>(StringComparer.Ordinal);
                foreach (var property in root.Properties())
                {
                    if (!(property.Value is JObject fields)) throw Invalid(property.Value, "Expected an object for the key");
                    var meta = new EditorLocalizationData.KeyMeta();
                    var description = fields["description"];
                    if (description != null && description.Type != JTokenType.Null)
                    {
                        if (description.Type != JTokenType.String) throw Invalid(description, "Expected a string or null");
                        meta.description = description.Value<string>();
                    }
                    var previous = fields["previousKeys"];
                    if (previous != null && previous.Type != JTokenType.Null)
                    {
                        if (!(previous is JArray history)) throw Invalid(previous, "Expected an array of strings or null");
                        meta.previousKeys = new List<string>();
                        foreach (var entry in history)
                        {
                            if (entry.Type != JTokenType.String) throw Invalid(entry, "Expected a string in previousKeys");
                            meta.previousKeys.Add(entry.Value<string>());
                        }
                    }
                    // Preserve extension fields, including for metadata-only keys not in translations.
                    meta.AdditionalFields = (JObject)fields.DeepClone();
                    meta.AdditionalFields.Remove("description");
                    meta.AdditionalFields.Remove("previousKeys");
                    result.Add(property.Name, meta);
                }
                return result;
            }
            catch (JsonException error)
            {
                throw new FormatException("Invalid metadata JSON: " + error.Message, error);
            }
        }

        internal static string Serialize(IReadOnlyDictionary<string, EditorLocalizationData.KeyMeta> metadata)
        {
            var root = new JObject();
            foreach (var pair in metadata.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                var meta = pair.Value;
                if (meta == null) throw new FormatException($"Metadata for '{pair.Key}' must not be null.");
                if (meta.previousKeys != null && meta.previousKeys.Any(key => key == null))
                    throw new FormatException($"Metadata '{pair.Key}.previousKeys' must contain only strings, not null.");
                if (meta.IsEmpty) continue;
                var fields = meta.AdditionalFields != null ? (JObject)meta.AdditionalFields.DeepClone() : new JObject();
                if (!string.IsNullOrEmpty(meta.description)) fields["description"] = meta.description;
                if (meta.previousKeys is { Count: > 0 }) fields["previousKeys"] = new JArray(meta.previousKeys);
                root.Add(pair.Key, fields);
            }
            return root.HasValues ? root.ToString(Formatting.Indented) : null;
        }

        private static FormatException Invalid(JToken token, string message)
        {
            var line = (IJsonLineInfo)token;
            return new FormatException($"{message} at '{token.Path}', line {line.LineNumber}, position {line.LinePosition}.");
        }
    }
}
