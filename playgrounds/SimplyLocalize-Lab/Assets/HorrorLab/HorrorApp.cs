using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SimplyLocalize.HorrorLab
{
    public enum HorrorAppState { Starting, Menu, PreparingWorld, World, LeavingWorld }

    [DefaultExecutionOrder(100)]
    public sealed class HorrorApp : MonoBehaviour
    {
        public static HorrorApp Instance { get; private set; }
        public LocalizationProjectConfig localization;
        public string voiceChannelId, visualChannelId;
        public LocalizationChannelDefinition VoiceChannel => localization.Channels.Single(c => c.Id == voiceChannelId);
        public LocalizationChannelDefinition VisualChannel => localization.Channels.Single(c => c.Id == visualChannelId);
        public LocalizationRuntime Runtime { get; private set; }
        public TMP_FontAsset font;
        public HorrorWorldDefinition worldDefinition;
        public Camera menuCamera;
        public HorrorPreferences Preferences { get; private set; }
        public HorrorTextService Text { get; private set; }
        public HorrorLoadMetrics Metrics { get; } = new();
        public HorrorWorldSession Session { get; private set; }
        public HorrorWorldPresentation Presentation { get; private set; }
        public HorrorPlayground Game => Presentation != null ? Presentation.Game : null;
        public HorrorHud Hud { get; private set; }
        public HorrorAppState State { get; private set; } = HorrorAppState.Starting;
        public bool TextBusy { get; private set; }
        public bool Busy => TextBusy || State == HorrorAppState.Starting || State == HorrorAppState.PreparingWorld || State == HorrorAppState.LeavingWorld;
        public string ProgressKey { get; private set; } = "UI/LoadingText";
        public string ErrorKey { get; private set; }
        public event Action Changed;
        public Task Initialization { get; private set; }
        public Task ReleaseCompletion { get; private set; } = Task.CompletedTask;
        private Task worldOperation = Task.CompletedTask;
        private IHorrorWorldPreparer preparer;
        private CancellationTokenSource operation, textOperation;
        private readonly CancellationTokenSource lifetime = new();
        private Scene worldScene;
        private bool previousBackground, destroyed;

        private void Awake()
        {
            if (Instance != null) throw new InvalidOperationException("Only one Night Shift bootstrap is allowed.");
            Instance = this; previousBackground = Application.runInBackground; Application.runInBackground = true;
            bool benchmark = Environment.GetCommandLineArgs().Contains("--nightshift-benchmark");
            if (localization == null) throw new InvalidOperationException("Assign the Night Shift project configuration.");
            Preferences = new HorrorPreferences(localization.MainChannel, VoiceChannel, VisualChannel, !benchmark);
            var providers = new LocalizationSourceRegistry(); providers.Register("resources", new ObservedResourcesProvider(Metrics));
            Runtime = new LocalizationRuntime(localization, providers, new LocalizationRuntimeOptions {
                Ownership = LocalizationResourceOwnership.Owned, DiagnosticName = "Night Shift" });
            Text = new HorrorTextService(Runtime, Metrics);
            Text.Changed += Notify;
            preparer = CreateWorldPreparer();
            Hud = new HorrorHud(this, font); Changed += Hud.Refresh;
            Initialization = InitializeAsync();
        }
        private async Task InitializeAsync()
        {
            try
            {
                await Text.ChangeAsync(Preferences.TextLocale, lifetime.Token);
                lifetime.Token.ThrowIfCancellationRequested(); State = HorrorAppState.Menu;
            }
            catch (OperationCanceledException) { return; }
            catch (Exception error) { Debug.LogException(error); ErrorKey = "UI/TextFailed"; State = HorrorAppState.Menu; }
            Notify();
        }
        private void LateUpdate() { Hud?.UpdatePosition(); }
        private void Notify() { if (!destroyed) Changed?.Invoke(); }
        public IHorrorWorldPreparer CreateWorldPreparer() => new SimplyLocalizeWorldPreparer(Runtime, voiceChannelId, visualChannelId, Metrics);
        public void SetPreparer(IHorrorWorldPreparer value)
        {
            if (State != HorrorAppState.Menu || Busy || Session != null) throw new InvalidOperationException("Replace preparation service only in an idle menu.");
            preparer = value ?? throw new ArgumentNullException(nameof(value));
        }
        public void SetVoice(string code)
        { RequireMenu(); Preferences.SetVoice(code, VoiceChannel); Notify(); }
        public void SetVisual(string code)
        { RequireMenu(); Preferences.SetVisual(code, VisualChannel); Notify(); }
        private void RequireMenu() { if (State != HorrorAppState.Menu || Busy) throw new InvalidOperationException("Heavy locale selections are available before world loading only."); }
        public void SetInscriptions(InscriptionMode mode) { Preferences.SetInscriptions(mode); Notify(); }
        public void SetSubtitles(bool enabled) { Preferences.SetSubtitles(enabled); Notify(); }
        public async Task ChangeTextAsync(string code)
        {
            if (Busy) throw new InvalidOperationException("Another preparation is active.");
            TextBusy = true; ErrorKey = null; textOperation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); Notify();
            try { await Text.ChangeAsync(code, textOperation.Token); Preferences.SetText(code); }
            catch (OperationCanceledException) { }
            catch (Exception error) { ErrorKey = "UI/TextFailed"; Debug.LogWarning(error.Message); }
            finally { textOperation.Dispose(); textOperation = null; TextBusy = false; Notify(); }
        }
        public void CancelPreparation() { operation?.Cancel(); textOperation?.Cancel(); }
        public Task StartWorldAsync()
        {
            RequireMenu();
            return worldOperation = StartWorldCoreAsync();
        }
        private async Task StartWorldCoreAsync()
        {
            if (!Text.IsReady) { ErrorKey = "UI/TextFailed"; Notify(); return; }
            State = HorrorAppState.PreparingWorld; ErrorKey = null;
            operation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); Notify();
            try
            {
                Session = await preparer.PrepareAsync(worldDefinition, Preferences.VoiceLocale, Preferences.VisualLocale,
                    operation.Token, key => { ProgressKey = key; Notify(); });
                operation.Token.ThrowIfCancellationRequested();
                ProgressKey = "UI/LoadingWorld"; Notify();
                var load = SceneManager.LoadSceneAsync(worldDefinition.scenePath, LoadSceneMode.Additive);
                if (load == null) throw new InvalidOperationException("World scene is not in build settings.");
                // Unity scene loading cannot be cancelled midway: finish it, then roll back before revealing it.
                while (!load.isDone) await Task.Yield();
                worldScene = SceneManager.GetSceneByPath(worldDefinition.scenePath);
                operation.Token.ThrowIfCancellationRequested();
                SceneManager.SetActiveScene(worldScene);
                var game = worldScene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<HorrorPlayground>(true)).Single();
                Presentation = game.GetComponent<HorrorWorldPresentation>();
                if (Presentation == null) throw new InvalidOperationException("Missing world presentation binding.");
                Presentation.Bind(game, Session, Metrics, Notify);
                operation.Token.ThrowIfCancellationRequested();
                menuCamera.gameObject.SetActive(false); State = HorrorAppState.World;
            }
            catch (OperationCanceledException) { await ReleaseWorldAsync(); State = HorrorAppState.Menu; }
            catch (Exception error)
            {
                Debug.LogWarning("Night Shift preparation failed: " + error.Message);
                await ReleaseWorldAsync(); ErrorKey = "UI/WorldFailed"; State = HorrorAppState.Menu;
            }
            finally { operation.Dispose(); operation = null; Notify(); }
        }
        public Task LeaveWorldAsync()
        {
            if (State != HorrorAppState.World || TextBusy) return Task.CompletedTask;
            return worldOperation = LeaveWorldCoreAsync();
        }
        private async Task LeaveWorldCoreAsync()
        {
            State = HorrorAppState.LeavingWorld; ProgressKey = "UI/UnloadingWorld"; Notify();
            await ReleaseWorldAsync(); State = HorrorAppState.Menu; Notify();
        }
        private async Task ReleaseWorldAsync()
        {
            if (Presentation != null) Presentation.Detach(); Presentation = null;
            if (menuCamera != null) menuCamera.gameObject.SetActive(true);
            if (this != null && gameObject.scene.isLoaded) SceneManager.SetActiveScene(gameObject.scene);
            if (worldScene.IsValid() && worldScene.isLoaded)
            { var unload = SceneManager.UnloadSceneAsync(worldScene); if (unload != null) while (!unload.isDone) await Task.Yield(); }
            worldScene = default;
            if (Session != null) await Session.ReleaseAsync(); Session = null;
        }
        public async void Observe(Task task)
        {
            try { await task; }
            catch (OperationCanceledException) { }
            catch (Exception error) { Debug.LogException(error); }
        }
        private void OnDestroy()
        {
            destroyed = true; lifetime.Cancel(); operation?.Cancel(); textOperation?.Cancel();
            if (Presentation != null) Presentation.Detach();
            Changed = null; Hud?.Dispose(); Text?.Dispose();
            if (Instance == this) Instance = null;
            Application.runInBackground = previousBackground;
            ReleaseCompletion = ReleaseAppAsync(); Observe(ReleaseCompletion);
        }
        private async Task ReleaseAppAsync()
        {
            try
            {
                await Task.WhenAll(Initialization ?? Task.CompletedTask, worldOperation);
                await ReleaseWorldAsync();
            }
            finally
            {
                if (Runtime != null) await Runtime.ReleaseAsync();
                lifetime.Dispose();
            }
        }
    }
}
