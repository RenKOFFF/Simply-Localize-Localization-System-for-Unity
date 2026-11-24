#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using SimplyLocalize.Data;
using UnityEngine;
using UnityEngine.Profiling;
using Unity.Profiling;

namespace SimplyLocalize.HorrorLab
{
    /// <summary>Fresh Player verification of real native release. No native cleanup exists in this probe.</summary>
    public sealed class ContextLifecycleProbe : MonoBehaviour
    {
        private readonly JObject report = new();
        private int checks;
        private bool finished;
        private bool measuring;
        private long peakAllocated, peakMono, peakMainThreadNs;
        private ProfilerRecorder mainThread;

        private void LateUpdate()
        {
            if (!measuring) return;
            peakAllocated = Math.Max(peakAllocated, Profiler.GetTotalAllocatedMemoryLong());
            peakMono = Math.Max(peakMono, Profiler.GetMonoUsedSizeLong());
            if (mainThread.Valid) peakMainThreadNs = Math.Max(peakMainThreadNs, mainThread.LastValue);
        }

        private async Task MeasureOptionalSet(LocalizationProjectConfig config, LocalizationSourceRegistry sources, LocalizationRuntimeOptions options)
        {
            var rows = new JArray();
            string id = config.Sets[0].Id;
            config.MainChannel.SetStartup(id, false);
            using var runtime = new LocalizationRuntime(config, sources, options);
            await runtime.InitializeAsync("en");
            mainThread = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread");
            measuring = true;
            try
            {
                for (int cycle = 0; cycle < 20; cycle++)
                {
                    await runtime.SwitchLanguageAsync("en");
                    await Task.Yield();
                    Sample("before", cycle, 0, "");
                    var lease = await runtime.AcquireSetAsync(null, id);
                    try
                    {
                        await Task.Yield();
                        Sample("en", cycle, 3, "en");
                        await runtime.SwitchLanguageAsync("ru");
                        await Task.Yield();
                        Sample("ru+en fallback", cycle, 6, "en,ru");
                    }
                    finally { await lease.ReleaseAsync(); }
                    await Task.Yield();
                    await Task.Yield();
                    Sample("released", cycle, 0, "");
                }
            }
            finally
            {
                measuring = false;
                mainThread.Dispose();
                report["memorySamples"] = rows;
                report["peakAllocatedBytes"] = peakAllocated;
                report["peakMonoUsedBytes"] = peakMono;
                report["peakMainThreadNs"] = peakMainThreadNs;
            }

            void Sample(string phase, int cycle, int expectedClips, string expectedLanguages)
            {
                var clips = Resources.FindObjectsOfTypeAll<AudioClip>();
                var tracked = LocalizationResourceDiagnostics.Capture().Resources
                    .Where(resource => resource.Type == nameof(AudioClip)).ToArray();
                var ownerCount = tracked.Sum(resource => resource.ReferenceCount);
                var languages = string.Join(",", tracked.SelectMany(resource => resource.Owners)
                    .Select(owner => owner.SourceLanguage).Distinct().OrderBy(code => code, StringComparer.Ordinal));
                Check(clips.Length == expectedClips && ownerCount == expectedClips && languages == expectedLanguages,
                    $"Cycle {cycle} {phase}: {clips.Length} clips, {ownerCount} references, languages [{languages}]");
                rows.Add(new JObject {
                    ["cycle"] = cycle, ["phase"] = phase, ["audioClips"] = clips.Length,
                    ["audioBytes"] = clips.Sum(Profiler.GetRuntimeMemorySizeLong),
                    ["ownerReferences"] = ownerCount, ["ownerLanguages"] = languages,
                    ["allocatedBytes"] = Profiler.GetTotalAllocatedMemoryLong(),
                    ["monoUsedBytes"] = Profiler.GetMonoUsedSizeLong(),
                    ["previousMainThreadNs"] = mainThread.Valid ? mainThread.LastValue : 0
                });
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Launch()
        {
            if (!Environment.GetCommandLineArgs().Contains("--context-lifecycle-probe")) return;
            var obj = new GameObject("Localization Context Lifecycle Probe"); DontDestroyOnLoad(obj); obj.AddComponent<ContextLifecycleProbe>();
        }
        private void Check(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); checks++; }
        private void Update()
        { if (!finished && Time.realtimeSinceStartup > 120) { report["failure"] = "Probe timed out."; Finish(); } }
        private async void Start()
        {
            report["unityVersion"] = Application.unityVersion; report["graphics"] = SystemInfo.graphicsDeviceType.ToString();
            var config = LocalizationProjectConfig.Create(new[] { "en", "ru" });
            foreach (string key in new[] { "Radio/01", "Radio/02", "Radio/03" }) config.Sets[0].AddEntry(key, LocalizationEntryKind.Asset);
            foreach (string code in new[] { "en", "ru" }) config.Sets[0].SetSources(code,
                new LocalizationSourceReference("resources", "NightShift/Worlds/Precinct/Voice/" + code + "/Radio", LocalizationSourceKind.AssetTable));
            var metrics = new HorrorLoadMetrics(); var sources = new LocalizationSourceRegistry();
            sources.Register("resources", new ObservedResourcesProvider(metrics));
            var options = new LocalizationRuntimeOptions { Ownership = LocalizationResourceOwnership.Owned, MaxAssetEntries = 6 };
            using var first = new LocalizationRuntime(config, sources, options);
            using var second = new LocalizationRuntime(config, sources, options);
            try
            {
                Check(Resources.FindObjectsOfTypeAll<AudioClip>().Length == 0, "Audio already loaded before context.");
                await first.InitializeAsync("ru"); await second.InitializeAsync("ru");
                var clip = first.GetAsset<AudioClip>("Radio/01"); int clipId = clip.GetInstanceID();
                Check(clip.loadState == AudioDataLoadState.Loaded, "Audio not ready after preparation.");
                Check(second.GetAsset<AudioClip>("Radio/01") == clip, "Runtimes do not share the same asset.");
                int requests = metrics.TotalReads;
                for (int i = 0; i < 1000; i++) Check(second.GetAsset<AudioClip>("Radio/01") == clip, "Ready lookup changed clip.");
                Check(requests == metrics.TotalReads, "Ready lookup started a load.");
                await first.ReleaseAsync();
                Check(Resources.FindObjectsOfTypeAll<AudioClip>().Any(c => c.GetInstanceID() == clipId), "First owner released a shared clip.");
                await second.ReleaseAsync();
                Check(Resources.FindObjectsOfTypeAll<AudioClip>().Length == 0, "Last owner left audio resident.");
                Check(clip == null, "Player still has a live native clip after last release.");
                report["audioAfterLastOwner"] = Resources.FindObjectsOfTypeAll<AudioClip>().Length;
                await MeasureOptionalSet(config, sources, options);
                config.MainChannel.SetStartup(config.Sets[0].Id, true);
                using (var repeated = new LocalizationRuntime(config, sources, options))
                {
                    await repeated.InitializeAsync("en");
                    for (int i = 0; i < 20; i++)
                    {
                        await repeated.SwitchLanguageAsync(i % 2 == 0 ? "ru" : "en");
                        Check(repeated.GetChannel().ActiveSetIds.Count == 1, "Active set registrations accumulated.");
                        Check(Resources.FindObjectsOfTypeAll<AudioClip>().Length == (i % 2 == 0 ? 6 : 3), "Old language assets accumulated.");
                        Check(repeated.GetAsset<AudioClip>("Radio/01").loadState == AudioDataLoadState.Loaded, "Repeated audio not ready.");
                    }
                    await repeated.ReleaseAsync();
                }
                Check(Resources.FindObjectsOfTypeAll<AudioClip>().Length == 0, "Repeated context left audio resident.");
                using (var canceled = new LocalizationRuntime(config, sources, options))
                {
                    var loading = canceled.InitializeAsync("ru"); await canceled.ReleaseAsync();
                    try { await loading; throw new InvalidOperationException("Canceled load succeeded."); } catch (OperationCanceledException) { }
                    Check(Resources.FindObjectsOfTypeAll<AudioClip>().Length == 0, "Late canceled load left audio resident.");
                }
                var visuals = config.AddSet("Visuals"); visuals.AddEntry("Signs/Warning", LocalizationEntryKind.Asset);
                visuals.SetSources("en", new LocalizationSourceReference("resources", "NightShift/Worlds/Precinct/Visuals/en/Signs", LocalizationSourceKind.AssetTable));
                config.MainChannel.RemoveSet(config.Sets[0].Id); config.MainChannel.IncludeSet(visuals.Id, true);
                using (var visual = new LocalizationRuntime(config, options: options))
                {
                    await visual.InitializeAsync("en");
                    var variant = visual.GetAsset<HorrorVisualVariant>("Signs/Warning");
                    var texture = variant.texture; int id = texture.GetInstanceID();
                    Check(variant.contentLanguage == "en", "Missing source language.");
                    await visual.ReleaseAsync();
                    Check(!Resources.FindObjectsOfTypeAll<Texture2D>().Any(t => t.GetInstanceID() == id), "Declared texture dependency stayed loaded.");
                    Check(variant == null && texture == null, "Custom owned graph was not unloaded.");
                    Check(config != null, "Lightweight config was incorrectly unloaded.");
                }
                report["passed"] = true;
            }
            catch (Exception error) { report["passed"] = false; report["failure"] = error.ToString(); }
            finally
            {
                await Task.WhenAll(first.ReleaseAsync(), second.ReleaseAsync());
                Destroy(config); Finish();
            }
        }
        private void Finish()
        {
            if (finished) return; finished = true; report["checks"] = checks;
            var identity = Resources.Load<TextAsset>("NightShift/BuildIdentity"); if (identity != null) report["buildIdentity"] = JObject.Parse(identity.text);
            var args = Environment.GetCommandLineArgs(); int index = Array.IndexOf(args, "--context-lifecycle-report");
            string path = index >= 0 && index + 1 < args.Length ? args[index + 1] : Path.Combine(Application.persistentDataPath, "context-lifecycle.json");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))); File.WriteAllText(path, report.ToString());
            Application.Quit(report.Value<bool>("passed") ? 0 : 1);
        }
    }
}
#endif
