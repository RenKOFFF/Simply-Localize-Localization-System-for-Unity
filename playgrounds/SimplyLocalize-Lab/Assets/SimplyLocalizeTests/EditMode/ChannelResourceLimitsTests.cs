using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using SimplyLocalize.TextProcessing;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace SimplyLocalize.Tests
{
    public class ChannelResourceLimitsTests
    {
        private string root, address;
        private LocalizationProjectConfig config;
        private readonly List<LocalizationRuntime> runtimes = new();
        private readonly Dictionary<LocalizationRuntime, ObservedResources> providers = new();
        [SetUp] public void Setup()
        {
            address = "ChannelLimits_" + Guid.NewGuid().ToString("N"); root = "Assets/" + address;
            Directory.CreateDirectory(root + "/Resources/" + address);
            File.WriteAllText(root + "/Resources/" + address + "/en.json", "{\"translations\":{\"a\":\"A {0}\",\"b\":\"B {0}\",\"c\":\"C {0}\"}}");
            File.WriteAllText(root + "/Resources/" + address + "/ru.json", "{\"translations\":{\"a\":\"R {0}\",\"d\":\"D {0}\"}}");
            File.WriteAllText(root + "/Resources/" + address + "/bad.json", "not json");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            config = LocalizationProjectConfig.Create(new[] { "en", "ru" });
            config.MainChannel.FallbackLanguage = "en";
            foreach (string key in new[] { "a", "b", "c", "d" }) config.Sets[0].AddEntry(key);
        }
        private LocalizationRuntime Runtime(LocalizationRuntimeOptions options = null, string russian = "ru")
        {
            config.Sets[0].SetSources("en", new LocalizationSourceReference("observed", address + "/en", LocalizationSourceKind.TextTable));
            config.Sets[0].SetSources("ru", new LocalizationSourceReference("observed", address + "/" + russian, LocalizationSourceKind.TextTable));
            var provider = new ObservedResources(); var registry = new LocalizationSourceRegistry(false); registry.Register("observed", provider);
            var runtime = new LocalizationRuntime(config, registry, options);
            providers.Add(runtime, provider); runtimes.Add(runtime); return runtime;
        }
        private static Task Change(LocalizationRuntime runtime, string code, CancellationToken token = default) =>
            runtime.IsInitialized ? runtime.SwitchLanguageAsync(code, token) : runtime.InitializeAsync(code, token);
        [UnityTearDown] public IEnumerator Cleanup()
        {
            foreach (var runtime in runtimes)
            {
                yield return Success(runtime.ReleaseAsync());
                Assert.That(providers[runtime].ActiveEntries, Is.Zero);
            }
            runtimes.Clear(); providers.Clear(); Object.DestroyImmediate(config); AssetDatabase.DeleteAsset(root);
        }
        private static IEnumerator Done(Task task)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (!task.IsCompleted && watch.Elapsed.TotalSeconds < 15) yield return null;
            Assert.That(task.IsCompleted, Is.True, "Channel operation timed out.");
        }
        private static IEnumerator Success(Task task) { yield return Done(task); Assert.That(task.IsCompletedSuccessfully, Is.True, task.Exception?.ToString()); }
        [UnityTest] public IEnumerator ReadyReadsHaveNoRequests_AndTemplateCacheIsBounded()
        {
            var runtime = Runtime(new LocalizationRuntimeOptions { MaxTemplates = 2 });
            Assert.Throws<InvalidOperationException>(() => runtime.Get("a"));
            yield return Success(Change(runtime, "ru"));
            var provider = providers[runtime]; int requests = provider.Reads;
            for (int i = 0; i < 20; i++) foreach (string key in new[] { "a", "b", "c", "d" })
            {
                Assert.That(runtime.Get(key, i), Does.Contain(i.ToString()));
                Assert.That(((LocalizationMessageFormatter)runtime.Formatter).CachedTemplateCount, Is.LessThanOrEqualTo(2));
            }
            Assert.That(runtime.GetChannel().TryGetText("b", out var fallback), Is.True);
            Assert.That(fallback.Language, Is.EqualTo("en"));
            Assert.That(runtime.GetChannel().TryGetText("missing", out _), Is.False);
            Assert.That(provider.Reads, Is.EqualTo(requests)); Assert.That(provider.ActiveEntries, Is.EqualTo(5));
        }
        [UnityTest] public IEnumerator FailedAndOverBudgetSwitchesPreserveTheActiveLanguage()
        {
            var options = new LocalizationRuntimeOptions { MaxTextEntries = 3 };
            var limited = Runtime(options); options.MaxTextEntries = 100;
            yield return Success(Change(limited, "en"));
            var task = Change(limited, "ru"); yield return Done(task);
            Assert.That(task.IsFaulted, Is.True); Assert.That(task.Exception.ToString(), Does.Contain("limits"));
            Assert.That(limited.Get("a", 1), Is.EqualTo("A 1")); Assert.That(limited.GetChannel().RequestedLanguage, Is.EqualTo("en"));
            var bad = Runtime(russian: "bad"); yield return Success(Change(bad, "en"));
            task = Change(bad, "ru"); yield return Done(task);
            Assert.That(task.IsFaulted, Is.True); Assert.That(task.Exception.ToString(), Does.Contain("Invalid localization JSON"));
            Assert.That(bad.GetChannel().RequestedLanguage, Is.EqualTo("en"));
        }
        [UnityTest] public IEnumerator NewerPreparationWins_AndCancellationPreservesState()
        {
            var runtime = Runtime(); yield return Success(Change(runtime, "en"));
            using var cancellation = new CancellationTokenSource();
            var canceled = Change(runtime, "ru", cancellation.Token); cancellation.Cancel();
            yield return Done(canceled); Assert.That(canceled.IsCanceled, Is.True); Assert.That(runtime.GetChannel().RequestedLanguage, Is.EqualTo("en"));
            var old = Change(runtime, "ru"); var fresh = Change(runtime, "en");
            yield return Success(fresh); yield return Done(old);
            Assert.That(old.IsCanceled, Is.True); Assert.That(runtime.GetChannel().RequestedLanguage, Is.EqualTo("en"));
        }
        [UnityTest] public IEnumerator ClosingPendingRuntimeDrainsLoads_AndRejectsFurtherReads()
        {
            var runtime = Runtime(); var pending = Change(runtime, "ru");
            var closing = runtime.ReleaseAsync(); Assert.That(runtime.ReleaseAsync(), Is.SameAs(closing));
            yield return Success(closing); yield return Done(pending);
            Assert.That(pending.IsCanceled, Is.True); Assert.That(providers[runtime].ActiveEntries, Is.Zero);
            Assert.Throws<ObjectDisposedException>(() => runtime.Get("a"));
            Assert.Throws<ObjectDisposedException>(() => Change(runtime, "en"));
        }
        [UnityTest] public IEnumerator ObserverFailureIsReportedWithoutRollingBackCommittedData()
        {
            var runtime = Runtime(); int errors = 0;
            runtime.ObserverError += error => { Assert.That(error.Message, Is.EqualTo("observer")); errors++; };
            runtime.Changed += (_, _) => throw new InvalidOperationException("observer");
            yield return Success(Change(runtime, "en"));
            Assert.That(errors, Is.EqualTo(1)); Assert.That(runtime.Get("a", 1), Is.EqualTo("A 1"));
        }
        [UnityTest] public IEnumerator CharacterBudgetAndConfigMutationCannotTriggerHiddenLoads()
        {
            var tiny = Runtime(new LocalizationRuntimeOptions { MaxTextCharacters = 1 });
            var rejected = Change(tiny, "en"); yield return Done(rejected);
            Assert.That(rejected.IsFaulted, Is.True); Assert.That(rejected.Exception.ToString(), Does.Contain("limits"));
            Assert.That(tiny.IsInitialized, Is.False);
            var runtime = Runtime(); yield return Success(Change(runtime, "en"));
            int reads = providers[runtime].Reads; config.MainChannel.SetFallback("en", "ru");
            Assert.That(runtime.Get("d"), Is.EqualTo("d"), "Authoring changes do not mutate a running definition.");
            Assert.That(providers[runtime].Reads, Is.EqualTo(reads));
        }
        [UnityTest] public IEnumerator RepeatedSwitchingKeepsOnlyCurrentFallbackSources()
        {
            var runtime = Runtime();
            for (int i = 0; i < 8; i++)
            {
                yield return Success(Change(runtime, i % 2 == 0 ? "ru" : "en"));
                Assert.That(providers[runtime].ActiveEntries, Is.EqualTo(i % 2 == 0 ? 5 : 3));
            }
        }
        [UnityTest] public IEnumerator OversizedSourceIsRejectedBeforeParsing()
        {
            var runtime = Runtime(new LocalizationRuntimeOptions { MaxSourceCharacters = 1 }, "bad");
            var task = Change(runtime, "ru"); yield return Done(task);
            Assert.That(task.IsFaulted, Is.True); Assert.That(task.Exception.ToString(), Does.Contain("source exceeds configured limits"));
            Assert.That(task.Exception.ToString(), Does.Not.Contain("Invalid localization JSON"));
        }
        [UnityTest] public IEnumerator OptionsAreValidatedAndOwnedResourcesCannotLoadInEditMode()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Runtime(new LocalizationRuntimeOptions { MaxTemplates = 0 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => Runtime(new LocalizationRuntimeOptions { AudioTimeoutSeconds = double.NaN }));
            var owned = Runtime(new LocalizationRuntimeOptions { Ownership = LocalizationResourceOwnership.Owned });
            var task = owned.InitializeAsync(); yield return Done(task);
            Assert.That(task.IsFaulted, Is.True); Assert.That(task.Exception.ToString(), Does.Contain("runtime-only"));
        }
        private sealed class ObservedResources : ILocalizationSourceProvider
        {
            private readonly ResourcesLocalizationSourceProvider inner = new();
            internal int Reads, ActiveEntries;
            public LocalizationProviderCapabilities GetCapabilities(LocalizationSourceKind kind) => inner.GetCapabilities(kind);
            public async Task<LocalizationSourceLease> LoadAsync(LocalizationSourceRequest request, CancellationToken token)
            {
                Reads++; var loaded = await inner.LoadAsync(request, token); int count = loaded.Data.SourceEntryCount;
                ActiveEntries += count;
                return new LocalizationSourceLease(loaded.Data, async () => { try { await loaded.ReleaseAsync(); } finally { ActiveEntries -= count; } });
            }
        }
    }
}
