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
        private static ScopedResourcesDataProvider Provider() => new(new[] { "en", "ru" }.Select(code =>
            new ResourcesLanguageSources(code, new[] { "ScopedResourcesProbe/" + code + "/selected" },
                new[] { "ScopedResourcesProbe/" + code + "/SLScoped_SelectedTable" })));
        private async void Start()
        {
            report.unityVersion = Application.unityVersion; report.graphics = SystemInfo.graphicsDeviceType.ToString();
            var config = ScriptableObject.CreateInstance<LocalizationConfig>();
            var en = ScriptableObject.CreateInstance<LanguageProfile>(); en.languageCode = "en";
            var ru = ScriptableObject.CreateInstance<LanguageProfile>(); ru.languageCode = "ru";
            config.languages.Add(en); config.languages.Add(ru); config.defaultLanguage = config.fallbackLanguage = en;
            using var provider = Provider(); using var secondProvider = Provider();
            var first = new LocalizationManager(config); first.SetDataProvider(provider);
            var second = new LocalizationManager(config); second.SetDataProvider(secondProvider);
            try
            {
                report.neighborsBefore = Neighbors(); Check(report.neighborsBefore == 0, "Neighbor fixture was already loaded at startup.");
                Check(!Resources.FindObjectsOfTypeAll<LocalizationAssetTable>().Any(t => t.name == "SLScoped_SelectedTable"), "Selected fixture was already loaded at startup.");
                var timer = System.Diagnostics.Stopwatch.StartNew();
                using (var prepared = await first.PrepareLanguageAsync("ru")) first.ApplyPreparedLanguage(prepared);
                report.preparationMilliseconds = timer.Elapsed.TotalMilliseconds;
                report.neighborsAfterPreparation = Neighbors(); Check(report.neighborsAfterPreparation == 0, "Scope loaded its neighbor.");
                report.selectedTables = Resources.FindObjectsOfTypeAll<LocalizationAssetTable>().Count(t => t.name == "SLScoped_SelectedTable");
                Check(report.selectedTables == 2, "Expected only en/ru selected tables.");
                var picture = first.ResolvePreparedAsset<Texture2D>("picture");
                Check(picture.Status == LocalizationResolveStatus.Found && picture.ResolvedLanguage == "en", "Asset fallback failed.");
                Check(first.ResolvePreparedText("probe").ResolvedLanguage == "en", "Text fallback failed.");
                Check(first.ResolvePreparedAsset<Texture2D>("neighbor").Status == LocalizationResolveStatus.Missing, "Unselected key leaked into scope.");
                using (var prepared = await second.PrepareLanguageAsync("en")) second.ApplyPreparedLanguage(prepared);
                Check(second.ResolvePreparedAsset<Texture2D>("picture").Value == picture.Value, "Shared Unity object identity changed.");
                provider.Dispose();
                Check(first.ResolvePreparedAsset<Texture2D>("picture").Value != null, "Provider close destroyed an applied object.");
                for (int i = 0; i < 12; i++)
                {
                    using var prepared = await second.PrepareLanguageAsync(i % 2 == 0 ? "ru" : "en");
                    second.ApplyPreparedLanguage(prepared);
                    Check(second.ResolvePreparedAsset<Texture2D>("picture").Value == picture.Value, "Repeated scoped switching lost shared data.");
                }
                using (var closing = Provider())
                {
                    var pendingManager = new LocalizationManager(config); pendingManager.SetDataProvider(closing);
                    var pending = pendingManager.PrepareLanguageAsync("ru"); closing.Dispose();
                    try { using var unexpected = await pending; throw new InvalidOperationException("Closed provider published pending data."); }
                    catch (OperationCanceledException) { }
                    Check(pendingManager.CurrentLanguage == null && closing.PendingLoadCount == 0, "Close did not drain pending wait.");
                }
                secondProvider.Dispose();
                Check(second.ResolvePreparedAsset<Texture2D>("picture").Value != null, "Second provider close destroyed borrowed data.");
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
                first.ClearCaches(); second.ClearCaches();
                Destroy(config); Destroy(en); Destroy(ru); Finish();
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
