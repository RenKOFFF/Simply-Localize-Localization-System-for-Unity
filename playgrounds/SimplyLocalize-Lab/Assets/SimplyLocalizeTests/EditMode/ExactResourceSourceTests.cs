using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace SimplyLocalize.Tests
{
    public class ExactResourceSourceTests
    {
        private string root, address;
        private LocalizationProjectConfig config;
        private Probe provider;
        private readonly List<LocalizationRuntime> runtimes = new();
        [SetUp] public void Setup()
        {
            address = "SimplyLocalizeScopeTests_" + Guid.NewGuid().ToString("N"); root = "Assets/" + address;
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
            config = LocalizationProjectConfig.Create(new[] { "en", "ru" }); config.MainChannel.FallbackLanguage = "en";
            config.Sets[0].AddEntry("hello"); config.Sets[0].AddEntry("other"); config.Sets[0].AddEntry("icon", LocalizationEntryKind.Asset);
            config.Sets[0].SetSources("en",
                new("resources", address + "/first", LocalizationSourceKind.TextTable),
                new("resources", address + "/second", LocalizationSourceKind.TextTable),
                new("resources", address + "/Selected", LocalizationSourceKind.AssetTable));
            provider = new Probe();
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            foreach (var runtime in runtimes) yield return Done(runtime.ReleaseAsync());
            runtimes.Clear(); Object.DestroyImmediate(config);
            if (root != null) Assert.That(AssetDatabase.DeleteAsset(root), Is.True);
        }
        private LocalizationRuntime Runtime()
        {
            var registry = new LocalizationSourceRegistry(); registry.Register("resources", provider);
            var runtime = new LocalizationRuntime(config, registry); runtimes.Add(runtime); return runtime;
        }
        [TestCase("")] [TestCase("/")] [TestCase("../secret")] [TestCase("a/../b")]
        [TestCase("C:/file")] [TestCase("a\\b")] [TestCase("a//b")] [TestCase("a/")]
        public void RejectsNonExactAddressesBeforeRuntimeCreation(string path)
        {
            config.Sets[0].SetSources("en", new LocalizationSourceReference("resources", path, LocalizationSourceKind.TextTable));
            Assert.That(() => config.ValidateOrThrow(), Throws.Exception);
        }
        [UnityTest] public IEnumerator OnlyDeclaredSourcesLoadAndReadyReadsKeepFallbackWithoutIO()
        {
            var runtime = Runtime(); yield return Done(runtime.InitializeAsync("ru"));
            Assert.That(provider.Addresses, Is.EquivalentTo(new[] { address + "/first", address + "/second", address + "/Selected" }));
            for (int i = 0; i < 100; i++)
            {
                Assert.That(runtime.Get("hello"), Is.EqualTo("Hello")); Assert.That(runtime.Get("other"), Is.EqualTo("Other"));
                Assert.That(runtime.GetChannel().TryGetText("hello", out var found), Is.True); Assert.That(found.Language, Is.EqualTo("en"));
                Assert.That(runtime.GetAsset<Texture2D>("icon"), Is.Null);
            }
            Assert.That(provider.Addresses.Count, Is.EqualTo(3));
            // Editor created Neighbor above; this proves request scope, not physical memory residency.
        }
        [UnityTest] public IEnumerator DuplicateSourceKeysFailAtomicallyInsteadOfSilentlyOverwriting()
        {
            var runtime = Runtime(); yield return Done(runtime.InitializeAsync()); var before = runtime.GetChannel();
            provider.RedirectSecond = address + "/duplicate";
            var failed = runtime.ReloadAsync(); yield return Done(failed, false);
            Assert.That(failed.Exception.ToString(), Does.Contain("Multiple sources").And.Contain("hello"));
            Assert.That(runtime.GetChannel(), Is.SameAs(before)); Assert.That(runtime.Get("hello"), Is.EqualTo("Hello"));
            provider.RedirectSecond = null; yield return Done(runtime.ReloadAsync());
            Assert.That(runtime.Get("other"), Is.EqualTo("Other"));
        }
        [UnityTest] public IEnumerator MissingBrokenAndWrongTypeReplacementPreserveActiveData()
        {
            var runtime = Runtime(); yield return Done(runtime.InitializeAsync()); var before = runtime.GetChannel();
            foreach (string file in new[] { "missing", "broken", "Selected" })
            {
                var replacement = new Probe { RedirectSecond = address + "/" + file };
                var registry = new LocalizationSourceRegistry(); registry.Register("resources", replacement);
                var failed = runtime.ReplaceProvidersAsync(registry); yield return Done(failed, false);
                Assert.That(failed.Exception.ToString(), Does.Contain(address + "/" + file));
                Assert.That(runtime.GetChannel(), Is.SameAs(before)); Assert.That(runtime.Get("hello"), Is.EqualTo("Hello"));
            }
            yield return Done(runtime.ReloadAsync()); Assert.That(runtime.Get("other"), Is.EqualTo("Other"));
        }
        [UnityTest] public IEnumerator ClosingOneRuntimeKeepsSharedProviderAndBorrowedTableUsable()
        {
            var first = Runtime(); var second = Runtime();
            yield return Done(first.InitializeAsync("ru")); yield return Done(second.InitializeAsync());
            var borrowed = Resources.Load<LocalizationAssetTable>(address + "/Selected");
            yield return Done(first.ReleaseAsync()); yield return Done(first.ReleaseAsync());
            Assert.That(borrowed != null, Is.True); Assert.That(second.Get("hello"), Is.EqualTo("Hello"));
            yield return Done(second.ReloadAsync()); Assert.That(Resources.Load<LocalizationAssetTable>(address + "/Selected"), Is.SameAs(borrowed));
        }
        [UnityTest] public IEnumerator CancelledNativePreparationKeepsStateAndAllowsReuse()
        {
            var runtime = Runtime(); yield return Done(runtime.InitializeAsync()); var before = runtime.GetChannel();
            using var cancel = new CancellationTokenSource(); var change = runtime.SwitchLanguageAsync("ru", cancel.Token); cancel.Cancel();
            yield return Done(change, false); Assert.That(change.IsCanceled, Is.True); Assert.That(runtime.GetChannel(), Is.SameAs(before));
            yield return Done(runtime.SwitchLanguageAsync("ru")); Assert.That(runtime.Get("hello"), Is.EqualTo("Hello"));
        }
        [UnityTest] public IEnumerator ReleaseDrainsPendingNativePreparationAndClosesRuntime()
        {
            var runtime = Runtime(); yield return Done(runtime.InitializeAsync());
            var change = runtime.SwitchLanguageAsync("ru"); var closing = runtime.ReleaseAsync();
            yield return Done(change, false); yield return Done(closing); Assert.That(change.IsCanceled, Is.True);
            Assert.Throws<ObjectDisposedException>(() => runtime.Get("hello"));
        }
        [UnityTest] public IEnumerator MissingInitialSourceReportsAddressWithoutPublishing()
        {
            provider.RedirectSecond = address + "/missing"; var runtime = Runtime();
            var initialization = runtime.InitializeAsync(); yield return Done(initialization, false);
            Assert.That(initialization.Exception.ToString(), Does.Contain(address + "/missing"));
            Assert.That(runtime.IsInitialized, Is.False);
            provider.RedirectSecond = null; yield return Done(runtime.InitializeAsync());
        }
        private static IEnumerator Done(Task task, bool success = true)
        {
            var timeout = System.Diagnostics.Stopwatch.StartNew();
            while (!task.IsCompleted && timeout.Elapsed.TotalSeconds < 15) yield return null;
            Assert.That(task.IsCompleted, Is.True, "Resource operation did not finish in 15 seconds.");
            if (success) Assert.That(task.IsCompletedSuccessfully, Is.True, task.Exception?.ToString());
            else Assert.That(task.IsCanceled || task.IsFaulted, Is.True);
        }
        private sealed class Probe : ILocalizationSourceProvider
        {
            private readonly ResourcesLocalizationSourceProvider inner = new();
            internal readonly List<string> Addresses = new();
            internal string RedirectSecond;
            public LocalizationProviderCapabilities GetCapabilities(LocalizationSourceKind kind) => inner.GetCapabilities(kind);
            public Task<LocalizationSourceLease> LoadAsync(LocalizationSourceRequest request, CancellationToken token)
            {
                if (RedirectSecond != null && request.Source.Address.EndsWith("/second", StringComparison.Ordinal))
                    request = new LocalizationSourceRequest(new("resources", RedirectSecond, request.Source.Kind),
                        request.Language, request.RequestedKeys, ownerId: request.OwnerId, ownerName: request.OwnerName, requestedLanguage: request.RequestedLanguage);
                Addresses.Add(request.Source.Address); return inner.LoadAsync(request, token);
            }
        }
    }
}
