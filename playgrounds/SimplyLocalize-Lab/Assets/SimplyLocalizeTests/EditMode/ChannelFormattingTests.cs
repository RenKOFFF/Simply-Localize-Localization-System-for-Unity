using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using SimplyLocalize.Pluralization;
using SimplyLocalize.TextProcessing;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SimplyLocalize.Tests
{
    public class ChannelFormattingTests
    {
        private LocalizationProjectConfig config;
        private readonly List<LocalizationRuntime> runtimes = new();
        private readonly List<Object> objects = new();
        private readonly Provider provider = new();
        private readonly Dictionary<string, string> english = new();
        private readonly Dictionary<string, string> russian = new();
        private CultureInfo oldCulture;
        private Dictionary<string, IPluralRule> oldRules;
        [SetUp] public void Setup()
        {
            oldCulture = CultureInfo.CurrentCulture;
            oldRules = new((Dictionary<string, IPluralRule>)typeof(PluralRuleProvider).GetField("Overrides", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null));
            english.Clear(); russian.Clear(); provider.Loads = 0; provider.Assets.Clear(); provider.CleanupGate = null;
            provider.Text["en"] = english; provider.Text["ru"] = russian; provider.Text["x-demo"] = new();
        }
        [TearDown] public void Cleanup()
        {
            foreach (var runtime in runtimes) Assert.That(runtime.ReleaseAsync().IsCompletedSuccessfully, Is.True);
            runtimes.Clear();
            foreach (var value in objects) if (value != null) Object.DestroyImmediate(value); objects.Clear();
            if (config != null) Object.DestroyImmediate(config); config = null;
            CultureInfo.CurrentCulture = oldCulture;
            typeof(PluralRuleProvider).GetMethod("ResetRuntimeState", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
            foreach (var pair in oldRules) PluralRuleProvider.RegisterRule(pair.Key, pair.Value);
        }
        private LocalizationRuntime Create(string language = "ru", ILocalizationTextFormatter formatter = null, int maxTemplates = 1024)
        {
            if (config == null)
            {
                config = LocalizationProjectConfig.Create(new[] { "en", "ru", "x-demo" });
                foreach (string key in english.Keys.Concat(russian.Keys).Concat(provider.Text["x-demo"].Keys).Distinct()) config.Sets[0].AddEntry(key);
                if (provider.Assets.Count > 0) config.Sets[0].AddEntry("visual", LocalizationEntryKind.Asset);
                foreach (string code in new[] { "en", "ru", "x-demo" })
                {
                    var sources = new List<LocalizationSourceReference> { new("test", code, LocalizationSourceKind.TextTable) };
                    if (provider.Assets.ContainsKey(code)) sources.Add(new("test", code, LocalizationSourceKind.AssetTable));
                    config.Sets[0].SetSources(code, sources.ToArray());
                }
            }
            var registry = new LocalizationSourceRegistry(false); registry.Register("test", provider);
            var runtime = new LocalizationRuntime(config, registry, new LocalizationContextOptions { MaxTemplates = maxTemplates }, formatter);
            runtimes.Add(runtime);
            Assert.That(runtime.InitializeAsync(language).IsCompletedSuccessfully, Is.True, "The in-memory provider completes synchronously; tests do not block on async I/O.");
            return runtime;
        }
        [Test] public void PluralUsesResolvedTemplateLanguageAndEmptyTranslationsKeepFallback()
        {
            english["coins"] = "{0} {0|coin|coins}"; russian["coins"] = "";
            english["russian"] = "unused"; russian["russian"] = "{0|монета|монеты|монет}";
            var runtime = Create(); Assert.That(runtime.Get("coins", 21), Is.EqualTo("21 coins"));
            Assert.That(runtime.Get("russian", 21), Is.EqualTo("монета"));
            Assert.That(runtime.GetChannel().TryGetText("coins", out var value), Is.True); Assert.That(value.Language, Is.EqualTo("en"));
        }
        [TestCase(LocalizationValueCulture.ResolvedLanguage, "1.5 coins")]
        [TestCase(LocalizationValueCulture.RequestedLanguage, "1,5 coins")]
        [TestCase(LocalizationValueCulture.Invariant, "1.5 coins")]
        public void ValueCultureIsExplicitAndDoesNotChangePluralLanguage(LocalizationValueCulture culture, string expected)
        {
            english["key"] = "{0} {0|coin|coins}";
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            var runtime = Create(formatter: new LocalizationMessageFormatter(valueCulture: culture));
            Assert.That(runtime.Get("key", 1.5m), Is.EqualTo(expected));
            Assert.That(CultureInfo.CurrentCulture.Name, Is.EqualTo("de-DE"));
        }
        [Test] public void CurrentCultureAndGlobalOverridesCannotChangeRuntimeFormatting()
        {
            provider.Text["x-demo"]["key"] = "{0} {0|one|many}";
            PluralRuleProvider.RegisterRule("x-demo", new FixedRule(1));
            var first = Create("x-demo"); var second = Create("x-demo");
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");
            Assert.That(first.Get("key", 1), Is.EqualTo("1 one"));
            PluralRuleProvider.RegisterRule("x-demo", new FixedRule(0));
            Assert.That(second.Get("key", 2), Is.EqualTo("2 many"));
        }
        [Test] public void RuleRegistrationsAreCopiedAndTwoRuntimesCanUseDifferentRules()
        {
            english["key"] = "{0|one|many}";
            var rules = new LocalizationPluralRules(); rules.Register("en", new FixedRule(0));
            var custom = new LocalizationMessageFormatter(pluralRules: rules);
            rules.Register("en", new FixedRule(1));
            var first = Create("en", custom); var second = Create("en");
            Assert.That(first.Get("key", 2), Is.EqualTo("one")); Assert.That(second.Get("key", 2), Is.EqualTo("many"));
            Assert.That(custom.GetPluralRuleInfo("en").Support, Is.EqualTo(PluralRuleSupport.Custom));
        }
        [Test] public void RegionalRuleOverridesUseTheMostSpecificInstanceRegistration()
        {
            var rules = new LocalizationPluralRules(); var parent = new FixedRule(0); var exact = new FixedRule(1);
            rules.Register("pt", parent); rules.Register("PT_pt", exact);
            Assert.That(rules.GetRuleInfo("pt-PT").Rule, Is.SameAs(exact)); Assert.That(rules.GetRuleInfo("pt-BR").Rule, Is.SameAs(parent));
            Assert.That(new LocalizationPluralRules().GetRuleInfo("pt-PT").Rule.Evaluate(0), Is.EqualTo(1));
            Assert.That(new LocalizationPluralRules().GetRuleInfo("pt-BR").Rule.Evaluate(0), Is.EqualTo(0));
        }
        [TestCase("{{value}}", "{value}")]
        [TestCase("{{{0}}}", "{Ada}")]
        [TestCase("bad {unclosed", "bad {unclosed")]
        [TestCase("{missing } {0}", "{missing } Ada")]
        public void ExistingTemplateSyntaxRemainsAvailableThroughRuntime(string input, string expected)
        { english["key"] = input; Assert.That(Create("en").Get("key", "Ada"), Is.EqualTo(expected)); }
        [Test] public void NamedIndexedAndSelectorsUseTheSameReadyPath()
        {
            english["key"] = "{name}: {0} {0|coin|coins}, {gender|he|she}";
            var runtime = Create("en"); int loads = provider.Loads;
            var named = new Dictionary<string, object> { ["name"] = "Ada", ["gender"] = new FormSelector(1) };
            Assert.That(runtime.Format("key", new object[] { 2 }, named), Is.EqualTo("Ada: 2 coins, she"));
            var reference = new LocalizationEntryReference(config.MainChannelId, config.Sets[0].Entries[0].Id);
            Assert.That(runtime.Format(reference, new object[] { 2 }, named), Is.EqualTo("Ada: 2 coins, she"));
            Assert.That(provider.Loads, Is.EqualTo(loads));
        }
        [Test] public void MissingKeysStayLiteralAndNeverStartIO()
        {
            english["known"] = "Known"; var runtime = Create(); int loads = provider.Loads;
            Assert.That(runtime.Get("missing/{0}", 42), Is.EqualTo("missing/{0}")); Assert.That(runtime.Get(null), Is.Empty);
            Assert.That(provider.Loads, Is.EqualTo(loads));
        }
        [Test] public void BoundedCacheEvictsWithoutChangingMessagesAndReloadUsesNewContent()
        {
            for (int i = 0; i < 10; i++) english["key" + i] = "message " + i + " {0}";
            var runtime = Create("en", maxTemplates: 2); var formatter = (LocalizationMessageFormatter)runtime.Formatter;
            for (int i = 0; i < 10; i++) { Assert.That(runtime.Get("key" + i, 7), Is.EqualTo("message " + i + " 7")); Assert.That(formatter.CachedTemplateCount, Is.LessThanOrEqualTo(2)); }
            english["key0"] = "changed {0}"; Assert.That(runtime.ReloadAsync().IsCompletedSuccessfully, Is.True);
            Assert.That(runtime.Get("key0", 8), Is.EqualTo("changed 8")); Assert.That(formatter.CachedTemplateCount, Is.EqualTo(2));
        }
        [Test] public void CustomFormatterNeedsNoForkAndReceivesResolvedLanguage()
        {
            english["key"] = "value"; var formatter = new CustomFormatter(); var runtime = Create(formatter: formatter);
            Assert.That(runtime.Get("key"), Is.EqualTo("ru -> en: value")); Assert.That(formatter.Calls, Is.EqualTo(1));
        }
        [Test] public void ReleaseClearsOwnedTemplateCacheButDoesNotMutateACallerProvidedFormatter()
        {
            english["key"] = "message {0}";
            var owned = Create("en"); owned.Get("key", 1); var ownedFormatter = (LocalizationMessageFormatter)owned.Formatter;
            Assert.That(ownedFormatter.CachedTemplateCount, Is.EqualTo(1));
            provider.CleanupGate = new TaskCompletionSource<bool>(); var closing = owned.ReleaseAsync();
            var observed = ObserveCleanup(); provider.CleanupGate.SetResult(true);
            Assert.That(observed.IsCompletedSuccessfully, Is.True); Assert.That(observed.Result, Is.Zero, "Awaiting Release must see an already-cleared cache.");
            provider.CleanupGate = null;
            var shared = new LocalizationMessageFormatter(); var borrowed = Create("en", shared); borrowed.Get("key", 2);
            Assert.That(borrowed.ReleaseAsync().IsCompletedSuccessfully, Is.True); Assert.That(shared.CachedTemplateCount, Is.EqualTo(1));
            shared.ClearCache();
            async Task<int> ObserveCleanup() { await closing; return ownedFormatter.CachedTemplateCount; }
        }
        [Test] public void ReentrantFormattingDoesNotCorruptTheOuterMessage()
        {
            english["outer"] = "before [{0}] after"; english["inner"] = "inner {0}";
            var runtime = Create("en", maxTemplates: 1);
            Assert.That(runtime.Get("outer", new Nested(() => runtime.Get("inner", 7))), Is.EqualTo("before [inner 7] after"));
            Assert.Throws<InvalidOperationException>(() => runtime.Get("outer", new Nested(() => throw new InvalidOperationException("probe"))));
            Assert.That(runtime.Get("inner", 8), Is.EqualTo("inner 8"));
        }
        [Test] public void TypedAssetLookupContinuesFallbackAfterATypeMismatch()
        {
            var texture = new Texture2D(1, 1); var clip = AudioClip.Create("fallback", 8, 1, 8000, false); objects.Add(texture); objects.Add(clip);
            provider.Assets["ru"] = new() { ["visual"] = texture }; provider.Assets["en"] = new() { ["visual"] = clip };
            var runtime = Create(); int loads = provider.Loads;
            Assert.That(runtime.GetAsset<Texture2D>("visual"), Is.SameAs(texture)); Assert.That(runtime.GetAsset<AudioClip>("visual"), Is.SameAs(clip));
            Assert.That(runtime.GetChannel().TryGetAsset<AudioClip>("visual", out var value), Is.True); Assert.That(value.Language, Is.EqualTo("en"));
            using var owner = value.RetainSource(); Assert.That(owner.Data.Assets["visual"], Is.SameAs(clip)); Assert.That(provider.Loads, Is.EqualTo(loads));
        }
        private sealed class FixedRule : IPluralRule
        { private readonly int form; internal FixedRule(int form) => this.form = form; public int FormCount => 2; public int Evaluate(int count) => form; }
        private sealed class Nested
        { private readonly Func<string> value; internal Nested(Func<string> value) => this.value = value; public override string ToString() => value(); }
        private sealed class CustomFormatter : ILocalizationTextFormatter
        { internal int Calls; public string Format(LocalizationTextValue value, object[] args, Dictionary<string, object> namedArgs) { Calls++; return value.RequestedLanguage + " -> " + value.Language + ": " + value.Text; } }
        private sealed class Provider : ILocalizationSourceProvider
        {
            internal int Loads;
            internal TaskCompletionSource<bool> CleanupGate;
            internal readonly Dictionary<string, Dictionary<string, string>> Text = new();
            internal readonly Dictionary<string, Dictionary<string, Object>> Assets = new();
            public LocalizationProviderCapabilities GetCapabilities(LocalizationSourceKind kind) => new(LocalizationLoadUnit.Table, false, true);
            public Task<LocalizationSourceLease> LoadAsync(LocalizationSourceRequest request, CancellationToken token)
            {
                Loads++;
                return Task.FromResult(new LocalizationSourceLease(request.Source.Kind == LocalizationSourceKind.TextTable
                    ? LocalizationSourceData.FromText(Text[request.Language]) : LocalizationSourceData.FromAssets(Assets[request.Language]),
                    () => CleanupGate?.Task ?? Task.CompletedTask));
            }
        }
    }
}
