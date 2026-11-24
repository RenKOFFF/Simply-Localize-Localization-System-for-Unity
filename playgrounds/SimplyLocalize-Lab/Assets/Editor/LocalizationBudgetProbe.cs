using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.TextCore.LowLevel;
using Object = UnityEngine.Object;

namespace SimplyLocalize.Tests
{
    public static class LocalizationBudgetProbe
    {
        public static void Run()
        {
            string path = Environment.GetEnvironmentVariable("SL_BUDGET_REPORT") ?? "TestResults/localization-budgets.json";
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            var times = new JArray();
            foreach (int count in new[] { 100, 1000, 4000 })
                for (int repeat = 0; repeat < 3; repeat++) times.Add(Release(count));
            var report = new JObject { ["release"] = times };
            File.WriteAllText(path, report.ToString());
            string fonts = Environment.GetEnvironmentVariable("SL_BUDGET_FONTS");
            if (string.IsNullOrEmpty(fonts)) return;
            var atlases = new JArray();
            foreach (string file in fonts.Split(Path.PathSeparator)) atlases.Add(Atlas(file));
            report["fonts"] = atlases;
            File.WriteAllText(path, report.ToString());
        }

        private static JObject Release(int count)
        {
            // Isolate registry bookkeeping from disk IO and driver/native texture destruction.
            var type = typeof(LocalizationRuntime).Assembly.GetType("SimplyLocalize.LocalizationResourceLease", true);
            var flags = BindingFlags.NonPublic | BindingFlags.Instance;
            var lease = Activator.CreateInstance(type, flags, null, new object[] { false, 1L, "Scale", "en" }, null);
            var track = (Action<string, string, Object>)type.GetMethod("TrackLoaded", flags)
                .CreateDelegate(typeof(Action<string, string, Object>), lease);
            var close = (Func<Task>)type.GetMethod("ReleaseAsync", flags).CreateDelegate(typeof(Func<Task>), lease);
            var roots = new List<Texture2D>();
            try
            {
                for (int i = 0; i < count; i++)
                {
                    var root = new Texture2D(1, 1);
                    roots.Add(root);
                    track("Scale/" + i, "en", root);
                }
                var time = Stopwatch.StartNew();
                close().GetAwaiter().GetResult();
                time.Stop();
                return new JObject { ["addresses"] = count, ["elapsedMs"] = time.Elapsed.TotalMilliseconds };
            }
            finally
            {
                close().GetAwaiter().GetResult();
                foreach (var root in roots) Object.DestroyImmediate(root);
            }
        }

        private static JObject Atlas(string file)
        {
            if (TMP_Settings.instance == null)
                throw new InvalidOperationException("Run the font probe in the playground with TMP Settings configured.");
            string root = "Assets/FontBudget_" + Guid.NewGuid().ToString("N");
            string path = root + "/" + Path.GetFileName(file);
            Directory.CreateDirectory(root);
            try
            {
                File.Copy(file, path);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                var font = AssetDatabase.LoadAssetAtPath<Font>(path);
                if (!font) throw new InvalidOperationException("Cannot import " + file);
                var results = new JArray();
                foreach (var sample in new[] { ("Latin", 0x41, 26), ("Cyrillic", 0x410, 64),
                    ("Hebrew", 0x5D0, 27), ("Arabic letters", 0x621, 26), ("CJK", 0x4e00, 1024), ("Hangul", 0xac00, 1024) })
                {
                    var asset = TMP_FontAsset.CreateFontAsset(font, 48, 5, GlyphRenderMode.SDFAA, 512, 512, AtlasPopulationMode.Dynamic, true);
                    try
                    {
                        var requested = Enumerable.Range(sample.Item2, sample.Item3).Select(i => (uint)i).ToArray();
                        asset.TryAddCharacters(requested, out uint[] missing);
                        var pages = asset.atlasTextures.Where(t => t != null && t.width > 0).ToArray();
                        results.Add(new JObject { ["script"] = sample.Item1, ["requested"] = requested.Length,
                            ["missing"] = missing?.Length ?? 0, ["pages"] = pages.Length,
                            ["textureBytes"] = pages.Sum(Profiler.GetRuntimeMemorySizeLong) });
                    }
                    finally { Object.DestroyImmediate(asset); }
                }
                return new JObject { ["font"] = Path.GetFileName(file), ["samples"] = results };
            }
            finally { AssetDatabase.DeleteAsset(root); }
        }
    }
}
