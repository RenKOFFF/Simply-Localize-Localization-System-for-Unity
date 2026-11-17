using System;
using System.IO;
using System.Linq;
using SimplyLocalize.Data;
using UnityEditor;

namespace SimplyLocalize.HorrorLab.Editor
{
    public static class HorrorLabChecks
    {
        public static string Data()
        {
            var project = AssetDatabase.LoadAssetAtPath<LocalizationProjectConfig>(HorrorArchitectureSetup.ConfigPath);
            if (project == null) throw new InvalidOperationException("Configure Night Shift channels first.");
            project.ValidateOrThrow();
            var definition = AssetDatabase.LoadAssetAtPath<HorrorWorldDefinition>(HorrorLabSetup.Root + "/Data/Precinct.asset");
            foreach (string setId in new[] { definition.voiceSetId, definition.visualSetId })
                if (!project.Sets.Any(s => s.Id == setId)) throw new InvalidOperationException("Unknown world set: " + setId);
            int sources = 0, variants = 0;
            foreach (var set in project.Sets)
            foreach (var language in set.Sources)
            foreach (var source in language.Sources)
            {
                string path = HorrorLabSetup.Root + "/Resources/" + source.Address;
                if (source.Kind == LocalizationSourceKind.TextTable)
                {
                    var text = LocalizationFileParser.Parse(File.ReadAllText(path + ".json"));
                    foreach (string key in text.Keys)
                        if (!set.Entries.Any(e => e.Key == key)) throw new InvalidOperationException("Unregistered key: " + key);
                }
                else
                {
                    var table = AssetDatabase.LoadAssetAtPath<LocalizationAssetTable>(path + ".asset");
                    if (table == null) throw new InvalidOperationException("Missing table: " + path);
                    foreach (var entry in table.Entries)
                    {
                        if (entry.asset == null) throw new InvalidOperationException("Missing asset: " + entry.key);
                        if (entry.asset is HorrorVisualVariant variant)
                        {
                            if (variant.texture == null || !project.Languages.Any(l => l.Code == variant.contentLanguage))
                                throw new InvalidOperationException("Invalid visual metadata: " + entry.key);
                            variants++;
                        }
                    }
                }
                sources++;
            }
            return "PASS: " + sources + " registered sources, " + variants + " visual metadata entries and world set identities. Runtime fallback, ownership and scene behavior require PlayMode/Player checks.";
        }
    }
}
