using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace SimplyLocalize.Tests
{
    public class FacadeChannelTests
    {
        private LocalizationProjectConfig config;
        private readonly List<Source> sources = new();
        private Action<LocalizationRuntime> runtimeHandler, secondHandler;
        private Action<string> languageHandler;
        private static IEnumerator Done(Task task, bool success = true)
        {
            float deadline = Time.realtimeSinceStartup + 10;
            while (!task.IsCompleted && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(task.IsCompleted, Is.True, "Facade operation must drain.");
            if (success) task.GetAwaiter().GetResult();
        }
        [UnitySetUp] public IEnumerator Setup()
        {
            yield return Done(Localization.ShutdownAsync());
            config = LocalizationProjectConfig.Create(new[] { "en", "ru" });
            config.Sets[0].AddEntry("key");
            foreach (string language in new[] { "en", "ru" })
                config.Sets[0].SetSources(language, new LocalizationSourceReference("test", language, LocalizationSourceKind.TextTable));
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            Localization.OnRuntimeChanged -= runtimeHandler; Localization.OnRuntimeChanged -= secondHandler;
            Localization.OnLanguageChanged -= languageHandler;
            yield return Done(Localization.ShutdownAsync());
            UnityEngine.Object.DestroyImmediate(config);
            foreach (var source in sources) Assert.That(source.Active, Is.Zero);
            sources.Clear();
        }
        private Source Provider(string suffix = "") { var source = new Source { Suffix = suffix }; sources.Add(source); return source; }
        [UnityTest] public IEnumerator FailedInitializationPreservesRuntimeAndNotifiesNobody()
        {
            var first = Provider(); yield return Done(Localization.InitializeAsync(config, providers: first.Registry()));
            var previous = Localization.Runtime; int events = 0;
            runtimeHandler = _ => events++; Localization.OnRuntimeChanged += runtimeHandler;
            var failed = Provider(); failed.Fail = true;
            var attempt = Localization.InitializeAsync(config, "ru", failed.Registry()); yield return Done(attempt, false);
            Assert.That(attempt.IsFaulted, Is.True); Assert.That(Localization.Runtime, Is.SameAs(previous));
            Assert.That(Localization.Get("key"), Is.EqualTo("en")); Assert.That(events, Is.Zero);
            Assert.That(Localization.IsInitializing, Is.False); Assert.That(failed.Active, Is.Zero);
        }
        [UnityTest] public IEnumerator LatestInitializationWinsAndDrainsLateSource()
        {
            var slow = Provider(); slow.Delay = true;
            var first = Localization.InitializeAsync(config, providers: slow.Registry());
            var fast = Provider(" new"); var second = Localization.InitializeAsync(config, "ru", fast.Registry());
            Assert.That(first.IsCompleted, Is.False); Assert.That(second.IsCompleted, Is.False);
            Assert.That(fast.Loads, Is.Zero);
            slow.Complete(); yield return Done(first, false); yield return Done(second);
            Assert.That(first.IsCanceled, Is.True); Assert.That(slow.Active, Is.Zero);
            Assert.That(Localization.Runtime, Is.SameAs(second.Result)); Assert.That(Localization.Get("key"), Is.EqualTo("ru new"));
        }
        [UnityTest] public IEnumerator ShutdownWaitsForLateInitializationAndPreventsPublication()
        {
            var slow = Provider(); slow.Delay = true;
            var initialize = Localization.InitializeAsync(config, providers: slow.Registry());
            var close = Localization.ShutdownAsync(); Assert.That(close.IsCompleted, Is.False);
            slow.Complete(); yield return Done(initialize, false); yield return Done(close);
            Assert.That(initialize.IsCanceled, Is.True); Assert.That(Localization.IsInitialized, Is.False);
            Assert.That(Localization.Runtime, Is.Null); Assert.That(slow.Active, Is.Zero);
            yield return Done(Localization.InitializeAsync(config, providers: Provider().Registry()));
        }
        [UnityTest] public IEnumerator ReentrantInitializationReturnsItsOwnRuntimeAndKeepsReplacementAlive()
        {
            Task<LocalizationRuntime> replacement = null; bool once = false;
            var source = Provider();
            runtimeHandler = current =>
            {
                if (current == null || once) return;
                once = true; replacement = Localization.InitializeAsync(config, "ru", source.Registry());
            };
            Localization.OnRuntimeChanged += runtimeHandler;
            var initial = Localization.InitializeAsync(config, providers: source.Registry());
            yield return Done(initial); yield return Done(replacement);
            Assert.That(initial.Result, Is.Not.SameAs(replacement.Result)); Assert.That(initial.Result.IsReleased, Is.True);
            Assert.That(Localization.Runtime, Is.SameAs(replacement.Result)); Assert.That(replacement.Result.IsReleased, Is.False);
        }
        [UnityTest] public IEnumerator ReentrantShutdownDoesNotPublishStaleRuntimeToLaterSubscribers()
        {
            Task closing = null; var seen = new List<LocalizationRuntime>();
            runtimeHandler = value => { if (value != null) closing = Localization.ShutdownAsync(); };
            secondHandler = value => seen.Add(value);
            Localization.OnRuntimeChanged += runtimeHandler; Localization.OnRuntimeChanged += secondHandler;
            var initial = Localization.InitializeAsync(config, providers: Provider().Registry());
            yield return Done(initial); yield return Done(closing);
            Assert.That(seen, Is.EqualTo(new LocalizationRuntime[] { null }));
            Assert.That(initial.Result.IsReleased, Is.True); Assert.That(Localization.Runtime, Is.Null);
        }
        [UnityTest] public IEnumerator ReleasingRuntimeDirectlyDetachesFacadeAndReadyReadsDoNotLoad()
        {
            var source = Provider(); yield return Done(Localization.InitializeAsync(config, providers: source.Registry()));
            int loads = source.Loads;
            for (int i = 0; i < 20; i++)
            {
                Assert.That(Localization.Get("key"), Is.EqualTo("en"));
                Assert.That(Localization.HasKey("key"), Is.True); Assert.That(Localization.GetAsset<Texture2D>("missing"), Is.Null);
            }
            Assert.That(source.Loads, Is.EqualTo(loads));
            yield return Done(Localization.Runtime.ReleaseAsync()); Assert.That(Localization.IsInitialized, Is.False);
            Assert.That(Localization.Config, Is.Null); Assert.That(Localization.Get("key"), Is.EqualTo("key"));
        }
        [UnityTest] public IEnumerator InvalidReinitializationDoesNotCancelAlreadyValidPendingRequest()
        {
            var source = Provider(); source.Delay = true;
            var pending = Localization.InitializeAsync(config, providers: source.Registry());
            Assert.Throws<ArgumentException>(() => Localization.InitializeAsync(config, "xx"));
            source.Complete(); yield return Done(pending);
            Assert.That(Localization.Runtime, Is.SameAs(pending.Result));
        }
        private sealed class Source : ILocalizationSourceProvider
        {
            internal bool Delay, Fail;
            internal string Suffix;
            internal int Loads, Active;
            private TaskCompletionSource<LocalizationSourceLease> pending;
            private LocalizationSourceRequest request;
            internal LocalizationSourceRegistry Registry() { var registry = new LocalizationSourceRegistry(false); registry.Register("test", this); return registry; }
            public LocalizationProviderCapabilities GetCapabilities(LocalizationSourceKind kind) => new(LocalizationLoadUnit.Table, false, false);
            public Task<LocalizationSourceLease> LoadAsync(LocalizationSourceRequest value, CancellationToken token)
            {
                Loads++; if (Fail) throw new InvalidOperationException("test source failure");
                if (Delay) { request = value; pending = new(); return pending.Task; }
                return Task.FromResult(Data(value));
            }
            private LocalizationSourceLease Data(LocalizationSourceRequest value)
            {
                Active++; return new(LocalizationSourceData.FromText(new Dictionary<string, string> { ["key"] = value.Language + Suffix }),
                    () => { Active--; return Task.CompletedTask; });
            }
            internal void Complete() { Delay = false; pending.SetResult(Data(request)); }
        }
    }
}
