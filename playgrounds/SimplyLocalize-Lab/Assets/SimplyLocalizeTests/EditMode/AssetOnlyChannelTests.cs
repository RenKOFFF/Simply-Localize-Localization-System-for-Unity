using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace SimplyLocalize.Tests
{
    public class AssetOnlyChannelTests
    {
        private readonly List<Object> objects = new();
        private readonly List<LocalizationRuntime> runtimes = new();
        private LocalizationProjectConfig config;
        [SetUp] public void Setup()
        {
            config = LocalizationProjectConfig.Create(new[] { "en", "ru" }); objects.Add(config);
            config.MainChannel.DefaultLanguage = "ru"; config.MainChannel.FallbackLanguage = "en";
            config.Sets[0].AddEntry("icon", LocalizationEntryKind.Asset);
            Sources(false);
        }
        private void Sources(bool text)
        {
            foreach (string code in new[] { "en", "ru" })
            {
                var descriptors = new List<LocalizationSourceReference> { new("test", "assets", LocalizationSourceKind.AssetTable) };
                if (text) descriptors.Add(new("test", "text", LocalizationSourceKind.TextTable));
                config.Sets[0].SetSources(code, descriptors.ToArray());
            }
        }
        private Provider Data()
        {
            var provider = new Provider(); var texture = new Texture2D(2, 2); objects.Add(texture);
            provider.Asset = texture; return provider;
        }
        private LocalizationRuntime Runtime(Provider provider)
        {
            var runtime = new LocalizationRuntime(config, provider.Registry()); runtimes.Add(runtime); return runtime;
        }
        private static void Completed(Task task)
        {
            Assert.That(task.IsCompleted, Is.True, "An in-memory provider must not suspend the test.");
            task.GetAwaiter().GetResult();
        }
        [TearDown] public void Cleanup()
        {
            foreach (var runtime in runtimes) Completed(runtime.ReleaseAsync()); runtimes.Clear();
            foreach (var value in objects) Object.DestroyImmediate(value); objects.Clear();
        }
        [Test] public void CommonSetCanExplicitlyContainBothTextAndAssets()
        {
            config.Sets[0].AddEntry("greeting"); Sources(true);
            var provider = Data(); var runtime = Runtime(provider); Completed(runtime.InitializeAsync());
            Assert.That(provider.TextCalls, Is.EqualTo(2)); Assert.That(runtime.Get("greeting"), Is.EqualTo("hello"));
            Assert.That(runtime.GetAsset<Texture2D>("icon"), Is.SameAs(provider.Asset));
        }
        [Test] public void AssetOnly_SwitchReloadAndProviderReplacement_NeverRequestText()
        {
            var provider = Data(); provider.RejectText = true; var runtime = Runtime(provider);
            Completed(runtime.InitializeAsync()); var original = runtime.GetAsset<Texture2D>("icon");
            Assert.That(original, Is.Not.Null); Assert.That(runtime.GetChannel().TryGetText("greeting", out _), Is.False);
            Completed(runtime.SwitchLanguageAsync("en")); Completed(runtime.SwitchLanguageAsync("ru")); Completed(runtime.ReloadAsync());
            Assert.That(runtime.GetAsset<Texture2D>("icon"), Is.SameAs(original));
            var replacement = Data(); replacement.RejectText = true; Completed(runtime.ReplaceProvidersAsync(replacement.Registry()));
            Assert.That(runtime.GetAsset<Texture2D>("icon"), Is.SameAs(replacement.Asset));
            Assert.That(provider.TextCalls + replacement.TextCalls, Is.Zero);
        }
        [Test] public void AssetOnly_FailedProviderReplacementPreservesActiveAssetsAndEvents()
        {
            var provider = Data(); provider.RejectText = true; var runtime = Runtime(provider);
            Completed(runtime.InitializeAsync()); var original = runtime.GetAsset<Texture2D>("icon");
            int events = 0; runtime.Changed += (_, _) => events++;
            var failing = Data(); failing.RejectText = true; failing.FailAssets = true;
            Assert.Throws<InvalidOperationException>(() => Completed(runtime.ReplaceProvidersAsync(failing.Registry())));
            Assert.That(runtime.GetChannel().RequestedLanguage, Is.EqualTo("ru")); Assert.That(events, Is.Zero);
            Assert.That(runtime.GetAsset<Texture2D>("icon"), Is.SameAs(original)); Assert.That(failing.TextCalls, Is.Zero);
        }
        [Test] public void EmptyAssetOnlySetDoesNotScanTextFoldersOrProduceWarnings()
        {
            config.Sets[0].SetSources("en"); config.Sets[0].SetSources("ru");
            var runtime = new LocalizationRuntime(config); runtimes.Add(runtime);
            Completed(runtime.InitializeAsync()); Completed(runtime.ReloadAsync());
            Assert.That(runtime.GetAsset<Texture2D>("icon"), Is.Null); LogAssert.NoUnexpectedReceived();
        }
        [UnityTest] public IEnumerator ExplicitMissingTextSourceProducesAnAddressedFailure()
        {
            config.Sets[0].AddEntry("greeting");
            string missing = "MissingChannelSource_" + Guid.NewGuid().ToString("N");
            config.Sets[0].SetSources("en");
            config.Sets[0].SetSources("ru", new LocalizationSourceReference("resources", missing, LocalizationSourceKind.TextTable));
            var runtime = new LocalizationRuntime(config); runtimes.Add(runtime);
            var task = runtime.InitializeAsync(); float deadline = Time.realtimeSinceStartup + 15;
            while (!task.IsCompleted && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(task.IsFaulted, Is.True); Assert.That(task.Exception.ToString(), Does.Contain(missing));
            Assert.That(runtime.IsInitialized, Is.False);
        }
        [Test] public void TextEnabled_ProviderErrorsAreNotHidden()
        {
            config.Sets[0].AddEntry("greeting"); Sources(true);
            var provider = Data(); provider.RejectText = true; var runtime = Runtime(provider);
            Assert.Throws<InvalidOperationException>(() => Completed(runtime.InitializeAsync()));
            Assert.That(runtime.IsInitialized, Is.False); Assert.That(provider.TextCalls, Is.EqualTo(1));
        }
        private sealed class Provider : ILocalizationSourceProvider
        {
            internal int TextCalls; internal bool RejectText, FailAssets; internal Texture2D Asset;
            internal LocalizationSourceRegistry Registry() { var registry = new LocalizationSourceRegistry(false); registry.Register("test", this); return registry; }
            public LocalizationProviderCapabilities GetCapabilities(LocalizationSourceKind kind) => new(LocalizationLoadUnit.Table, true, true);
            public Task<LocalizationSourceLease> LoadAsync(LocalizationSourceRequest request, CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                LocalizationSourceData data;
                if (request.Source.Kind == LocalizationSourceKind.TextTable)
                {
                    TextCalls++; if (RejectText) throw new InvalidOperationException("Text should not be requested");
                    data = LocalizationSourceData.FromText(new Dictionary<string, string> { ["greeting"] = "hello" });
                }
                else
                {
                    if (FailAssets) throw new InvalidOperationException("Asset load failure");
                    data = LocalizationSourceData.FromAssets(request.Language == "en"
                        ? new Dictionary<string, Object> { ["icon"] = Asset } : new Dictionary<string, Object>());
                }
                return Task.FromResult(new LocalizationSourceLease(data));
            }
        }
    }
}
