using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace SimplyLocalize.Editor.Data
{
    /// <summary>Translator notes keyed by entry identity. Stored under Editor, outside runtime Resources.</summary>
    public static class LocalizationProjectNotes
    {
        public static string GetAssetPath(LocalizationProjectConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(config));
            if (string.IsNullOrEmpty(guid)) throw new InvalidOperationException("Save the project config before using translator notes.");
            return "Assets/SimplyLocalize/Editor/Notes/" + guid + ".json";
        }
        public static IReadOnlyDictionary<string, string> Load(LocalizationProjectConfig config) =>
            new ReadOnlyDictionary<string, string>(new ProjectNotesDocument(config).Descriptions.Properties().ToDictionary(p => p.Name, p => (string)p.Value, StringComparer.Ordinal));
    }
    internal sealed class ProjectNotesDocument
    {
        internal readonly string Path;
        internal readonly byte[] Before;
        private readonly JObject original, root;
        private readonly Encoding encoding = new UTF8Encoding(false, true);
        private readonly bool bom;
        private readonly string newline = "\n";
        internal JObject Descriptions => (JObject)root["descriptions"];
        internal ProjectNotesDocument(LocalizationProjectConfig config)
        {
            Path = LocalizationProjectNotes.GetAssetPath(config); Before = EditorFileTransaction.Read(Path);
            if (Before == null) root = new JObject { ["descriptions"] = new JObject() };
            else
            {
                using var stream = new StreamReader(new MemoryStream(Before), encoding, true);
                string json = stream.ReadToEnd(); encoding = stream.CurrentEncoding; bom = encoding.GetPreamble().Length > 0 && Before.Take(encoding.GetPreamble().Length).SequenceEqual(encoding.GetPreamble());
                newline = json.Contains("\r\n") ? "\r\n" : "\n";
                using var input = new JsonTextReader(new StringReader(json)) { DateParseHandling = DateParseHandling.None, MaxDepth = 32 };
                root = JObject.Load(input, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                if (input.Read()) throw new FormatException("Trailing content in translator notes: " + Path);
                if (!(root["descriptions"] is JObject)) throw new FormatException("Expected descriptions in translator notes: " + Path);
                foreach (var property in Descriptions.Properties())
                    if (!Guid.TryParseExact(property.Name, "N", out var id) || id == Guid.Empty || id.ToString("N") != property.Name || property.Value.Type != JTokenType.String)
                        throw new FormatException("Expected entry IDs and string descriptions in " + Path);
            }
            original = (JObject)root.DeepClone();
        }
        internal string Get(string id) => (string)Descriptions[id] ?? "";
        internal void Set(string id, string value)
        { if (string.IsNullOrEmpty(value)) Descriptions.Property(id)?.Remove(); else Descriptions[id] = value; }
        internal byte[] Encode()
        {
            if (JToken.DeepEquals(root, original)) return Before;
            var output = new StringWriter { NewLine = newline };
            using (var writer = new JsonTextWriter(output) { Formatting = Formatting.Indented }) root.WriteTo(writer);
            byte[] bytes = encoding.GetBytes(output.ToString()); return bom ? encoding.GetPreamble().Concat(bytes).ToArray() : bytes;
        }
        internal void Reset()
        { root.RemoveAll(); foreach (var property in original.Properties()) root.Add(property.Name, property.Value.DeepClone()); }
    }
}
