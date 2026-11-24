#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using SimplyLocalize.Data;
using UnityEngine;
using UnityEngine.Profiling;

namespace SimplyLocalize.HorrorLab
{
    /// <summary>Internal fresh-Player probe. No resource sweep, native unload or forced GC.</summary>
    public sealed class ScopedResourcesProbe : MonoBehaviour
    {
        [Serializable] private sealed class Report
        {
            public bool passed;
            public string failure, unityVersion, graphics;
            public int checks, neighborsBefore, neighborsAfterPreparation, neighborsAfterClose, selectedTables;
            public int largeTextureWidth, largeTextureHeight;
            public long largeTextureRuntimeBytes;
            public double preparationMilliseconds;
        }
        private readonly Report report = new();
        private bool finished;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Launch()
        {
            if (!Environment.GetCommandLineArgs().Contains("--scoped-resources-probe")) return;
            var obj = new GameObject("Scoped Resources Probe"); DontDestroyOnLoad(obj); obj.AddComponent<ScopedResourcesProbe>();
        }
        private void Update()
        {
            if (!finished && Time.realtimeSinceStartup > 90) { report.failure = "Probe timed out."; Finish(); }
        }
        private void Check(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); report.checks++; }
        private static int Neighbors() => Resources.FindObjectsOfTypeAll<Texture2D>().Count(t => t.name == "SLScoped_LargeNeighbor")
            + Resources.FindObjectsOfTypeAll<LocalizationAssetTable>().Count(t => t.name == "SLScoped_NeighborTable");
        private async void Start()
        {
            report.unityVersion = Application.unityVersion; report.graphics = SystemInfo.graphicsDeviceType.ToString();
            var config = LocalizationProjectConfig.Create(new[] { "en", "ru" });
            config.Sets[0].AddEntry("probe"); config.Sets[0].AddEntry("picture", LocalizationEntryKind.Asset);
            foreach (string code in new[] { "en", "ru" })
                config.Sets[0].SetSources(code,
                    new LocalizationSourceReference("resources", "ScopedResourcesProbe/" + code + "/selected", LocalizationSourceKind.TextTable),
                    new LocalizationSourceReference("resources", "ScopedResourcesProbe/" + code + "/SLScoped_SelectedTable", LocalizationSourceKind.AssetTable));
            using var first = new LocalizationRuntime(config);
            using var second = new LocalizationRuntime(config);
            try
            {
                report.neighborsBefore = Neighbors(); Check(report.neighborsBefore == 0, "Neighbor fixture was already loaded at startup.");
                Check(!Resources.FindObjectsOfTypeAll<LocalizationAssetTable>().Any(t => t.name == "SLScoped_SelectedTable"), "Selected fixture was already loaded at startup.");
                var timer = System.Diagnostics.Stopwatch.StartNew();
                await first.InitializeAsync("ru");
                report.preparationMilliseconds = timer.Elapsed.TotalMilliseconds;
                report.neighborsAfterPreparation = Neighbors(); Check(report.neighborsAfterPreparation == 0, "Scope loaded its neighbor.");
                report.selectedTables = Resources.FindObjectsOfTypeAll<LocalizationAssetTable>().Count(t => t.name == "SLScoped_SelectedTable");
                Check(report.selectedTables == 2, "Expected only en/ru selected tables.");
                Check(first.GetChannel().TryGetAsset<Texture2D>("picture", out var picture) && picture.Language == "en", "Asset fallback failed.");
                Check(first.GetChannel().TryGetText("probe", out var text) && text.Language == "en", "Text fallback failed.");
                Check(first.GetAsset<Texture2D>("neighbor") == null, "Unselected key leaked into scope.");
                await second.InitializeAsync("en");
                Check(second.GetAsset<Texture2D>("picture") == picture.Asset, "Shared Unity object identity changed.");
                await first.ReleaseAsync();
                Check(second.GetAsset<Texture2D>("picture") != null, "First runtime close destroyed a borrowed object.");
                for (int i = 0; i < 12; i++)
                {
                    await second.SwitchLanguageAsync(i % 2 == 0 ? "ru" : "en");
                    Check(second.GetAsset<Texture2D>("picture") == picture.Asset, "Repeated scoped switching lost shared data.");
                }
                using (var closing = new LocalizationRuntime(config))
                {
                    var pending = closing.InitializeAsync("ru"); await closing.ReleaseAsync();
                    try { await pending; throw new InvalidOperationException("Closed runtime published pending data."); }
                    catch (OperationCanceledException) { }
                    Check(closing.IsReleased && !closing.IsInitialized, "Close did not drain pending wait.");
                }
                await second.ReleaseAsync();
                Check(picture.Asset != null, "Second runtime close destroyed borrowed data.");
                report.neighborsAfterClose = Neighbors(); Check(report.neighborsAfterClose == 0, "Neighbor appeared before positive control.");
                // Positive control happens LAST: prove the large neighbor exists in this build and can be loaded.
                var neighbor = Resources.Load<LocalizationAssetTable>("ScopedResourcesProbe/en/SLScoped_NeighborTable");
                Check(neighbor != null, "Missing positive-control table.");
                var large = neighbor.Get<Texture2D>("neighbor");
                Check(large != null && large.width == 2048 && large.height == 2048, "Missing large neighbor dependency.");
                report.largeTextureWidth = large.width; report.largeTextureHeight = large.height;
                report.largeTextureRuntimeBytes = Profiler.GetRuntimeMemorySizeLong(large);
                Check(Neighbors() > 0, "Positive control did not register in the loaded object scan.");
                report.passed = true;
            }
            catch (Exception error) { report.failure = error.ToString(); }
            finally
            {
                await Task.WhenAll(first.ReleaseAsync(), second.ReleaseAsync());
                Destroy(config); Finish();
            }
        }
        private void Finish()
        {
            if (finished) return; finished = true;
            var args = Environment.GetCommandLineArgs(); int index = Array.IndexOf(args, "--scoped-resources-report");
            string path = index >= 0 && index + 1 < args.Length ? args[index + 1] : Path.Combine(Application.persistentDataPath, "scoped-resources.json");
            var json = JObject.Parse(JsonUtility.ToJson(report));
            var identity = Resources.Load<TextAsset>("NightShift/BuildIdentity");
            if (identity != null) json["buildIdentity"] = JObject.Parse(identity.text);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))); File.WriteAllText(path, json.ToString());
            Application.Quit(report.passed ? 0 : 1);
        }
    }
}
#endif
