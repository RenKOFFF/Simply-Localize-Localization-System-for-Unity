using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using SimplyLocalize.Data;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SimplyLocalize.Tests
{
    public class ChannelLoadingTests
    {
        private LocalizationProjectConfig config;
        private LocalizationRuntime runtime;
        private MemoryProvider provider;
        private readonly List<Object> objects = new();
        private readonly List<MemoryProvider> providers = new();
        [SetUp] public void Setup()
        {
            config = LocalizationProjectConfig.Create(new[] { "en", "ru", "uk" }); objects.Add(config);
            config.MainChannel.FallbackLanguage = "en";
            config.MainChannel.SetFallback("uk", "ru");
            foreach (string key in new[] { "greeting", "global", "empty", "coins", "hello", "GREETING" })
                config.Sets[0].AddEntry(key);
            config.Sets[0].AddEntry("icon", LocalizationEntryKind.Asset);
            SetSources(true);
            provider = Provider();
            provider.Text["en"] = new() { ["greeting"] = "Hello" };
            provider.Text["ru"] = new() { ["greeting"] = "Привет" };
            provider.Text["uk"] = new();
        }
        [TearDown] public void Cleanup()
        {
            if (runtime != null) Complete(runtime.ReleaseAsync()); runtime = null;
            foreach (var value in providers)
            {
                Assert.That(value.Active, Is.Zero, "All partial and committed source owners must be released.");
                Assert.That(value.Disposed, Is.False, "Source providers belong to their caller.");
            }
            providers.Clear();
            foreach (var value in objects) if (value != null) Object.DestroyImmediate(value);
            objects.Clear();
        }
        private MemoryProvider Provider() { var value = new MemoryProvider(); providers.Add(value); return value; }
        private static LocalizationSourceRegistry Registry(MemoryProvider value)
        { var registry = new LocalizationSourceRegistry(false); registry.Register("memory", value); return registry; }
        private void SetSources(bool assets)
        {
            foreach (string code in new[] { "en", "ru", "uk" })
            {
                var sources = new List<LocalizationSourceReference> { new("memory", "text", LocalizationSourceKind.TextTable) };
                if (assets) sources.Add(new("memory", "assets", LocalizationSourceKind.AssetTable));
                config.Sets[0].SetSources(code, sources.ToArray());
            }
        }
        private void Create(string language = "ru")
        { runtime = new LocalizationRuntime(config, Registry(provider)); Complete(runtime.InitializeAsync(language)); }
        private static void Complete(Task task)
        {
            Assert.That(task.IsCompleted, Is.True, "In-memory operations must not suspend; never block Unity I/O.");
            task.GetAwaiter().GetResult();
        }
        private static void Fails(Task task)
        {
            Assert.That(task.IsCompleted, Is.True);
            Assert.That(task.IsFaulted, Is.True, task.Exception?.ToString());
            _ = task.Exception;
        }
        private Texture2D Texture() { var value = new Texture2D(1, 1); objects.Add(value); return value; }
        [Test] public void InitializationPreparesWholeFallbackBeforeNotifying()
        {
            SetSources(false); runtime = new LocalizationRuntime(config, Registry(provider)); int changes = 0;
            runtime.Changed += (value, _) =>
            {
                changes++;
                Assert.That(value.GetChannel().RequestedLanguage, Is.EqualTo("uk"));
                Assert.That(value.Get("greeting"), Is.EqualTo("Привет"));
                Assert.That(provider.TextLoads.Keys, Is.EquivalentTo(new[] { "uk", "ru", "en" }));
            };
            Complete(runtime.InitializeAsync("uk")); Assert.That(changes, Is.EqualTo(1));
            Assert.That(provider.AssetLoads, Is.Empty);
        }
        [TestCase("")]
        [TestCase("unknown")]
        public void InvalidSelectionRejectsWithoutChangingState(string code)
        {
            Create(); var before = runtime.GetChannel(); int changes = 0; runtime.Changed += (_, _) => changes++;
            Assert.Throws<ArgumentException>(() => runtime.SwitchLanguageAsync(code));
            Assert.That(runtime.GetChannel(), Is.SameAs(before)); Assert.That(changes, Is.Zero);
        }
        [Test] public void NullSelectionUsesConfiguredDefault()
        {
            Create(); Complete(runtime.SwitchLanguageAsync(null));
            Assert.That(runtime.GetChannel().RequestedLanguage, Is.EqualTo("en"));
        }
        [TestCase("")]
        [TestCase(null)]
        public void EmptyEntryUsesFallbackAndReportsResolvedLanguage(string content)
        {
            provider.Text["ru"]["greeting"] = content; Create();
            Assert.That(runtime.Get("greeting"), Is.EqualTo("Hello"));
            Assert.That(runtime.GetChannel().TryGetText("greeting", out var found), Is.True);
            Assert.That(found.Language, Is.EqualTo("en")); Assert.That(found.RequestedLanguage, Is.EqualTo("ru"));
        }
        [Test] public void WhitespaceIsExplicitContent()
        {
            provider.Text["ru"]["greeting"] = " \n"; Create();
            Assert.That(runtime.Get("greeting"), Is.EqualTo(" \n"));
        }
        [Test] public void EmptyChainAndMissingKeysReturnLiteralKeysWithoutIO()
        {
            provider.Text["ru"]["empty"] = ""; provider.Text["en"]["empty"] = ""; Create();
            int reads = provider.Reads;
            Assert.That(runtime.Get("empty"), Is.EqualTo("empty"));
            Assert.That(runtime.Get("absent{0}", 5), Is.EqualTo("absent{0}"));
            Assert.That(runtime.GetChannel().TryGetText("empty", out _), Is.False);
            Assert.That(runtime.Get((string)null), Is.Empty); Assert.That(runtime.Get(""), Is.Empty);
            Assert.That(provider.Reads, Is.EqualTo(reads));
        }
        [TestCase("uk", "Вітаю")]
        [TestCase("ru", "Привет")]
        [TestCase("en", "Hello")]
        public void FallbackChoosesFirstNonEmptyTranslation(string source, string expected)
        {
            provider.Text["uk"]["greeting"] = source == "uk" ? "Вітаю" : "";
            provider.Text["ru"]["greeting"] = source == "ru" ? "Привет" : "";
            Create("uk"); Assert.That(runtime.Get("greeting"), Is.EqualTo(expected));
            Assert.That(runtime.GetChannel().TryGetText("greeting", out var found), Is.True);
            Assert.That(found.Language, Is.EqualTo(source));
        }
        [Test] public void FallbackMatchingGlobalIsNotLoadedTwice()
        {
            config.MainChannel.SetFallback("ru", "en"); Create();
            Assert.That(provider.TextLoads["en"], Is.EqualTo(1));
            Assert.That(provider.AssetLoads["en"], Is.EqualTo(1));
        }
        [Test] public void ExistingJsonAndDirectPluralRemainSupported()
        {
            provider.Text["ru"] = LocalizationFileParser.Parse(
                "{\"translations\":{\"coins\":\"{0} {0|монета|монеты|монет}\",\"hello\":\"Привет, {name}!\"}}");
            Create();
            Assert.That(runtime.Get("coins", 21), Is.EqualTo("21 монета"));
            Assert.That(runtime.Get("hello", new Dictionary<string, object> { ["name"] = "Alex" }), Is.EqualTo("Привет, Alex!"));
        }
        [Test] public void ReloadPublishesNewTextAndAssetsTogetherEveryTime()
        {
            var old = Texture(); var next = Texture(); provider.Assets["ru"] = new() { ["icon"] = old }; Create();
            provider.Text["ru"]["greeting"] = "Новый текст"; provider.Assets["ru"]["icon"] = next;
            int changes = 0; runtime.Changed += (value, _) =>
            {
                changes++; Assert.That(value.Get("greeting"), Is.EqualTo("Новый текст"));
                Assert.That(value.GetAsset<Texture2D>("icon"), Is.SameAs(next));
            };
            Complete(runtime.ReloadAsync()); Complete(runtime.ReloadAsync());
            Assert.That(changes, Is.EqualTo(2)); Assert.That(provider.TextLoads["ru"], Is.EqualTo(3));
            Assert.That(provider.TextLoads["en"], Is.EqualTo(3));
        }
        [Test] public void ReloadRefreshesIntermediateAndGlobalFallback()
        {
            provider.Text["en"]["global"] = "Old global"; Create("uk");
            provider.Text["ru"]["greeting"] = "Новый fallback"; provider.Text["en"]["global"] = "New global";
            Complete(runtime.ReloadAsync()); Assert.That(runtime.Get("greeting"), Is.EqualTo("Новый fallback"));
            Assert.That(runtime.Get("global"), Is.EqualTo("New global"));
        }
        [TestCase("ru", false)]
        [TestCase("en", false)]
        [TestCase("ru", true)]
        [TestCase("en", true)]
        public void ReloadFailurePreservesWholeSnapshotAndAllowsRetry(string language, bool assets)
        {
            var original = Texture(); provider.Assets["ru"] = new() { ["icon"] = original }; Create();
            var before = runtime.GetChannel(); int owners = provider.Active, changes = 0;
            runtime.Changed += (_, _) => changes++;
            provider.Text["ru"]["greeting"] = "After retry"; provider.FailLanguage = language; provider.FailAssets = assets;
            Fails(runtime.ReloadAsync());
            Assert.That(runtime.GetChannel(), Is.SameAs(before)); Assert.That(runtime.Get("greeting"), Is.EqualTo("Привет"));
            Assert.That(runtime.GetAsset<Texture2D>("icon"), Is.SameAs(original));
            Assert.That(provider.Active, Is.EqualTo(owners)); Assert.That(changes, Is.Zero);
            provider.FailLanguage = null; Complete(runtime.ReloadAsync());
            Assert.That(runtime.Get("greeting"), Is.EqualTo("After retry")); Assert.That(changes, Is.EqualTo(1));
        }
        [Test] public void FailedFallbackDoesNotCachePartialLanguage()
        {
            Create("en"); provider.Text["uk"]["greeting"] = "Partial"; provider.FailLanguage = "ru";
            Fails(runtime.SwitchLanguageAsync("uk")); Assert.That(runtime.Get("greeting"), Is.EqualTo("Hello"));
            provider.Text["uk"]["greeting"] = "Recovered"; provider.FailLanguage = null;
            Complete(runtime.SwitchLanguageAsync("uk")); Assert.That(runtime.Get("greeting"), Is.EqualTo("Recovered"));
        }
        [Test] public void ReplacementPublishesTextAndAssetsTogetherWithoutDisposingProviders()
        {
            var original = Texture(); var replacement = Texture(); provider.Assets["ru"] = new() { ["icon"] = original }; Create();
            var next = Provider(); next.Text["ru"] = new() { ["greeting"] = "Replacement" }; next.Assets["ru"] = new() { ["icon"] = replacement };
            int changes = 0; runtime.Changed += (value, _) =>
            {
                changes++; Assert.That(value.Get("greeting"), Is.EqualTo("Replacement"));
                Assert.That(value.GetAsset<Texture2D>("icon"), Is.SameAs(replacement));
            };
            Complete(runtime.ReplaceProvidersAsync(Registry(next))); Assert.That(changes, Is.EqualTo(1));
            Assert.That(provider.Active, Is.Zero); Assert.That(provider.Disposed, Is.False);
        }
        [TestCase(false)]
        [TestCase(true)]
        public void ReplacementFailureKeepsOriginalProviderForNextReload(bool assets)
        {
            var original = Texture(); provider.Assets["ru"] = new() { ["icon"] = original }; Create();
            var before = runtime.GetChannel(); var next = Provider(); next.FailLanguage = assets ? "ru" : "en"; next.FailAssets = assets;
            Fails(runtime.ReplaceProvidersAsync(Registry(next)));
            Assert.That(runtime.GetChannel(), Is.SameAs(before)); Assert.That(next.Active, Is.Zero);
            provider.Text["ru"]["greeting"] = "Still original"; Complete(runtime.ReloadAsync());
            Assert.That(runtime.Get("greeting"), Is.EqualTo("Still original"));
            Assert.That(runtime.GetAsset<Texture2D>("icon"), Is.SameAs(original));
        }
        [Test] public void NullRegistryRejectsWithoutChangingState()
        {
            Create(); var before = runtime.GetChannel();
            Assert.Throws<ArgumentNullException>(() => runtime.ReplaceProvidersAsync(null));
            Assert.That(runtime.GetChannel(), Is.SameAs(before));
        }
        [TestCase(false)]
        [TestCase(true)]
        public void NullLeaseDoesNotReplaceSuccessfulState(bool assets)
        {
            Create(); var before = runtime.GetChannel(); provider.NullLanguage = "ru"; provider.FailAssets = assets;
            Fails(runtime.ReloadAsync()); Assert.That(runtime.GetChannel(), Is.SameAs(before));
        }
        [Test] public void SourceBuffersAreCopiedAndKeysStayCaseSensitiveRegardlessOfProviderComparer()
        {
            provider.Text["ru"] = new(StringComparer.OrdinalIgnoreCase) { ["GREETING"] = "Original" };
            var original = Texture(); provider.Assets["ru"] = new() { ["icon"] = original }; Create();
            provider.Text["ru"]["GREETING"] = "Mutated"; provider.Assets["ru"].Clear();
            provider.FailLanguage = "en"; Fails(runtime.ReloadAsync());
            Assert.That(runtime.Get("GREETING"), Is.EqualTo("Original"));
            Assert.That(runtime.Get("greeting"), Is.EqualTo("Hello"), "Key identity must not depend on the provider's comparer.");
            Assert.That(runtime.GetAsset<Texture2D>("icon"), Is.SameAs(original));
        }
        [Test] public void SameProviderReplacementReloadsAndNoInactiveLanguageCacheSurvives()
        {
            Create("uk"); Complete(runtime.SwitchLanguageAsync("ru"));
            provider.Text["uk"]["greeting"] = "Updated uk"; provider.Text["ru"]["greeting"] = "Updated ru";
            Complete(runtime.ReplaceProvidersAsync(Registry(provider)));
            Assert.That(runtime.Get("greeting"), Is.EqualTo("Updated ru"));
            Complete(runtime.SwitchLanguageAsync("uk")); Assert.That(runtime.Get("greeting"), Is.EqualTo("Updated uk"));
        }
        [Test] public void TextOnlyReloadReplacementAndAssetQueryNeverLoadAssetSources()
        {
            SetSources(false); Create(); Complete(runtime.ReloadAsync()); Complete(runtime.ReplaceProvidersAsync(Registry(provider)));
            Assert.That(runtime.GetAsset<Texture2D>("icon"), Is.Null); Assert.That(provider.AssetLoads, Is.Empty);
        }
        [Test] public void EmptyOrWrongTypeAssetDoesNotBlockTypedFallback()
        {
            var expected = Texture(); provider.Assets["uk"] = new() { ["icon"] = null };
            provider.Assets["ru"] = new() { ["icon"] = config }; provider.Assets["en"] = new() { ["icon"] = expected };
            Create("uk"); Assert.That(runtime.GetAsset<Texture2D>("icon"), Is.SameAs(expected));
            Assert.That(runtime.GetChannel().TryGetAsset<Texture2D>("icon", out var found), Is.True);
            Assert.That(found.Language, Is.EqualTo("en"));
        }
        [Test] public void ConstructionReadsNothingAndReadyOperationsRequireInitialization()
        {
            runtime = new LocalizationRuntime(config, Registry(provider));
            Assert.Throws<InvalidOperationException>(() => runtime.Get("greeting"));
            Assert.Throws<InvalidOperationException>(() => runtime.ReloadAsync());
            Assert.Throws<InvalidOperationException>(() => runtime.ReplaceProvidersAsync(Registry(provider)));
            Assert.That(provider.Reads, Is.Zero);
        }
        [Test] public void EmptyProviderIsValidAndFallsBackToLiteralKey()
        {
            Create(); Complete(runtime.ReplaceProvidersAsync(Registry(Provider())));
            Assert.That(runtime.Get("greeting"), Is.EqualTo("greeting"));
            Assert.That(runtime.GetChannel().TryGetText("greeting", out _), Is.False);
            Assert.That(runtime.GetAsset<Texture2D>("icon"), Is.Null);
        }
        [Test] public void FacadeReloadRequiresInitialization()
        {
            Complete(Localization.ShutdownAsync());
            Assert.Throws<InvalidOperationException>(() => Localization.ReloadAsync());
            Assert.That(Localization.IsInitialized, Is.False);
        }
        private sealed class MemoryProvider : ILocalizationSourceProvider, IDisposable
        {
            internal readonly Dictionary<string, Dictionary<string, string>> Text = new();
            internal readonly Dictionary<string, Dictionary<string, Object>> Assets = new();
            internal readonly Dictionary<string, int> TextLoads = new(), AssetLoads = new();
            internal string FailLanguage, NullLanguage;
            internal bool FailAssets, Disposed;
            internal int Active, Reads;
            public LocalizationProviderCapabilities GetCapabilities(LocalizationSourceKind kind) => new(LocalizationLoadUnit.Table, false, false);
            public Task<LocalizationSourceLease> LoadAsync(LocalizationSourceRequest request, CancellationToken token)
            {
                Reads++; bool assets = request.Source.Kind == LocalizationSourceKind.AssetTable;
                var counts = assets ? AssetLoads : TextLoads;
                counts.TryGetValue(request.Language, out int count); counts[request.Language] = count + 1;
                if (request.Language == FailLanguage && assets == FailAssets) throw new InvalidOperationException("Injected source failure");
                if (request.Language == NullLanguage && assets == FailAssets) return Task.FromResult<LocalizationSourceLease>(null);
                LocalizationSourceData data;
                if (assets) data = LocalizationSourceData.FromAssets(Assets.TryGetValue(request.Language, out var entries) ? entries : new());
                else data = LocalizationSourceData.FromText(Text.TryGetValue(request.Language, out var entries) ? entries : new());
                Active++; return Task.FromResult(new LocalizationSourceLease(data, () => { Active--; return Task.CompletedTask; }));
            }
            public void Dispose() => Disposed = true;
        }
    }
}
