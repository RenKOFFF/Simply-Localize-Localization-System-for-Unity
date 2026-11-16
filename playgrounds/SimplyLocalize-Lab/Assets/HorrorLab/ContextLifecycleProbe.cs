using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using SimplyLocalize.Data;
using UnityEngine;

namespace SimplyLocalize.HorrorLab
{
    /// <summary>Fresh Player verification of real native release. No native cleanup exists in this probe.</summary>
    public sealed class ContextLifecycleProbe : MonoBehaviour
    {
        private readonly JObject report = new();
        private int checks;
        private bool finished;
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
            var config = ScriptableObject.CreateInstance<LocalizationConfig>(); config.loadTextData = false;
            var en = ScriptableObject.CreateInstance<LanguageProfile>(); en.languageCode = "en";
            var ru = ScriptableObject.CreateInstance<LanguageProfile>(); ru.languageCode = "ru";
            config.languages.Add(en); config.languages.Add(ru); config.defaultLanguage = config.fallbackLanguage = en;
            var sources = new[] { "en", "ru" }.Select(c => new ResourcesLanguageSources(c,
                assetTables: new[] { "NightShift/Worlds/Precinct/Voice/" + c + "/Radio" })).ToArray();
            var options = new LocalizationContextOptions { Ownership = LocalizationResourceOwnership.Owned, MaxAssetEntries = 6 };
            using var first = new LocalizationContext(config, sources, options);
            using var second = new LocalizationContext(config, sources, options);
            try
            {
                Check(Resources.FindObjectsOfTypeAll<AudioClip>().Length == 0, "Audio already loaded before context.");
                await first.ChangeLanguageAsync("ru"); await second.ChangeLanguageAsync("ru");
                var clip = first.ResolveAsset<AudioClip>("Radio/01").Value; int clipId = clip.GetInstanceID();
                Check(clip.loadState == AudioDataLoadState.Loaded, "Audio not ready after preparation.");
                Check(second.ResolveAsset<AudioClip>("Radio/01").Value == clip, "Contexts do not share the same asset.");
                int requests = second.ResourceRequestCount;
                for (int i = 0; i < 1000; i++) Check(second.ResolveAsset<AudioClip>("Radio/01").Value == clip, "Ready lookup changed clip.");
                Check(requests == second.ResourceRequestCount, "Ready lookup started a load.");
                await first.ReleaseAsync();
                Check(Resources.FindObjectsOfTypeAll<AudioClip>().Any(c => c.GetInstanceID() == clipId), "First owner released a shared clip.");
                await second.ReleaseAsync();
                Check(Resources.FindObjectsOfTypeAll<AudioClip>().Length == 0, "Last owner left audio resident.");
                Check(clip == null, "Player still has a live native clip after last release.");
                report["audioAfterLastOwner"] = Resources.FindObjectsOfTypeAll<AudioClip>().Length;
                using (var repeated = new LocalizationContext(config, sources, options))
                {
                    for (int i = 0; i < 12; i++)
                    {
                        await repeated.ChangeLanguageAsync(i % 2 == 0 ? "ru" : "en");
                        Check(repeated.AssetEntryCount == (i % 2 == 0 ? 6 : 3), "Active set accumulated old entries.");
                        Check(Resources.FindObjectsOfTypeAll<AudioClip>().Length == (i % 2 == 0 ? 6 : 3), "Old language assets accumulated.");
                        Check(repeated.ResolveAsset<AudioClip>("Radio/01").Value.loadState == AudioDataLoadState.Loaded, "Repeated audio not ready.");
                    }
                    await repeated.ReleaseAsync();
                }
                Check(Resources.FindObjectsOfTypeAll<AudioClip>().Length == 0, "Repeated context left audio resident.");
                using (var canceled = new LocalizationContext(config, sources, options))
                {
                    var loading = canceled.ChangeLanguageAsync("ru"); await canceled.ReleaseAsync();
                    try { await loading; throw new InvalidOperationException("Canceled load succeeded."); } catch (OperationCanceledException) { }
                    Check(Resources.FindObjectsOfTypeAll<AudioClip>().Length == 0, "Late canceled load left audio resident.");
                }
                var visualSources = new[] { new ResourcesLanguageSources("en", assetTables: new[] { "NightShift/Worlds/Precinct/Visuals/en/Signs" }) };
                using (var visual = new LocalizationContext(config, visualSources, options))
                {
                    await visual.ChangeLanguageAsync("en");
                    var variant = visual.ResolveAsset<HorrorVisualVariant>("Signs/Warning").Value;
                    var texture = variant.texture; int id = texture.GetInstanceID();
                    Check(variant.contentLanguage != null, "Missing borrowed profile.");
                    await visual.ReleaseAsync();
                    Check(!Resources.FindObjectsOfTypeAll<Texture2D>().Any(t => t.GetInstanceID() == id), "Declared texture dependency stayed loaded.");
                    Check(variant == null && texture == null, "Custom owned graph was not unloaded.");
                    Check(en != null, "Shared config profile was incorrectly unloaded.");
                }
                report["passed"] = true;
            }
            catch (Exception error) { report["passed"] = false; report["failure"] = error.ToString(); }
            finally
            {
                await Task.WhenAll(first.ReleaseAsync(), second.ReleaseAsync());
                Destroy(config); Destroy(en); Destroy(ru); Finish();
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
