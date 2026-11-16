using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;

namespace SimplyLocalize.HorrorLab
{
    /// <summary>Observes stock plugin behavior. Known limitations are findings, not hidden by cleanup.</summary>
    public sealed class HorrorBenchmark : MonoBehaviour
    {
        [Serializable] public sealed class Sample
        {
            public string name, audioStateBeforePlay;
            public double worldSetupMs, textSwitchMs, playCallMs, frameP95Ms, frameMaxMs;
            public long unityAllocatedBytes, managedBytes;
            public int audioObjects, textureObjects, providerReads, firstDialogueProviderReads;
        }
        [Serializable] public sealed class Finding
        {
            public string id, evidence;
            public Finding(string id, string evidence) { this.id = id; this.evidence = evidence; }
        }
        [Serializable] public sealed class Report
        {
            public string completedUtc, unityVersion, device, graphics, note;
            public bool functionalChecksPassed;
            public string failure;
            public long peakUnityAllocatedBytes, peakManagedBytes;
            public double initialTextSwitchMs, peakObservedFrameMs;
            public string[] startupAudio, menuAudio;
            public int warningCount;
            public List<string> warnings = new();
            public List<string> checks = new();
            public List<Finding> findings = new();
            public List<Sample> samples = new();
        }
        private readonly Report report = new();
        private bool finished;
        private void Update()
        {
            if (finished) return;
            report.peakUnityAllocatedBytes = Math.Max(report.peakUnityAllocatedBytes, Profiler.GetTotalAllocatedMemoryLong());
            report.peakManagedBytes = Math.Max(report.peakManagedBytes, GC.GetTotalMemory(false));
            report.peakObservedFrameMs = Math.Max(report.peakObservedFrameMs, Time.unscaledDeltaTime * 1000);
            if (Time.realtimeSinceStartup > 180) { report.failure = "Verification timed out."; Finish(); }
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Launch()
        {
            if (!Environment.GetCommandLineArgs().Contains("--nightshift-benchmark")) return;
            var obj = new GameObject("Night Shift Plugin Audit"); DontDestroyOnLoad(obj); obj.AddComponent<HorrorBenchmark>();
        }
        private void Check(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); report.checks.Add(message); }
        private void Observe(string id, string evidence) => report.findings.Add(new Finding(id, evidence));
        private async void Start()
        {
            Application.targetFrameRate = 60;
            report.unityVersion = Application.unityVersion; report.device = SystemInfo.processorType;
            report.graphics = SystemInfo.graphicsDeviceType.ToString();
            report.note = "Protocol plugin-owned-v3-scoped-async: plugin prepares exact world table addresses through Resources.LoadAsync; ready-only asset reads; no manual localization cleanup/audio prewarming. Functional PASS is not a plugin performance certification. Small fixture; 60 FPS cap; world frames measured with menu open; Null graphics excludes GPU. Play call cost is not physical sound latency. Unity allocator/GC counters are not process RAM. Once-per-frame peaks can miss short spikes.";
            Application.logMessageReceived += OnLog;
            try
            {
                var app = HorrorApp.Instance ?? throw new InvalidOperationException("Start from NightShiftMenu.");
                report.startupAudio = DescribeAudio();
                await app.Initialization;
                report.initialTextSwitchMs = app.Metrics.lastTextPrepareMs; report.menuAudio = DescribeAudio();
                Check(app.State == HorrorAppState.Menu && app.Session == null && app.Game == null, "Menu has no world scene");
                Check(Cursor.lockState == CursorLockMode.None, "Startup does not capture cursor");
                if (report.menuAudio.Length > 0) Observe("SL-001", "AudioClip objects already exist before any world: " + string.Join(", ", report.menuAudio));
                Capture("initial-menu", app);
                var global = Localization.Manager;
                var real = new SimplyLocalizeWorldPreparer(app.Metrics);
                // Measure ordinary first use before the matrix and failure-injection tests warm anything.
                for (int cycle = 0; cycle < 3; cycle++)
                {
                    app.SetVoice(app.worldDefinition.voiceConfig.languages[cycle % app.worldDefinition.voiceConfig.languages.Count].Code);
                    app.SetVisual(app.worldDefinition.visualConfig.languages[cycle % app.worldDefinition.visualConfig.languages.Count].Code);
                    await app.StartWorldAsync();
                    Check(app.State == HorrorAppState.World && app.Game.Ready, "World " + cycle + " is usable");
                    Check(!app.Hud.VoiceLanguage.interactable && !app.Hud.VisualLanguage.interactable, "Heavy settings locked inside world");
                    app.Presentation.voiceSource.mute = true;
                    int beforeReads = app.Metrics.TotalReads, beforeVisuals = app.Presentation.VisualApplyCount;
                    var playback = app.Presentation.Dialogue.PlayAsync(app.worldDefinition.dialogues[0].sequenceId);
                    int firstReads = app.Metrics.TotalReads - beforeReads;
                    string stateBeforePlay = app.Metrics.lastAudioStateBeforePlay;
                    Check(firstReads == 0, "World " + cycle + ": prepared first dialogue performs no provider reads");
                    if (stateBeforePlay != AudioDataLoadState.Loaded.ToString()) Observe("SL-006", "World " + cycle + ": plugin returned audio in state " + stateBeforePlay + " before Play");
                    var clip = app.Presentation.Dialogue.CurrentClip;
                    Check(clip != null, "Dialogue resolves voice through the plugin");
                    string nextText = app.textConfig.languages[(cycle + 1) % app.textConfig.languages.Count].Code;
                    var voice = app.Session.VoiceLocale; var visual = app.Session.VisualLocale;
                    await app.ChangeTextAsync(nextText);
                    Check(app.Text.Locale == nextText && app.Session.VoiceLocale == voice && app.Session.VisualLocale == visual, "Independent language channels");
                    Check(app.Presentation.Dialogue.CurrentClip == clip && app.Presentation.VisualApplyCount == beforeVisuals, "Text change preserves current voice and visual application count");
                    var line = app.Presentation.Dialogue.CurrentLine;
                    Check(app.Hud.Subtitle.text == app.Text.Format("UI/SubtitleLine", app.Text.Get(line.speakerKey), app.Text.Get(line.textKey)), "Subtitle follows selected text");
                    app.Presentation.Dialogue.Stop(); try { await playback; } catch (OperationCanceledException) { }
                    clip = null; playback = null;
                    foreach (var sign in app.Game.interactables.Where(i => i.kind == HorrorInteraction.Sign))
                    {
                        var variant = app.Session.Visual(sign.textureKey);
                        Check(!app.Presentation.ShouldTranslate(sign, app.Text.Locale, InscriptionMode.Off), "Off hides " + sign.name);
                        Check(app.Presentation.ShouldTranslate(sign, app.Text.Locale, InscriptionMode.Auto) == (variant.contentLanguage.Code != app.Text.Locale), "Auto uses resolved source of " + sign.name);
                        Check(app.Presentation.ShouldTranslate(sign, app.Text.Locale, InscriptionMode.Always), "Always translates " + sign.name);
                    }
                    int interactionReads = app.Metrics.TotalReads;
                    var game = app.Game;
                    var door = game.interactables.First(i => i.kind == HorrorInteraction.Door);
                    game.Interact(door); Check(!game.DoorOpen, "Door starts locked");
                    game.Interact(game.interactables.First(i => i.kind == HorrorInteraction.Key));
                    game.Interact(door); Check(game.HasKey && game.DoorOpen, "Key unlocks door");
                    game.Interact(game.interactables.First(i => i.kind == HorrorInteraction.Note)); game.CloseDocument();
                    var frames = new List<double>();
                    for (int i = 0; i < 120; i++) { await Task.Yield(); frames.Add(Time.unscaledDeltaTime * 1000); }
                    Check(app.Metrics.TotalReads == interactionReads, "Resident text and object interactions do not re-read provider");
                    Capture("world-" + cycle, app, frames, firstReads, stateBeforePlay);
                    var context = app.Session;
                    await app.LeaveWorldAsync();
                    Check(app.Session == null && app.Game == null && context.IsDisposed, "Game detaches consumers and drops its context");
                    // No assertion of zero native assets, and absolutely no cleanup to make that assertion pass.
                    Capture("menu-after-" + cycle, app);
                    if (report.samples.Last().audioObjects > 0) Observe("SL-005", "After world " + cycle + ": " + report.samples.Last().audioObjects + " AudioClip objects remain; reachability/leak not inferred");
                }
                string previous = app.Text.Locale;
                using (var cancellation = new CancellationTokenSource())
                {
                    cancellation.Cancel();
                    try { await app.Text.ChangeAsync(app.textConfig.languages[0].Code, cancellation.Token); } catch (OperationCanceledException) { }
                }
                Check(app.Text.Locale == previous, "Cancellation before synchronous SetLanguage keeps state");
                await app.ChangeTextAsync("unsupported-test-locale");
                Check(app.Text.Locale == previous, "Invalid menu selection does not replace locale");
                string missing = "Missing/DeliberateAuditKey";
                if (app.Text.Get(missing) == missing) Observe("SL-008", "Missing key returned as key by plugin; no sample rejection/rollback added");
                await Matrix(app);
                app.SetPreparer(new Delayed(real)); var pending = app.StartWorldAsync(); await Task.Yield();
                Check(app.State == HorrorAppState.PreparingWorld && app.Game == null, "Delayed context creation keeps world hidden");
                app.CancelPreparation(); await pending;
                Check(app.State == HorrorAppState.Menu && app.Session == null, "Cancelled context creation returns to menu");
                app.SetPreparer(new Failing()); await app.StartWorldAsync();
                Check(app.State == HorrorAppState.Menu && app.Session == null && app.ErrorKey != null, "Context failure is reported");
                app.SetPreparer(real);
                var sceneLoad = app.StartWorldAsync();
                while (app.State == HorrorAppState.PreparingWorld && app.ProgressKey != "UI/LoadingWorld") await Task.Yield();
                app.CancelPreparation(); await sceneLoad;
                Check(app.State == HorrorAppState.Menu && app.Session == null && !SceneManager.GetSceneByPath(app.worldDefinition.scenePath).isLoaded, "Cancelled scene load is rolled back");
                Check(ReferenceEquals(global, Localization.Manager), "Global localization facade unchanged");
                Check(Cursor.lockState == CursorLockMode.None, "Audit never captures cursor");
                report.functionalChecksPassed = true;
            }
            catch (Exception error) { report.failure = error.ToString(); Debug.LogError(report.failure); }
            finally { Finish(); }
        }
        private async Task Matrix(HorrorApp app)
        {
            int count = 0;
            foreach (var voice in app.worldDefinition.voiceConfig.languages)
            foreach (var visual in app.worldDefinition.visualConfig.languages)
            {
                using (var session = await new SimplyLocalizeWorldPreparer(app.Metrics).PrepareAsync(app.worldDefinition, voice.Code, visual.Code, CancellationToken.None, null))
                foreach (var locale in app.textConfig.languages)
                {
                    await app.ChangeTextAsync(locale.Code);
                    Check(app.Text.Locale == locale.Code && app.ErrorKey == null, "Matrix text " + locale.Code);
                    foreach (string key in app.worldDefinition.visualKeys)
                    {
                        var variant = session.Visual(key);
                        Check(variant != null && variant.texture != null && variant.contentLanguage != null, "Matrix visual " + visual.Code + "/" + key);
                    }
                    foreach (var line in app.worldDefinition.dialogues.SelectMany(s => s.lines)) Check(session.Clip(line.audioKey) != null, "Matrix voice " + voice.Code + "/" + line.audioKey);
                    count++;
                }
            }
            Check(count == 18, "18 independent language combinations resolved");
        }
        private static string[] DescribeAudio() => Resources.FindObjectsOfTypeAll<AudioClip>().Select(c => c.name + ": " + c.loadState).OrderBy(s => s).ToArray();
        private void Capture(string name, HorrorApp app, List<double> frames = null, int firstReads = 0, string audioState = null)
        {
            frames?.Sort();
            report.samples.Add(new Sample { name = name, worldSetupMs = app.Metrics.lastWorldPrepareMs, textSwitchMs = app.Metrics.lastTextPrepareMs, playCallMs = app.Metrics.lastFirstPlayCallMs,
                unityAllocatedBytes = Profiler.GetTotalAllocatedMemoryLong(), managedBytes = GC.GetTotalMemory(false),
                audioObjects = Resources.FindObjectsOfTypeAll<AudioClip>().Length, textureObjects = Resources.FindObjectsOfTypeAll<Texture>().Length,
                providerReads = app.Metrics.TotalReads, firstDialogueProviderReads = firstReads, audioStateBeforePlay = audioState,
                frameP95Ms = frames == null ? 0 : frames[(int)(frames.Count * .95)], frameMaxMs = frames == null ? 0 : frames.Last() });
        }
        private void OnLog(string message, string stack, LogType type)
        {
            if (type == LogType.Warning) { report.warningCount++; if (report.warnings.Count < 30) report.warnings.Add(message); }
            if ((type == LogType.Error || type == LogType.Exception) && report.failure == null) report.failure = message + "\n" + stack;
        }
        private void Finish()
        {
            if (finished) return; finished = true; Application.logMessageReceived -= OnLog;
            if (report.failure != null) report.functionalChecksPassed = false;
            report.completedUtc = DateTime.UtcNow.ToString("O");
            var result = JObject.Parse(JsonUtility.ToJson(report));
            var identity = Resources.Load<TextAsset>("NightShift/BuildIdentity");
            result["buildIdentity"] = identity != null ? JObject.Parse(identity.text) : new JObject { ["error"] = "Missing build identity" };
            if (identity == null) { report.functionalChecksPassed = false; result["functionalChecksPassed"] = false; result["failure"] = "Missing build identity"; }
            var args = Environment.GetCommandLineArgs(); int index = Array.IndexOf(args, "--nightshift-report");
            string path = index >= 0 && index + 1 < args.Length ? args[index + 1] : Path.Combine(Application.persistentDataPath, "nightshift-benchmark.json");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))); File.WriteAllText(path, result.ToString());
            Debug.Log("Night Shift functional checks: " + report.functionalChecksPassed + "; findings: " + report.findings.Count);
            Application.Quit(report.functionalChecksPassed ? 0 : 1);
        }
        private sealed class Delayed : IHorrorWorldPreparer
        {
            private readonly IHorrorWorldPreparer inner;
            public Delayed(IHorrorWorldPreparer inner) { this.inner = inner; }
            public async Task<HorrorWorldSession> PrepareAsync(HorrorWorldDefinition definition, string voice, string visual, CancellationToken token, Action<string> progress)
            { for (int i = 0; i < 12; i++) { token.ThrowIfCancellationRequested(); await Task.Yield(); } return await inner.PrepareAsync(definition, voice, visual, token, progress); }
        }
        private sealed class Failing : IHorrorWorldPreparer
        {
            public Task<HorrorWorldSession> PrepareAsync(HorrorWorldDefinition definition, string voice, string visual, CancellationToken token, Action<string> progress)
                => Task.FromException<HorrorWorldSession>(new IOException("Expected injected context failure"));
        }
    }
}

