using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SimplyLocalize.Editor.Data;
using UnityEditor;
using UnityEngine;

namespace SimplyLocalize.Tests
{
    // Batch entry point for an isolated project with the local UPM package.
    public static class CsvScaleProbe
    {
        public static void Run()
        {
            var report = new JArray();
            string output = Environment.GetEnvironmentVariable("SL_SCALE_REPORT") ?? "TestResults/csv-scale.json";
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
            foreach (int size in new[] { 1000, 10000, 100000 })
            {
                string root = "Assets/CsvScale_" + Guid.NewGuid().ToString("N");
                try { report.Add(Measure(root, size)); }
                finally { AssetDatabase.DeleteAsset(root); }
                File.WriteAllText(output, report.ToString());
            }
        }

        private static JObject Measure(string root, int count)
        {
            string[] languages = { "en", "ru", "pl" };
            bool initialImport = Environment.GetEnvironmentVariable("SL_SCALE_NEW") == "1";
            var config = LocalizationProjectConfig.Create(languages);
            var set = config.Sets[0];
            string folder = root + "/Resources/" + root.Substring("Assets/".Length);
            Directory.CreateDirectory(folder);
            foreach (string language in languages)
                set.SetSources(language, new LocalizationSourceReference("resources",
                    root.Substring("Assets/".Length) + "/" + language, LocalizationSourceKind.TextTable));

            // Bulk authoring keeps fixture construction outside the measured import.
            var entries = (System.Collections.Generic.List<LocalizationEntryDefinition>)typeof(LocalizationContentSet)
                .GetField("_entries", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(set);
            var tables = languages.Select(_ => new JObject()).ToArray();
            var input = new StringBuilder("EntryId,Key,en,ru,pl\n");
            int expectedChanges = 0;
            for (int i = 0; i < count; i++)
            {
                string key = "Log/" + i, id = (i + 1).ToString("x32");
                if (!initialImport) entries.Add(new LocalizationEntryDefinition(key, LocalizationEntryKind.Text, id));
                input.Append(id).Append(',').Append(key);
                for (int j = 0; j < languages.Length; j++)
                {
                    string value = languages[j] + " line " + i;
                    if (!initialImport) tables[j][key] = value;
                    bool update = j == 1 && i < 80;
                    if (update) expectedChanges++;
                    input.Append(',').Append(update ? value + " updated" : value);
                }
                input.AppendLine();
            }
            for (int j = 0; j < languages.Length; j++)
                File.WriteAllText(folder + "/" + languages[j] + ".json", new JObject { ["translations"] = tables[j] }.ToString(Formatting.None));
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.CreateAsset(config, root + "/Project.asset");
            AssetDatabase.SaveAssetIfDirty(config);
            if (initialImport) expectedChanges = count * (1 + languages.Length);
            string csv = input.ToString();
            GC.Collect();
            long start = GC.GetTotalMemory(false), peak = start;
            using var done = new ManualResetEvent(false);
            var sampler = new Thread(() => {
                while (!done.WaitOne(25))
                {
                    peak = Math.Max(peak, GC.GetTotalMemory(false));
                }
            }) { IsBackground = true };
            sampler.Start();
            var timer = Stopwatch.StartNew();
            string outcome = "completed";
            long applyMs = -1;
            ProjectCsvImportPlan plan = null;
            try
            {
                plan = ProjectCsv.Preview(config, csv);
                if (plan.RowCount != count || plan.Changes.Count != expectedChanges)
                    throw new InvalidOperationException("Unexpected preview for " + count + " rows.");
            }
            catch (TimeoutException) { outcome = "time limit"; }
            finally { timer.Stop(); done.Set(); sampler.Join(); }
            using (plan)
            {
                if (plan != null && Environment.GetEnvironmentVariable("SL_SCALE_APPLY") == "1")
                {
                    var save = Stopwatch.StartNew();
                    plan.Apply();
                    applyMs = save.ElapsedMilliseconds;
                    if (config.Sets[0].Entries.Count != count) throw new InvalidOperationException("Saved keys differ.");
                    var saved = (JObject)JObject.Parse(File.ReadAllText(folder + "/ru.json"))["translations"];
                    for (int i = 0; i < count; i++)
                        if ((string)saved["Log/" + i] != "ru line " + i + (i < 80 ? " updated" : ""))
                            throw new InvalidOperationException("Saved translation mismatch at " + i);
                }
            }
            return new JObject { ["initialImport"] = initialImport, ["applyElapsedMs"] = applyMs, ["rows"] = count, ["result"] = outcome, ["mainThreadElapsedMs"] = timer.ElapsedMilliseconds,
                ["managedStartBytes"] = start, ["managedSampledPeakBytes"] = peak, ["expectedChanges"] = expectedChanges };
        }
    }
}
