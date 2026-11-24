using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SimplyLocalize.TextProcessing;
using UnityEngine;

namespace SimplyLocalize
{
    /// <summary>Convenience facade over one channel runtime. Initialization and mutation are asynchronous; reads never load content.</summary>
    public static class Localization
    {
        private static LocalizationRuntime runtime;
        private static SemaphoreSlim initializationGate = new(1, 1);
        private static HashSet<Task> pending = new();
        private static CancellationTokenSource initializationCancellation;
        private static Task<LocalizationRuntime> initializing;
        private static long version;
        private static int thread = Environment.CurrentManagedThreadId;
        public static LocalizationRuntime Runtime => runtime;
        public static LocalizationProjectConfig Config { get; private set; }
        public static bool IsInitialized => runtime != null && runtime.IsInitialized && !runtime.IsReleased;
        public static bool IsInitializing => initializing != null;
        public static string CurrentLanguage => IsInitialized ? runtime.GetChannel().RequestedLanguage : null;
        public static LocalizationAppearance CurrentAppearance => IsInitialized ? runtime.GetChannel().GetAppearance() : null;
        public static IReadOnlyList<string> AvailableLanguages => IsInitialized ? runtime.GetAvailableLanguages() : Array.Empty<string>();
        public static event Action<LocalizationRuntime> OnRuntimeChanged;
        public static event Action<string> OnLanguageChanged;
        public static event Action<Exception> OnError;
        // Framework subscriptions survive a no-domain/no-scene reload. Public user callbacks are reset.
        internal static event Action<LocalizationRuntime> BindingChanged;

