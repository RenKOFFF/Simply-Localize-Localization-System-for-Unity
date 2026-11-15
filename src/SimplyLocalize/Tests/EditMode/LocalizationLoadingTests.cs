using System;
using System.Collections.Generic;
using NUnit.Framework;
using SimplyLocalize.Data;
using SimplyLocalize.Editor.Data;
using SimplyLocalize.Editor.Utilities;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SimplyLocalize.Tests
{
    public class LocalizationLoadingTests
    {
        private readonly List<Object> _objects = new();
        private LocalizationConfig _config;
        private LanguageProfile _en;
        private LanguageProfile _ru;
        private LanguageProfile _uk;
        private MemoryProvider _provider;
        private LocalizationManager Manager => Localization.Manager;

        [SetUp]
        public void SetUp()
        {
            Localization.Shutdown();
            _config = Create<LocalizationConfig>();
            _en = Profile("en");
            _ru = Profile("ru");
            _uk = Profile("uk");
            _config.fallbackLanguage = _en;
            _provider = new MemoryProvider();
            _provider.Text["en"] = new() { ["greeting"] = "Hello" };
            _provider.Text["ru"] = new() { ["greeting"] = "Привет" };
            _provider.Text["uk"] = new();
            Localization.Initialize(_config);
            Localization.SetDataProvider(_provider);
        }

        [TearDown]
        public void TearDown()
        {
            Localization.Shutdown();
            for (int i = _objects.Count - 1; i >= 0; i--)
                Object.DestroyImmediate(_objects[i]);
            _objects.Clear();
        }

        [Test]
        public void SetLanguage_PreloadsCurrentAndFallbackBeforeNotifying()
        {
            _uk.fallbackProfile = _ru;
            var events = new List<string>();
            Manager.OnLanguageChanged += code =>
            {
                events.Add(code);
                Assert.That(Manager.CurrentProfile, Is.SameAs(_uk));
                Assert.That(Manager.Get("greeting"), Is.EqualTo("Привет"));
                Assert.That(_provider.TextLoads.Keys, Is.EquivalentTo(new[] { "uk", "ru", "en" }));
            };
            Manager.OnProfileChanged += profile => events.Add(profile.Code + ":profile");

            Manager.SetLanguage(_uk);

            Assert.That(events, Is.EqualTo(new[] { "uk", "uk:profile" }));
            Assert.That(_provider.AssetLoads, Is.Empty, "Text-only users must not load asset tables.");
        }

        [Test]
        public void SetLanguage_SameLanguageDoesNotLoadOrNotifyAgain()
        {
            Manager.SetLanguage("ru");
            int notifications = 0;
            Manager.OnLanguageChanged += _ => notifications++;
            Manager.OnProfileChanged += _ => notifications++;
            Manager.SetLanguage("ru");
            Assert.That(_provider.TextLoads["ru"], Is.EqualTo(1));
            Assert.That(_provider.TextLoads["en"], Is.EqualTo(1));
            Assert.That(notifications, Is.Zero);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("unknown")]
        public void SetLanguage_InvalidCodePreservesState(string code)
        {
            Manager.SetLanguage("ru");
            int notifications = 0;
            Manager.OnLanguageChanged += _ => notifications++;
            Manager.SetLanguage(code);
            Assert.That(Manager.CurrentLanguage, Is.EqualTo("ru"));
            Assert.That(Manager.Get("greeting"), Is.EqualTo("Привет"));
            Assert.That(notifications, Is.Zero);
        }

        [TestCase("")]
        [TestCase(null)]
        public void Get_EmptyOrNullEntryUsesFallback_ButQueriesKeepEntrySemantics(string value)
        {
            _provider.Text["ru"]["greeting"] = value;
            Manager.SetLanguage("ru");
            Assert.That(Manager.Get("greeting"), Is.EqualTo("Hello"));
            Assert.That(Manager.HasTranslation("greeting", "ru"), Is.True);
            Assert.That(Manager.HasKey("greeting"), Is.True);
            Assert.That(Manager.GetAllKeys(), Does.Contain("greeting"));
        }

        [Test]
        public void Get_WhitespaceIsExplicitContent()
        {
            _provider.Text["ru"]["greeting"] = " \n";
            Manager.SetLanguage("ru");
            Assert.That(Manager.Get("greeting"), Is.EqualTo(" \n"));
        }

        [Test]
        public void Get_EmptyChainReturnsKey_WithoutChangingKeyQueries()
        {
            _provider.Text["ru"]["empty"] = "";
            _provider.Text["en"]["empty"] = "";
            Manager.SetLanguage("ru");
            Assert.That(Manager.Get("empty"), Is.EqualTo("empty"));
            Assert.That(Manager.HasKey("empty"), Is.True);
            Assert.That(Manager.HasKey("absent"), Is.False);
            Assert.That(Manager.HasTranslation("absent", "ru"), Is.False);
            Assert.That(Manager.Get(null), Is.Empty);
            Assert.That(Manager.Get(""), Is.Empty);
        }

        [TestCase("direct")]
        [TestCase("intermediate")]
        [TestCase("global")]
        public void Get_MatchesEditorFallbackResolution(string source)
        {
            _uk.fallbackProfile = _ru;
            _provider.Text["uk"]["greeting"] = source == "direct" ? "Вітаю" : "";
            _provider.Text["ru"]["greeting"] = source == "intermediate" ? "Привет" : "";
            var editorData = new EditorLocalizationData();
            foreach (var language in _provider.Text)
                foreach (var entry in language.Value)
                    editorData.SetTranslation(entry.Key, language.Key, entry.Value);
            Manager.SetLanguage("uk");
            var preview = FallbackResolver.ResolveTranslation(editorData, _config, "greeting", "uk");
            Assert.That(Manager.Get("greeting"), Is.EqualTo(preview.Value));
        }

        [Test]
        public void FallbackCycleTerminatesAndLoadsEachLanguageOnce()
        {
            _uk.fallbackProfile = _ru;
            _ru.fallbackProfile = _uk;
            _provider.Text["ru"].Clear();
            Manager.SetLanguage("uk");
            Assert.That(Manager.Get("greeting"), Is.EqualTo("Hello"));
            Assert.That(Manager.HasKey("greeting"), Is.True);
            Assert.That(Manager.GetAsset<Texture2D>("absent"), Is.Null);
            foreach (var loads in _provider.TextLoads.Values) Assert.That(loads, Is.EqualTo(1));
            foreach (var loads in _provider.AssetLoads.Values) Assert.That(loads, Is.EqualTo(1));
        }

        [Test]
        public void FallbackMatchingGlobalIsNotLoadedTwice()
        {
            _ru.fallbackProfile = _en;
            Manager.SetLanguage("ru");
            Assert.That(_provider.TextLoads["en"], Is.EqualTo(1));
            Assert.That(Manager.GetAsset<Texture2D>("absent"), Is.Null);
            Assert.That(_provider.AssetLoads["en"], Is.EqualTo(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Get_FormatsFallbackUsingItsSourceLanguage(bool intermediate)
        {
            _provider.Text["en"]["indexed"] = "{0} {0|coin|coins}";
            _provider.Text["en"]["named"] = "{count} {count|coin|coins}";
            _provider.Text["en"]["mixed"] = "{name}: {0} {0|coin|coins}";
            _provider.Text["en"]["noargs"] = "{0|one|other}";
            if (intermediate)
            {
                _ru.fallbackProfile = _en;
                _config.fallbackLanguage = _uk;
            }
            Manager.SetLanguage("ru");
            Assert.That(Manager.Get("indexed", new object[] { 21 }), Is.EqualTo("21 coins"));
            Assert.That(Manager.Get("named", new Dictionary<string, object> { ["count"] = 21 }), Is.EqualTo("21 coins"));
            Assert.That(Manager.Get("mixed", new object[] { 21 }, new Dictionary<string, object> { ["name"] = "Alex" }), Is.EqualTo("Alex: 21 coins"));
            // French differs from English at zero, exercising the parameterless overload too.
            var fr = Profile("fr");
            fr.fallbackProfile = _en;
            Manager.SetLanguage("fr");
            Assert.That(Manager.Get("noargs"), Is.EqualTo("other"));
            Assert.That(Manager.CurrentProfile, Is.SameAs(fr));
        }

        [Test]
        public void Get_DirectPluralAndExistingJsonFormatRemainSupported()
        {
            _provider.Text["ru"] = LocalizationFileParser.Parse(
                "{\"translations\":{\"coins\":\"{0} {0|монета|монеты|монет}\",\"hello\":\"Привет, {name}!\"}}");
            Manager.SetLanguage("ru");
            Assert.That(Localization.Get("coins", 21), Is.EqualTo("21 монета"));
            Assert.That(Localization.Get("hello", new Dictionary<string, object> { ["name"] = "Alex" }), Is.EqualTo("Привет, Alex!"));
        }

        [Test]
        public void Reload_ReplacesTextAndCachedAssetsBeforeNotifying()
        {
            var oldAsset = Texture();
            var newAsset = Texture();
            _provider.Tables["ru"] = new() { Table("icon", oldAsset) };
            Manager.SetLanguage("ru");
            Assert.That(Manager.GetAsset<Texture2D>("icon"), Is.SameAs(oldAsset));
            _provider.Text["ru"]["greeting"] = "Новый текст";
            _provider.Tables["ru"] = new() { Table("icon", newAsset) };
            int languageEvents = 0;
            int profileEvents = 0;
            Manager.OnLanguageChanged += code =>
            {
                languageEvents++;
                Assert.That(code, Is.EqualTo("ru"));
                Assert.That(Manager.Get("greeting"), Is.EqualTo("Новый текст"));
                Assert.That(Manager.GetAsset<Texture2D>("icon"), Is.SameAs(newAsset));
            };
            Manager.OnProfileChanged += _ => profileEvents++;

            Localization.Reload();
            Localization.Reload();

            Assert.That(Manager.CurrentProfile, Is.SameAs(_ru));
            Assert.That(languageEvents, Is.EqualTo(2));
            Assert.That(profileEvents, Is.EqualTo(2));
            Assert.That(_provider.TextLoads["ru"], Is.EqualTo(3));
            Assert.That(_provider.TextLoads["en"], Is.EqualTo(3));
        }

        [Test]
        public void Reload_RefreshesIntermediateAndGlobalFallbacks()
        {
            _uk.fallbackProfile = _ru;
            _provider.Text["en"]["global"] = "Old global";
            Manager.SetLanguage("uk");
            _provider.Text["ru"]["greeting"] = "Новый fallback";
            _provider.Text["en"]["global"] = "New global";
            Localization.Reload();
            Assert.That(Manager.Get("greeting"), Is.EqualTo("Новый fallback"));
            Assert.That(Manager.Get("global"), Is.EqualTo("New global"));
        }

        [TestCase("ru")]
        [TestCase("en")]
        public void Reload_TextLoadFailurePreservesLastSuccessfulState(string failingLanguage)
        {
            Manager.SetLanguage("ru");
            _provider.Text["ru"]["greeting"] = "Must not commit";
            _provider.ThrowText = failingLanguage;
            int events = 0;
            Manager.OnLanguageChanged += _ => events++;
            Manager.OnProfileChanged += _ => events++;
            Assert.Throws<InvalidOperationException>(() => Localization.Reload());
            Assert.That(Manager.CurrentLanguage, Is.EqualTo("ru"));
            Assert.That(Manager.Get("greeting"), Is.EqualTo("Привет"));
            Assert.That(events, Is.Zero);
            _provider.ThrowText = null;
            Localization.Reload();
            Assert.That(Manager.Get("greeting"), Is.EqualTo("Must not commit"));
        }

        [Test]
        public void Reload_CachedAssetLoadFailurePreservesTextAndAssets()
        {
            var original = Texture();
            _provider.Tables["ru"] = new() { Table("icon", original) };
            Manager.SetLanguage("ru");
            Manager.GetAsset<Texture2D>("icon");
            _provider.Text["ru"]["greeting"] = "Must not commit";
            _provider.ThrowAssets = "ru";
            Assert.Throws<InvalidOperationException>(() => Localization.Reload());
            Assert.That(Manager.Get("greeting"), Is.EqualTo("Привет"));
            Assert.That(Manager.GetAsset<Texture2D>("icon"), Is.SameAs(original));
        }

        [Test]
        public void SetLanguage_FailedFallbackLoadDoesNotCachePartialNewLanguage()
        {
            _config.fallbackLanguage = null;
            Manager.SetLanguage("en");
            _uk.fallbackProfile = _ru;
            _provider.Text["uk"]["greeting"] = "Partial";
            _provider.ThrowText = "ru";
            int events = 0;
            Manager.OnLanguageChanged += _ => events++;
            Assert.Throws<InvalidOperationException>(() => Manager.SetLanguage("uk"));
            Assert.That(Manager.CurrentLanguage, Is.EqualTo("en"));
            Assert.That(Manager.Get("greeting"), Is.EqualTo("Hello"));
            Assert.That(events, Is.Zero);
            _provider.Text["uk"]["greeting"] = "Recovered";
            _provider.ThrowText = null;
            Manager.SetLanguage("uk");
            Assert.That(Manager.Get("greeting"), Is.EqualTo("Recovered"));
        }

        [Test]
        public void SetDataProvider_ReplacesTextAndAssetsBeforeNotifying_AndDoesNotDisposeProvider()
        {
            var original = Texture();
            var replacement = Texture();
            _provider.Tables["ru"] = new() { Table("icon", original) };
            Manager.SetLanguage("ru");
            Manager.GetAsset<Texture2D>("icon");
            var next = new MemoryProvider();
            next.Text["ru"] = new() { ["greeting"] = "Replacement" };
            next.Tables["ru"] = new() { Table("icon", replacement) };
            int events = 0;
            Manager.OnLanguageChanged += _ =>
            {
                events++;
                Assert.That(Manager.Get("greeting"), Is.EqualTo("Replacement"));
                Assert.That(Manager.GetAsset<Texture2D>("icon"), Is.SameAs(replacement));
            };
            Localization.SetDataProvider(next);
            Assert.That(events, Is.EqualTo(1));
            Assert.That(Manager.CurrentLanguage, Is.EqualTo("ru"));
            Assert.That(_provider.Disposed, Is.False, "Providers remain caller-owned.");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SetDataProvider_FailurePreservesOldProviderAndCaches(bool assetFailure)
        {
            var original = Texture();
            _provider.Tables["ru"] = new() { Table("icon", original) };
            Manager.SetLanguage("ru");
            Manager.GetAsset<Texture2D>("icon");
            var next = new MemoryProvider();
            if (assetFailure) next.ThrowAssets = "ru";
            else next.ThrowText = "en";
            int events = 0;
            Manager.OnLanguageChanged += _ => events++;
            Assert.Throws<InvalidOperationException>(() => Manager.SetDataProvider(next));
            Assert.That(Manager.Get("greeting"), Is.EqualTo("Привет"));
            Assert.That(Manager.GetAsset<Texture2D>("icon"), Is.SameAs(original));
            Assert.That(events, Is.Zero);
            _provider.Text["ru"]["greeting"] = "Still old provider";
            Localization.Reload();
            Assert.That(Manager.Get("greeting"), Is.EqualTo("Still old provider"));
        }

        [Test]
        public void SetDataProvider_NullDoesNotChangeState()
        {
            Manager.SetLanguage("ru");
            Assert.Throws<ArgumentNullException>(() => Manager.SetDataProvider(null));
            Assert.That(Manager.Get("greeting"), Is.EqualTo("Привет"));
        }

        [Test]
        public void SetLanguage_AssetFailurePreservesSelectedLanguageAndAllowsRetry()
        {
            var original = Texture();
            _provider.Tables["en"] = new() { Table("icon", original) };
            Manager.SetLanguage("en");
            Manager.GetAsset<Texture2D>("icon");
            _provider.ThrowAssets = "ru";
            int events = 0;
            Manager.OnLanguageChanged += _ => events++;
            Manager.OnProfileChanged += _ => events++;
            Assert.Throws<InvalidOperationException>(() => Manager.SetLanguage("ru"));
            Assert.That(Manager.CurrentProfile, Is.SameAs(_en));
            Assert.That(Manager.Get("greeting"), Is.EqualTo("Hello"));
            Assert.That(Manager.GetAsset<Texture2D>("icon"), Is.SameAs(original));
            Assert.That(events, Is.Zero);
            _provider.Text["ru"]["greeting"] = "Recovered";
            _provider.ThrowAssets = null;
            Manager.SetLanguage("ru");
            Assert.That(Manager.Get("greeting"), Is.EqualTo("Recovered"));
            Assert.That(events, Is.EqualTo(2));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Reload_NullProviderResultDoesNotReplaceSuccessfulState(bool assets)
        {
            Manager.SetLanguage("ru");
            Manager.GetAsset<Texture2D>("icon");
            if (assets) _provider.NullAssets = "ru";
            else _provider.NullText = "ru";
            int events = 0;
            Manager.OnLanguageChanged += _ => events++;
            Assert.Throws<InvalidOperationException>(() => Localization.Reload());
            Assert.That(Manager.Get("greeting"), Is.EqualTo("Привет"));
            Assert.That(events, Is.Zero);
        }

        [Test]
        public void ProviderCollectionsAreSnapshotted_WithTheirKeyComparer()
        {
            _provider.ReturnSharedCollections = true;
            _provider.Text["ru"] = new(StringComparer.OrdinalIgnoreCase) { ["GREETING"] = "Original" };
            var original = Texture();
            _provider.Tables["ru"] = new() { Table("icon", original) };
            Manager.SetLanguage("ru");
            Manager.GetAsset<Texture2D>("icon");
            _provider.Text["ru"]["GREETING"] = "Changed";
            _provider.Tables["ru"].Clear();
            _provider.ThrowText = "en";
            Assert.Throws<InvalidOperationException>(() => Localization.Reload());
            Assert.That(Manager.Get("greeting"), Is.EqualTo("Original"));
            Assert.That(Manager.GetAsset<Texture2D>("icon"), Is.SameAs(original));
        }

        [Test]
        public void SetDataProvider_SameInstanceReloads_AndDropsInactiveLanguageCaches()
        {
            Manager.SetLanguage("uk");
            Manager.SetLanguage("ru");
            _provider.Text["uk"]["greeting"] = "Updated uk";
            _provider.Text["ru"]["greeting"] = "Updated ru";
            Manager.SetDataProvider(_provider);
            Assert.That(Manager.Get("greeting"), Is.EqualTo("Updated ru"));
            Manager.SetLanguage("uk");
            Assert.That(Manager.Get("greeting"), Is.EqualTo("Updated uk"));
        }

        [Test]
        public void TextOnlyReloadAndProviderChangeDoNotLoadAssets()
        {
            Manager.SetLanguage("ru");
            Manager.Reload();
            Manager.SetDataProvider(_provider);
            Assert.That(_provider.AssetLoads, Is.Empty);
        }

        [Test]
        public void GetAsset_NullTableDoesNotPreventFallback()
        {
            var expected = Texture();
            _provider.Tables["ru"] = new() { null };
            _provider.Tables["en"] = new() { Table("icon", expected) };
            Manager.SetLanguage("ru");
            Assert.That(Manager.GetAsset<Texture2D>("icon"), Is.SameAs(expected));
        }

        [Test]
        public void SetDataProvider_BeforeLanguageIsSelectedDoesNotLoadOrNotify()
        {
            var next = new MemoryProvider();
            int events = 0;
            Manager.OnLanguageChanged += _ => events++;
            Manager.SetDataProvider(next);
            Localization.Reload();
            Assert.That(next.TextLoads, Is.Empty);
            Assert.That(next.AssetLoads, Is.Empty);
            Assert.That(events, Is.Zero);
        }

        [Test]
        public void EmptyProviderIsValid_AndAllowsFallbackToKey()
        {
            Manager.SetLanguage("ru");
            Manager.SetDataProvider(new MemoryProvider());
            Assert.That(Manager.Get("greeting"), Is.EqualTo("greeting"));
            Assert.That(Manager.HasKey("greeting"), Is.False);
            Assert.That(Manager.GetAsset<Texture2D>("icon"), Is.Null);
        }

        [Test]
        public void ClearCaches_AllowsLazyReloadOfCurrentLanguageAndQueries()
        {
            Manager.SetLanguage("ru");
            Manager.ClearCaches();
            Assert.That(Manager.HasKey("greeting"), Is.True);
            Assert.That(Manager.Get("greeting"), Is.EqualTo("Привет"));
            Assert.That(_provider.TextLoads["ru"], Is.EqualTo(2));
        }

        [Test]
        public void GetAsset_SearchesMultipleTablesThenFallback_AndSkipsWrongType()
        {
            _uk.fallbackProfile = _ru;
            var expected = Texture();
            _provider.Tables["uk"] = new() { Table("other", Texture()), Table("icon", _en) };
            _provider.Tables["ru"] = new() { Table("other", Texture()), Table("icon", expected) };
            Manager.SetLanguage("uk");
            Assert.That(Manager.GetAsset<Texture2D>("icon"), Is.SameAs(expected));
            Assert.That(_provider.AssetLoads.ContainsKey("en"), Is.False);
        }

        [Test]
        public void Reload_WhenUninitializedIsSafe()
        {
            Localization.Shutdown();
            Assert.DoesNotThrow(Localization.Reload);
            Assert.That(Localization.IsInitialized, Is.False);
        }

        private T Create<T>() where T : ScriptableObject
        {
            var value = ScriptableObject.CreateInstance<T>();
            _objects.Add(value);
            return value;
        }

        private LanguageProfile Profile(string code)
        {
            var profile = Create<LanguageProfile>();
            profile.languageCode = code;
            _config.languages.Add(profile);
            return profile;
        }

        private Texture2D Texture()
        {
            var texture = new Texture2D(1, 1);
            _objects.Add(texture);
            return texture;
        }

        private LocalizationAssetTable Table(string key, Object asset)
        {
            var table = Create<LocalizationAssetTable>();
            table.Set(key, asset);
            return table;
        }

        private sealed class MemoryProvider : ILocalizationDataProvider, IDisposable
        {
            public readonly Dictionary<string, Dictionary<string, string>> Text = new();
            public readonly Dictionary<string, List<LocalizationAssetTable>> Tables = new();
            public readonly Dictionary<string, int> TextLoads = new();
            public readonly Dictionary<string, int> AssetLoads = new();
            public string ThrowText;
            public string ThrowAssets;
            public string NullText;
            public string NullAssets;
            public bool ReturnSharedCollections;
            public bool Disposed;

            public Dictionary<string, string> LoadTextData(string languageCode)
            {
                Count(TextLoads, languageCode);
                if (languageCode == ThrowText) throw new InvalidOperationException("Text load failed: " + languageCode);
                if (languageCode == NullText) return null;
                if (!Text.TryGetValue(languageCode, out var data)) return new();
                return ReturnSharedCollections ? data : new Dictionary<string, string>(data, data.Comparer);
            }

            public bool HasTextData(string languageCode) => Text.ContainsKey(languageCode);

            public List<LocalizationAssetTable> LoadAssetTables(string languageCode)
            {
                Count(AssetLoads, languageCode);
                if (languageCode == ThrowAssets) throw new InvalidOperationException("Asset load failed: " + languageCode);
                if (languageCode == NullAssets) return null;
                if (!Tables.TryGetValue(languageCode, out var tables)) return new();
                return ReturnSharedCollections ? tables : new List<LocalizationAssetTable>(tables);
            }

            public void Dispose() => Disposed = true;

            private static void Count(Dictionary<string, int> counts, string code)
            {
                counts.TryGetValue(code, out int count);
                counts[code] = count + 1;
            }
        }
    }
}
