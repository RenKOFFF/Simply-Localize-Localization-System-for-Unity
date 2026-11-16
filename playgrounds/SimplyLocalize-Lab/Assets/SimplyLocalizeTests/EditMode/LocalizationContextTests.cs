using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using SimplyLocalize.Data;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace SimplyLocalize.Tests
{
    public class LocalizationContextTests
    {
        private string root, address;
        private LocalizationConfig config;
        private LanguageProfile en, ru;
        private readonly List<LocalizationContext> contexts = new();
        [SetUp] public void Setup()
        {
            address = "ContextTests_" + Guid.NewGuid().ToString("N"); root = "Assets/" + address;
            Directory.CreateDirectory(root + "/Resources/" + address);
            File.WriteAllText(root + "/Resources/" + address + "/en.json", "{\"translations\":{\"a\":\"A {0}\",\"b\":\"B {0}\",\"c\":\"C {0}\"}}");
            File.WriteAllText(root + "/Resources/" + address + "/ru.json", "{\"translations\":{\"a\":\"R {0}\",\"d\":\"D {0}\"}}");
            File.WriteAllText(root + "/Resources/" + address + "/bad.json", "not json");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            config = ScriptableObject.CreateInstance<LocalizationConfig>(); en = ScriptableObject.CreateInstance<LanguageProfile>(); ru = ScriptableObject.CreateInstance<LanguageProfile>();
            en.languageCode = "en"; ru.languageCode = "ru"; config.languages.Add(en); config.languages.Add(ru); config.fallbackLanguage = config.defaultLanguage = en;
        }
        private LocalizationContext Context(LocalizationContextOptions options = null, string russian = "ru")
        {
            var context = new LocalizationContext(config, new[] {
                new ResourcesLanguageSources("en", new[] { address + "/en" }),
                new ResourcesLanguageSources("ru", new[] { address + "/" + russian }) }, options);
            contexts.Add(context); return context;
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            foreach (var context in contexts) { var task = context.ReleaseAsync(); yield return Done(task); Assert.That(task.IsFaulted, Is.False, task.Exception?.ToString()); }
            contexts.Clear(); Object.DestroyImmediate(config); Object.DestroyImmediate(en); Object.DestroyImmediate(ru); AssetDatabase.DeleteAsset(root);
        }
        private static IEnumerator Done(Task task)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (!task.IsCompleted && watch.Elapsed.TotalSeconds < 15) yield return null;
            Assert.That(task.IsCompleted, Is.True, "Context operation timed out.");
        }
        private static IEnumerator Success(Task task) { yield return Done(task); Assert.That(task.IsCompletedSuccessfully, Is.True, task.Exception?.ToString()); }
        [UnityTest] public IEnumerator ReadyReadsHaveNoRequests_AndTemplateCacheIsBounded()
        {
            var context = Context(new LocalizationContextOptions { MaxTemplates = 2 });
            Assert.That(context.ResolveText("a").Status, Is.EqualTo(LocalizationResolveStatus.NotPrepared));
            yield return Success(context.ChangeLanguageAsync("ru"));
            int requests = context.ResourceRequestCount;
            for (int i = 0; i < 20; i++) foreach (string key in new[] { "a", "b", "c", "d" })
            { Assert.That(context.Get(key, i), Does.Contain(i.ToString())); Assert.That(context.CachedTemplateCount, Is.LessThanOrEqualTo(2)); }
            Assert.That(context.ResolveText("b").ResolvedLanguage, Is.EqualTo("en"));
            Assert.That(context.ResolveText("missing").Status, Is.EqualTo(LocalizationResolveStatus.Missing));
            Assert.That(context.ResourceRequestCount, Is.EqualTo(requests));
            Assert.That(context.TextEntryCount, Is.EqualTo(5));
        }
        [UnityTest] public IEnumerator FailedAndOverBudgetSwitchesPreserveTheActiveLanguage()
        {
            var options = new LocalizationContextOptions { MaxTextEntries = 3 };
            var limited = Context(options); options.MaxTextEntries = 100; // The live contract is a snapshot.
            yield return Success(limited.ChangeLanguageAsync("en"));
            var task = limited.ChangeLanguageAsync("ru"); yield return Done(task);
            Assert.That(task.IsFaulted, Is.True); Assert.That(task.Exception.ToString(), Does.Contain("limits"));
            Assert.That(limited.Get("a", 1), Is.EqualTo("A 1")); Assert.That(limited.CurrentLanguage, Is.EqualTo("en"));
            var bad = Context(russian: "bad"); yield return Success(bad.ChangeLanguageAsync("en"));
            task = bad.ChangeLanguageAsync("ru"); yield return Done(task);
            Assert.That(task.IsFaulted, Is.True); Assert.That(task.Exception.ToString(), Does.Contain("Invalid localization JSON"));
            Assert.That(bad.CurrentLanguage, Is.EqualTo("en"));
        }
        [UnityTest] public IEnumerator NewerPreparationWins_AndCancellationPreservesState()
        {
            var context = Context(); yield return Success(context.ChangeLanguageAsync("en"));
            using var cancellation = new CancellationTokenSource();
            var canceled = context.ChangeLanguageAsync("ru", cancellation.Token); cancellation.Cancel();
            yield return Done(canceled); Assert.That(canceled.IsCanceled, Is.True); Assert.That(context.CurrentLanguage, Is.EqualTo("en"));
            var old = context.ChangeLanguageAsync("ru"); var fresh = context.ChangeLanguageAsync("en");
            yield return Success(fresh); yield return Done(old);
            Assert.That(old.IsCanceled, Is.True); Assert.That(context.CurrentLanguage, Is.EqualTo("en"));
        }
        [UnityTest] public IEnumerator ClosingPendingContextDrainsLoads_AndRejectsFurtherReads()
        {
            var context = Context(); var pending = context.ChangeLanguageAsync("ru");
            var closing = context.ReleaseAsync(); Assert.That(context.ReleaseAsync(), Is.SameAs(closing));
            yield return Success(closing); yield return Done(pending);
            Assert.That(pending.IsCanceled, Is.True); Assert.That(context.TextEntryCount, Is.Zero);
            Assert.Throws<ObjectDisposedException>(() => context.ResolveText("a"));
            Assert.Throws<ObjectDisposedException>(() => context.ChangeLanguageAsync("en"));
        }
        [UnityTest] public IEnumerator EventFailureDoesNotRollBackCommittedData()
        {
            var context = Context(); context.OnLanguageChanged += _ => throw new InvalidOperationException("observer");
            var task = context.ChangeLanguageAsync("en"); yield return Done(task);
            Assert.That(task.IsFaulted, Is.True); Assert.That(task.Exception.ToString(), Does.Contain("observer"));
            Assert.That(context.Get("a", 1), Is.EqualTo("A 1"));
        }
        [UnityTest] public IEnumerator CharacterBudgetAndConfigMutationCannotTriggerHiddenLoads()
        {
            var tiny = Context(new LocalizationContextOptions { MaxTextCharacters = 1 });
            var rejected = tiny.ChangeLanguageAsync("en"); yield return Done(rejected);
            Assert.That(rejected.IsFaulted, Is.True); Assert.That(rejected.Exception.ToString(), Does.Contain("limits"));
            Assert.That(tiny.CurrentLanguage, Is.Null);
            var context = Context(); yield return Success(context.ChangeLanguageAsync("en"));
            int reads = context.ResourceRequestCount; en.fallbackProfile = ru;
            Assert.Throws<InvalidOperationException>(() => context.Get("d"));
            Assert.That(context.ResourceRequestCount, Is.EqualTo(reads));
        }
        [UnityTest] public IEnumerator RepeatedSwitchingKeepsOnlyCurrentFallbackSet()
        {
            var context = Context();
            for (int i = 0; i < 8; i++)
            {
                yield return Success(context.ChangeLanguageAsync(i % 2 == 0 ? "ru" : "en"));
                Assert.That(context.TextEntryCount, Is.EqualTo(i % 2 == 0 ? 5 : 3));
            }
        }
        [UnityTest] public IEnumerator OversizedSourceIsRejectedBeforeParsing()
        {
            var context = Context(new LocalizationContextOptions { MaxSourceCharacters = 1 }, "bad");
            var task = context.ChangeLanguageAsync("ru"); yield return Done(task);
            Assert.That(task.IsFaulted, Is.True); Assert.That(task.Exception.ToString(), Does.Contain("source exceeds context limits"));
            Assert.That(task.Exception.ToString(), Does.Not.Contain("Invalid localization JSON"));
        }
        [Test] public void OptionsAreValidatedAndOwnedContextsAreRuntimeOnly()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Context(new LocalizationContextOptions { MaxTemplates = 0 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => Context(new LocalizationContextOptions { AudioTimeoutSeconds = double.NaN }));
            Assert.Throws<InvalidOperationException>(() => Context(new LocalizationContextOptions { Ownership = LocalizationResourceOwnership.Owned }));
        }
    }
}