        public static Task<LocalizationRuntime> InitializeAsync(LocalizationProjectConfig config, string languageCode = null,
            LocalizationSourceRegistry providers = null, LocalizationRuntimeOptions options = null,
            ILocalizationTextFormatter formatter = null, CancellationToken cancellationToken = default)
        {
            EnsureThread();
            if (config == null) throw new ArgumentNullException(nameof(config));
            config.ValidateOrThrow();
            string selected = InitialLanguage(config, languageCode);
            return StartInitialization(() => Task.FromResult(config), selected, providers, options, formatter, cancellationToken);
        }
        public static Task<LocalizationRuntime> InitializeAsync(CancellationToken cancellationToken = default)
        {
            EnsureThread();
            if (IsInitialized) return Task.FromResult(runtime);
            if (initializing != null) return initializing;
            return StartInitialization(() => FindResourceConfigAsync(false), null, null, null, null, cancellationToken);
        }
        internal static async Task<LocalizationRuntime> InitializeForBootstrapAsync(LocalizationProjectConfig config, string language, CancellationToken token)
        {
            if (IsInitialized) return null;
            var inFlight = initializing;
            if (inFlight != null)
            {
                LocalizationRuntime result;
                try { result = await inFlight; }
                catch { token.ThrowIfCancellationRequested(); return null; }
                token.ThrowIfCancellationRequested();
                // A successful existing initialization belongs to its original caller.
                // A completed automatic scan without a config must not suppress a scene bootstrap.
                if (result != null || IsInitialized || IsInitializing) return null;
            }
            return await InitializeAsync(config, language, cancellationToken: token);
        }
        internal static Task<LocalizationRuntime> InitializeAutomaticallyAsync()
        {
            EnsureThread();
            if (IsInitialized || IsInitializing) return Task.FromResult<LocalizationRuntime>(null);
            return StartInitialization(() => FindResourceConfigAsync(true), null, null, null, null, default);
        }
        private static Task<LocalizationRuntime> StartInitialization(Func<Task<LocalizationProjectConfig>> findConfig, string language,
            LocalizationSourceRegistry providers, LocalizationRuntimeOptions options, ILocalizationTextFormatter formatter, CancellationToken token)
        {
            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
            var previous = initializationCancellation; initializationCancellation = cancellation;
            long request = ++version;
            var gate = initializationGate; var operations = pending;
            var completion = new TaskCompletionSource<LocalizationRuntime>();
            initializing = completion.Task; operations.Add(completion.Task);
            Cancel(previous);
            Run(); return completion.Task;
            async void Run()
            {
                bool entered = false; LocalizationRuntime candidate = null, result = null; Exception failure = null;
                try
                {
                    await gate.WaitAsync(cancellation.Token); entered = true;
                    var config = await findConfig(); Check();
                    if (config != null)
                    {
                        string selected = InitialLanguage(config, language);
                        candidate = new LocalizationRuntime(config, providers, options, formatter);
                        await candidate.InitializeAsync(selected, cancellation.Token); Check();
                        var old = runtime; Unsubscribe(old);
                        runtime = result = candidate; Config = config; candidate = null;
                        runtime.Changed += ForwardChange; runtime.Releasing += RuntimeReleasing; runtime.ObserverError += Report;
                        PublishRuntime(runtime);
                        if (ReferenceEquals(runtime, result)) PublishLanguage(result.GetChannel().RequestedLanguage);
                        if (old != null) try { await old.ReleaseAsync(); } catch (Exception error) { Report(error); }
                    }
                }
                catch (Exception error) { failure = error; }
                finally
                {
                    if (candidate != null)
                        try { await candidate.ReleaseAsync(); }
                        catch (Exception cleanup) { failure = failure == null ? cleanup : new AggregateException(failure, cleanup); }
                    if (entered) gate.Release();
                    operations.Remove(completion.Task);
                    if (ReferenceEquals(initializing, completion.Task)) initializing = null;
                    if (ReferenceEquals(initializationCancellation, cancellation)) initializationCancellation = null;
                    cancellation.Dispose();
                }
                if (failure is OperationCanceledException) completion.TrySetCanceled();
                else if (failure != null) completion.TrySetException(failure);
                else completion.TrySetResult(result);
            }
            void Check()
            {
                cancellation.Token.ThrowIfCancellationRequested();
                if (request != version) throw new OperationCanceledException("A newer localization initialization superseded this request.");
            }
        }
        private static string InitialLanguage(LocalizationProjectConfig config, string requested)
        {
            config.ValidateOrThrow();
            string code = requested;
            if (code == null && config.AutoDetectLanguage)
                code = config.Languages.FirstOrDefault(l => l.SystemLanguage != SystemLanguage.Unknown
                    && l.SystemLanguage == Application.systemLanguage && config.MainChannel.Languages.Contains(l.Code))?.Code;
            code = LocalizationLanguageCode.Normalize(code ?? config.MainChannel.DefaultLanguage);
            if (!config.MainChannel.Languages.Contains(code)) throw new ArgumentException("Language is not available to Main: " + code, nameof(requested));
            return code;
        }
        internal static async Task<LocalizationProjectConfig> FindResourceConfigAsync(bool automaticOnly)
        {
            var operation = Resources.LoadAsync<LocalizationProjectConfig>(LocalizationProjectConfig.StartupResourcePath);
            await Await(operation);
            var config = operation.asset as LocalizationProjectConfig;
            if (automaticOnly) return config != null && config.AutoInitialize ? config : null;
            if (config != null) return config;
            throw new InvalidOperationException("No Project Configuration at Resources/SimplyLocalizeSettings.asset. Create it in Simply Localize setup or pass it to InitializeAsync. Old LocalizationConfig assets must be replaced explicitly.");
        }
        private static async Task Await(ResourceRequest request)
        {
            if (request.isDone) return;
            var completion = new TaskCompletionSource<bool>();
            Action<AsyncOperation> finished = _ => completion.TrySetResult(true);
            request.completed += finished;
            try { await completion.Task; } finally { request.completed -= finished; }
        }
        public static Task SetLanguageAsync(string language, CancellationToken token = default)
        { EnsureReady(); return runtime.SwitchLanguageAsync(language, token); }
        public static Task ReloadAsync(CancellationToken token = default)
        { EnsureReady(); return runtime.ReloadAsync(cancellationToken: token); }
        public static Task ReplaceProvidersAsync(LocalizationSourceRegistry providers, CancellationToken token = default)
        { EnsureReady(); return runtime.ReplaceProvidersAsync(providers, token); }
        public static string Get(string key) => IsInitialized ? runtime.Get(key) : key ?? "";
        public static string Get(string key, params object[] args) => IsInitialized ? runtime.Get(key, args) : key ?? "";
        public static string Get(string key, Dictionary<string, object> namedArgs) => IsInitialized ? runtime.Get(key, namedArgs) : key ?? "";
        public static string Get(string key, object[] args, Dictionary<string, object> namedArgs) => IsInitialized ? runtime.Format(key, args, namedArgs) : key ?? "";
        public static T GetAsset<T>(string key) where T : UnityEngine.Object => IsInitialized ? runtime.GetAsset<T>(key) : null;
        public static bool HasKey(string key) => IsInitialized && runtime.GetChannel().TryGetText(key, out _);

