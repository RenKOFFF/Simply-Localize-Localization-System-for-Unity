using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
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
    public class ScopedResourcesTests
    {
        private string root, address;
        private LocalizationConfig config;
        private LanguageProfile en, ru;
        private LocalizationManager manager;
        private ScopedResourcesDataProvider provider;

        [SetUp] public void SetUp()
        {
            address = "SimplyLocalizeScopeTests_" + Guid.NewGuid().ToString("N");
            root = "Assets/" + address;
            Directory.CreateDirectory(root + "/Resources/" + address);
            File.WriteAllText(root + "/Resources/" + address + "/first.json", "{\"translations\":{\"hello\":\"Hello\"}}");
            File.WriteAllText(root + "/Resources/" + address + "/second.json", "{\"translations\":{\"other\":\"Other\"}}");
            File.WriteAllText(root + "/Resources/" + address + "/duplicate.json", "{\"translations\":{\"hello\":\"Last\"}}");
            File.WriteAllText(root + "/Resources/" + address + "/broken.json", "not json");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var table = ScriptableObject.CreateInstance<LocalizationAssetTable>();
            AssetDatabase.CreateAsset(table, root + "/Resources/" + address + "/Selected.asset");
            var neighbor = ScriptableObject.CreateInstance<LocalizationAssetTable>();
            AssetDatabase.CreateAsset(neighbor, root + "/Resources/" + address + "/Neighbor.asset");
            config = ScriptableObject.CreateInstance<LocalizationConfig>();
            en = ScriptableObject.CreateInstance<LanguageProfile>(); en.languageCode = "en";
            ru = ScriptableObject.CreateInstance<LanguageProfile>(); ru.languageCode = "ru";
            config.languages.Add(en); config.languages.Add(ru); config.defaultLanguage = config.fallbackLanguage = en;
            provider = CreateProvider(); manager = new LocalizationManager(config); manager.SetDataProvider(provider);
        }

        private ScopedResourcesDataProvider CreateProvider(params string[] texts) => new(new[] {
            new ResourcesLanguageSources("en", texts.Length == 0 ? new[] { address + "/first", address + "/second" } : texts,
                new[] { address + "/Selected" }) });

        [TearDown] public void TearDown()
        {
            provider?.Dispose(); manager?.ClearCaches();
            Object.DestroyImmediate(config); Object.DestroyImmediate(en); Object.DestroyImmediate(ru);
            if (root != null) AssetDatabase.DeleteAsset(root);
        }

        [TestCase("")] [TestCase("/")] [TestCase("../secret")] [TestCase("a/../b")]
        [TestCase("C:/file")] [TestCase("a\\b")] [TestCase("a//b")] [TestCase("a/")]
        public void RejectsNonExactAddresses(string path) => Assert.Throws<ArgumentException>(() => new ResourcesLanguageSources("en", new[] { path }));

        [Test] public void DescriptorsCopyInputsAndRejectDuplicates()
        {
            var paths = new[] { address + "/first" }; var source = new ResourcesLanguageSources("en", paths);
            paths[0] = "changed";
            Assert.That(source.TextFiles[0], Is.EqualTo(address + "/first"));
            Assert.Throws<NotSupportedException>(() => ((IList<string>)source.TextFiles)[0] = "changed");
            Assert.Throws<ArgumentException>(() => new ResourcesLanguageSources("en", new[] { "a", "A" }));
            Assert.Throws<ArgumentException>(() => new ScopedResourcesDataProvider(new[] { source, new ResourcesLanguageSources("EN") }));
            Assert.Throws<ArgumentException>(() => new ScopedResourcesDataProvider(new ResourcesLanguageSources[] { null }));
            Assert.Throws<ArgumentNullException>(() => new ScopedResourcesDataProvider(null));
        }

        [Test] public void LoadsOnlyDeclaredFilesAndTables_AndKeepsFallback()
        {
            using var prepared = manager.PrepareLanguage("ru"); manager.ApplyPreparedLanguage(prepared);
            Assert.That(manager.ResolvePreparedText("hello").ResolvedLanguage, Is.EqualTo("en"));
            Assert.That(manager.Get("other"), Is.EqualTo("Other"));
            Assert.That(provider.LoadAssetTables("en").Select(t => t.name), Is.EqualTo(new[] { "Selected" }));
            Assert.That(provider.HasTextData("en"), Is.True); Assert.That(provider.HasTextData("ru"), Is.False);
            Assert.That(provider.LoadTextData("ru"), Is.Empty); Assert.That(provider.LoadAssetTables("ru"), Is.Empty);
        }

        [Test] public void DuplicateKeysUseDeclaredOrder()
        {
            using var ordered = CreateProvider(address + "/first", address + "/duplicate");
            LogAssert.Expect(LogType.Warning, new Regex("Duplicate key 'hello'.*Last declared file wins"));
            Assert.That(ordered.LoadTextData("en")["hello"], Is.EqualTo("Last"));
        }

        [TestCase("missing")] [TestCase("broken")] [TestCase("Selected")]
        public void FailedProviderReplacementPreservesActiveData(string file)
        {
            manager.SetLanguage("en"); using var invalid = CreateProvider(address + "/" + file);
            Assert.That(() => manager.SetDataProvider(invalid), Throws.Exception);
            Assert.That(manager.Get("hello"), Is.EqualTo("Hello"));
            Assert.That(manager.CurrentLanguage, Is.EqualTo("en"));
        }

        [Test] public void ClosingOneProviderDoesNotDestroyAppliedOrSharedAssets()
        {
            using var otherProvider = CreateProvider(); var other = new LocalizationManager(config); other.SetDataProvider(otherProvider);
            using var prepared = manager.PrepareLanguage("ru"); manager.ApplyPreparedLanguage(prepared);
            using var second = other.PrepareLanguage("en"); other.ApplyPreparedLanguage(second);
            var borrowed = provider.LoadAssetTables("en")[0];
            provider.Dispose(); provider.Dispose();
            Assert.That(borrowed != null, Is.True); Assert.That(manager.Get("hello"), Is.EqualTo("Hello"));
            Assert.That(otherProvider.LoadAssetTables("en")[0], Is.SameAs(borrowed));
            Assert.Throws<ObjectDisposedException>(() => provider.LoadTextData("en"));
            Assert.Throws<ObjectDisposedException>(() => provider.LoadAssetTables("en"));
            Assert.Throws<ObjectDisposedException>(() => provider.HasTextData("en"));
            other.ClearCaches();
        }

        [UnityTest] public IEnumerator AsyncPreparationUsesRealResourcesAndMatchesSync()
        {
            var task = manager.PrepareLanguageAsync("ru");
            Assert.That(manager.CurrentLanguage, Is.Null);
            yield return Await(task);
            Assert.That(task.IsCompletedSuccessfully, Is.True, task.Exception?.ToString());
            using var prepared = task.Result; manager.ApplyPreparedLanguage(prepared);
            Assert.That(manager.Get("hello"), Is.EqualTo("Hello"));
            Assert.That(manager.ResolvePreparedText("other").ResolvedLanguage, Is.EqualTo("en"));
            Assert.That(provider.PendingLoadCount, Is.Zero);
        }

        [UnityTest] public IEnumerator CanceledPreparationKeepsState_AndProviderCanBeReused()
        {
            manager.SetLanguage("en"); using var token = new CancellationTokenSource();
            var task = manager.PrepareLanguageAsync("ru", cancellationToken: token.Token); token.Cancel();
            yield return Await(task);
            Assert.That(task.IsCanceled, Is.True); Assert.That(manager.CurrentLanguage, Is.EqualTo("en"));
            var next = manager.PrepareLanguageAsync("ru"); yield return Await(next);
            Assert.That(next.IsCompletedSuccessfully, Is.True, next.Exception?.ToString());
            using var prepared = next.Result; manager.ApplyPreparedLanguage(prepared);
            Assert.That(manager.CurrentLanguage, Is.EqualTo("ru")); Assert.That(provider.PendingLoadCount, Is.Zero);
        }

        [UnityTest] public IEnumerator DisposeCancelsPendingWait_WithoutChangingActiveState()
        {
            manager.SetLanguage("en"); var task = manager.PrepareLanguageAsync("ru");
            provider.Dispose(); yield return Await(task);
            Assert.That(task.IsCanceled, Is.True); Assert.That(provider.PendingLoadCount, Is.Zero);
            Assert.That(manager.CurrentLanguage, Is.EqualTo("en")); Assert.That(manager.Get("hello"), Is.EqualTo("Hello"));
        }

        [UnityTest] public IEnumerator MissingAsyncResourceFailsWithAddress_WithoutPublishing()
        {
            using var invalid = CreateProvider(address + "/missing");
            var fresh = new LocalizationManager(config); fresh.SetDataProvider(invalid);
            var task = fresh.PrepareLanguageAsync("en"); yield return Await(task);
            Assert.That(task.IsFaulted, Is.True); Assert.That(task.Exception.ToString(), Does.Contain(address + "/missing"));
            Assert.That(fresh.CurrentLanguage, Is.Null); Assert.That(invalid.PendingLoadCount, Is.Zero);
        }

        private static IEnumerator Await(Task task)
        {
            var timeout = System.Diagnostics.Stopwatch.StartNew();
            while (!task.IsCompleted && timeout.Elapsed.TotalSeconds < 10) yield return null;
            Assert.That(task.IsCompleted, Is.True, "Resource operation did not finish in 10 seconds.");
        }
    }
}
