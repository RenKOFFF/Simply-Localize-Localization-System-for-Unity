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
    public class LifecycleDiagnosticsTests
    {
        private readonly List<Object> _objects = new();
        private LocalizationConfig _config;
        private LocalizationProjectConfig _project;
        private Action<string> _languageHandler;
        private Action<LocalizationRuntime> _profileHandler;

        [SetUp]
        public void SetUp()
        {
            Completed(Localization.ShutdownAsync());
            _config = Create<LocalizationConfig>();
            foreach (var code in new[] { "en", "ru" })
            {
                var profile = Create<LanguageProfile>();
                profile.languageCode = code;
                _config.languages.Add(profile);
            }
            _project ??= LocalizationProjectConfig.Create(new[] { "en", "ru" });
            Completed(Localization.InitializeAsync(_project));
        }

        [TearDown]
        public void TearDown()
        {
            Localization.OnLanguageChanged -= _languageHandler;
            Localization.OnRuntimeChanged -= _profileHandler;
            Completed(Localization.ShutdownAsync());
            Object.DestroyImmediate(_project); _project = null;
            foreach (var obj in _objects) Object.DestroyImmediate(obj);
            _objects.Clear();
        }

        [Test]
        public void Reinitialize_DetachesOldManagerButKeepsFacadeSubscribers()
        {
            var old = Localization.Runtime;
            int languages = 0, profiles = 0;
            _languageHandler = _ => languages++;
            _profileHandler = _ => profiles++;
            Localization.OnLanguageChanged += _languageHandler;
            Localization.OnRuntimeChanged += _profileHandler;
            _project ??= LocalizationProjectConfig.Create(new[] { "en", "ru" });
            Completed(Localization.InitializeAsync(_project));
            Assert.That(old.IsReleased, Is.True);
            Assert.That(languages, Is.EqualTo(1));
            Assert.That(profiles, Is.EqualTo(1));
            Assert.That(Localization.CurrentLanguage, Is.EqualTo("en"));
        }

        [Test]
        public void Shutdown_DetachesForwardingFromRetainedManager()
        {
            var old = Localization.Runtime;
            int events = 0;
            _languageHandler = _ => events++;
            Localization.OnLanguageChanged += _languageHandler;
            Completed(Localization.ShutdownAsync());
            Assert.That(old.IsReleased, Is.True);
            Assert.That(events, Is.Zero);
        }

        [Test]
        public void InitializeUnknownLanguage_LeavesCurrentManagerIntactOnThrow()
        {
            var old = Localization.Runtime;
            Assert.Throws<ArgumentException>(() => Localization.InitializeAsync(_project, "missing"));
            Assert.That(Localization.Runtime, Is.SameAs(old));
            Assert.That(Localization.CurrentLanguage, Is.EqualTo("en"));
        }

        [Test]
        public void DuplicateCodes_AreRejectedBeforeReplacingCurrentManager()
        {
            var bad = Create<LocalizationProjectConfig>();
            var serialized = new UnityEditor.SerializedObject(bad);
            var languages = serialized.FindProperty("_languages");
            languages.arraySize = 2;
            languages.GetArrayElementAtIndex(0).FindPropertyRelative("_code").stringValue = "en";
            languages.GetArrayElementAtIndex(1).FindPropertyRelative("_code").stringValue = "en";
            serialized.ApplyModifiedPropertiesWithoutUndo();
            var old = Localization.Runtime;
            var error = Assert.Throws<ArgumentException>(() => Localization.InitializeAsync(bad));
            StringAssert.Contains("en", error.Message);
            Assert.That(Localization.Runtime, Is.SameAs(old));        }

        [TestCase(null)]
        [TestCase("../Outside")]
        [TestCase("C:/Absolute")]
        [TestCase("Bad\\Path")]
        public void InvalidResourcePath_IsRejectedWithUsefulArgumentError(string path)
        {
            Assert.Throws<ArgumentException>(() => new ResourcesDataProvider(path));
        }

        [Test]
        public void Coverage_EscapedBracesAndLiteralMalformedTokensAreNotParameters()
        {
            var data = new EditorLocalizationData();
            data.AddKey("key", "test");
            data.SetTranslation("key", "en", "{{literal}} {player} {bad{nested}}");
            data.SetTranslation("key", "ru", "{player}");
            Assert.That(CoverageAnalyzer.ComputeWarnings(data, _config), Is.Empty);
        }

        [Test]
        public void Coverage_EmptyDatasetIsNotComplete()
        {
            var data = new EditorLocalizationData();
            Assert.That(CoverageAnalyzer.ComputeCoverage(data, _config)[0].Percentage, Is.Zero);
            Assert.That(CoverageAnalyzer.ComputeWarnings(data, _config), Is.Not.Empty);
        }

        [Test]
        public void Configuration_NullSlotsAndExternalFallbackRemainCompatible()
        {
            _config.languages.Insert(0, null);
            var fallback = Create<LanguageProfile>();
            fallback.languageCode = "uk";
            _config.fallbackLanguage = fallback;
            _config.languages[1].fallbackProfile = fallback;
            fallback.fallbackProfile = _config.languages[1]; // Legacy authoring preview must stay bounded until its C5 replacement.
            Assert.That(_config.GetValidationErrors(), Is.Empty);
            var data = new EditorLocalizationData();
            Assert.DoesNotThrow(() => CoverageAnalyzer.ComputeWarnings(data, _config));
        }

        [Test]
        public void Configuration_MissingDefaultReferenceAndUnsafeFallbackAreDiagnosed()
        {
            var external = Create<LanguageProfile>();
            external.languageCode = "../outside";
            _config.defaultLanguage = _config.fallbackLanguage = external;
            var errors = _config.GetValidationErrors();
            Assert.That(errors.Exists(e => e.Contains("defaultLanguage")), Is.True);
            Assert.That(errors.Exists(e => e.Contains("../outside")), Is.True);
        }

        [Test]
        public void Configuration_NullLanguageListCanBeQueriedDuringSetup()
        {
            _config.languages = null;
            Assert.That(_config.GetLanguageCodes(), Is.Empty);
            Assert.That(_config.GetProfile("en"), Is.Null);
            Assert.That(_config.GetProfileBySystemLanguage(SystemLanguage.English), Is.Null);
            Assert.That(_config.GetValidationErrors(), Is.Empty);
        }

        [Test]
        public void Coverage_DistinguishesDirectAndFallbackIncludingWhitespaceAndCycles()
        {
            _config.languages[1].fallbackProfile = _config.languages[0];
            _config.languages[0].fallbackProfile = _config.languages[1];
            var data = new EditorLocalizationData();
            foreach (var key in new[] { "available", "space", "missing" }) data.AddKey(key, "test");
            data.SetTranslation("available", "en", "Hello");
            data.SetTranslation("available", "ru", "");
            data.SetTranslation("space", "ru", " \n");
            var coverage = CoverageAnalyzer.ComputeCoverage(data, _config)[1];
            Assert.That(coverage.TranslatedCount, Is.EqualTo(1));
            Assert.That(coverage.AvailableCount, Is.EqualTo(2));
            Assert.That(coverage.UnavailableKeys, Is.EqualTo(new[] { "missing" }));
        }

        [Test]
        public void Coverage_ReportsInsufficientFormsInReferenceLanguageToo()
        {
            var data = new EditorLocalizationData();
            data.AddKey("key", "test");
            data.SetTranslation("key", "en", "{0|coin}");
            data.SetTranslation("key", "ru", "{0|монета|монеты}");
            var warnings = CoverageAnalyzer.ComputeWarnings(data, _config);
            Assert.That(warnings.Exists(w => w.LanguageCode == "en" && w.Message.Contains("forms")), Is.True);
            Assert.That(warnings.Exists(w => w.LanguageCode == "ru" && w.Message.Contains("forms")), Is.True);
        }

        private T Create<T>() where T : ScriptableObject
        {
            var obj = ScriptableObject.CreateInstance<T>();
            _objects.Add(obj);
            return obj;
        }

        private static void Completed(System.Threading.Tasks.Task operation)
        {
            Assert.That(operation.IsCompleted, Is.True);
            operation.GetAwaiter().GetResult();
        }
    }
}