        public static Task ShutdownAsync()
        {
            EnsureThread(); ++version; Cancel(initializationCancellation);
            var old = runtime; Unsubscribe(old); runtime = null; Config = null;
            PublishRuntime(null);
            return Drain(old, pending.ToArray());
        }
        internal static Task ShutdownIfOwnedAsync(LocalizationRuntime owner) =>
            owner != null && ReferenceEquals(runtime, owner) ? ShutdownAsync() : Task.CompletedTask;
        private static async Task Drain(LocalizationRuntime old, Task[] operations)
        {
            try { await Task.WhenAll(operations); } catch { /* Initialization callers retain their own failures. */ }
            if (old != null) await old.ReleaseAsync();
        }
        private static void ForwardChange(LocalizationRuntime sender, IReadOnlyList<string> channels)
        { if (ReferenceEquals(runtime, sender) && channels.Contains(sender.MainChannelId)) PublishLanguage(sender.GetChannel().RequestedLanguage); }
        private static void RuntimeReleasing(LocalizationRuntime sender)
        {
            if (!ReferenceEquals(runtime, sender)) return;
            Unsubscribe(sender); runtime = null; Config = null; PublishRuntime(null);
        }
        private static void Unsubscribe(LocalizationRuntime target)
        { if (target != null) { target.Changed -= ForwardChange; target.Releasing -= RuntimeReleasing; target.ObserverError -= Report; } }
        private static void PublishRuntime(LocalizationRuntime value)
        {
            foreach (Action<LocalizationRuntime> subscriber in BindingChanged?.GetInvocationList() ?? Array.Empty<Delegate>())
            {
                if (!ReferenceEquals(runtime, value)) return;
                try { subscriber(value); } catch (Exception error) { Report(error); }
            }
            foreach (Action<LocalizationRuntime> subscriber in OnRuntimeChanged?.GetInvocationList() ?? Array.Empty<Delegate>())
            {
                if (!ReferenceEquals(runtime, value)) return;
                try { subscriber(value); } catch (Exception error) { Report(error); }
            }
        }
        private static void PublishLanguage(string language)
        {
            var sender = runtime;
            foreach (Action<string> subscriber in OnLanguageChanged?.GetInvocationList() ?? Array.Empty<Delegate>())
            {
                if (!ReferenceEquals(runtime, sender) || CurrentLanguage != language) return;
                try { subscriber(language); } catch (Exception error) { Report(error); }
            }
        }
        private static void Cancel(CancellationTokenSource source)
        { if (source != null) try { source.Cancel(); } catch (Exception error) { Report(error); } }
        internal static void Report(Exception error)
        {
            if (OnError == null) { Debug.LogException(error); return; }
            foreach (Action<Exception> subscriber in OnError.GetInvocationList())
                try { subscriber(error); } catch (Exception nested) { Debug.LogException(nested); }
        }
        internal static async void Observe(Task operation)
        { try { await operation; } catch (OperationCanceledException) { } catch (Exception error) { Report(error); } }
        private static void EnsureThread()
        { if (thread != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Use localization on the Unity thread."); }
        private static void EnsureReady()
        { EnsureThread(); if (!IsInitialized) throw new InvalidOperationException("Await Localization.InitializeAsync before changing localization."); }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRuntimeState()
        {
            thread = Environment.CurrentManagedThreadId; ++version;
            var old = runtime; Unsubscribe(old); runtime = null; Config = null;
            OnRuntimeChanged = null; OnLanguageChanged = null; OnError = null;
            Cancel(initializationCancellation); initializationCancellation = null; initializing = null;
            pending = new(); initializationGate = new(1, 1);
            PublishRuntime(null);
            if (old != null) Observe(old.ReleaseAsync());
            LocalizationLogger.Enabled = LocalizationLogger.EnabledInBuild = false;
            Pluralization.PluralRuleProvider.ResetRuntimeState();
        }
    }
}
