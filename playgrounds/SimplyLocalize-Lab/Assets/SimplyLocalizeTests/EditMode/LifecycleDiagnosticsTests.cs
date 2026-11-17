using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
        private LocalizationProjectConfig _project;
        private Action<string> _languageHandler;
        private Action<LocalizationRuntime> _profileHandler;

        [SetUp]
        public void SetUp()
        {
            Completed(Localization.ShutdownAsync());
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
            var config = Diagnostics(); config.Sets[0].SetSources("en", new LocalizationSourceReference("resources", path, LocalizationSourceKind.TextTable));
            Assert.Throws<ArgumentException>(config.ValidateOrThrow);
        }

        [Test]
        public void Coverage_EscapedBracesAndLiteralMalformedTokensAreNotParameters()
        {
            var config = Diagnostics(); config.Sets[0].AddEntry("key");
            var reader = new Reader(); reader.Text["en"]["key"] = "{{literal}} {player} {bad{nested}}"; reader.Text["ru"]["key"] = "{player}";
            Assert.That(ProjectCoverageAnalyzer.Analyze(config, textReader: reader).Warnings, Is.Empty);
        }

        [Test]
        public void Coverage_EmptyDatasetIsNotReportedAs100Percent()
        {
            var report = ProjectCoverageAnalyzer.Analyze(Diagnostics(), textReader: new Reader());
            Assert.That(report.TextCoverage.All(r => r.TotalCount == 0 && r.Percentage == 0 && r.AvailablePercentage == 0), Is.True);
        }

        [Test]
        public void Configuration_NullLanguageSlotIsDiagnosedBeforeReadingSources()
        {
            var config = Diagnostics(); var field = typeof(LocalizationProjectConfig).GetField("_languages", BindingFlags.Instance | BindingFlags.NonPublic);
            ((System.Collections.IList)field.GetValue(config)).Add(null);
            var reader = new Reader(); var report = ProjectCoverageAnalyzer.Analyze(config, textReader: reader);
            Assert.That(report.IsComplete, Is.False); Assert.That(report.TextCoverage, Is.Empty); Assert.That(reader.Reads, Is.Zero);
            Assert.That(report.Warnings.Any(w => w.Message.Contains("Language definition is empty")), Is.True);
        }

        [Test]
        public void Configuration_UnselectedDefaultAndUnsafeFallbackAreDiagnosed()
        {
            var config = Diagnostics();
            Assert.Throws<ArgumentException>(() => config.MainChannel.SetFallback("en", "../outside"));
            config.MainChannel.DefaultLanguage = "uk";
            var report = ProjectCoverageAnalyzer.Analyze(config, textReader: new Reader());
            Assert.That(report.IsComplete, Is.False); Assert.That(report.Warnings.Any(w => w.Message.Contains("Default language")), Is.True);
        }

        [Test]
        public void Configuration_NullLanguageListCanBeQueriedDuringSetup()
        {
            var config = Diagnostics(); typeof(LocalizationProjectConfig).GetField("_languages", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(config, null);
            Assert.That(config.Languages, Is.Empty);
            Assert.That(ProjectCoverageAnalyzer.Analyze(config, textReader: new Reader()).IsComplete, Is.False);
        }

        [Test]
        public void Coverage_DistinguishesDirectFallbackAndWhitespaceWithoutChangingRuntime()
        {
            var config = Diagnostics(); config.MainChannel.SetFallback("ru", "en");
            foreach (var key in new[] { "available", "space", "missing" }) config.Sets[0].AddEntry(key);
            var reader = new Reader(); reader.Text["en"]["available"] = "Hello"; reader.Text["ru"]["available"] = ""; reader.Text["ru"]["space"] = " \n";
            var live = Localization.Runtime.GetChannel(); var coverage = ProjectCoverageAnalyzer.Analyze(config, textReader: reader).TextCoverage.Single(r => r.LanguageCode == "ru");
            Assert.That(coverage.TranslatedCount, Is.EqualTo(1));
            Assert.That(coverage.AvailableCount, Is.EqualTo(2));
            Assert.That(coverage.UnavailableKeys, Is.EqualTo(new[] { "missing" }));
            Assert.That(Localization.Runtime.GetChannel(), Is.SameAs(live));
        }

        [TestCase(false)][TestCase(true)] public void InvalidFallbackCycleOrUnselectedTargetDoesNotReadSources(bool cycle)
        {
            var config = Diagnostics(); config.MainChannel.SetFallback("ru", "en");
            if (cycle) config.MainChannel.SetFallback("en", "ru"); else { config.AddLanguage("uk"); config.MainChannel.SetFallback("en", "uk"); }
            var reader = new Reader(); var report = ProjectCoverageAnalyzer.Analyze(config, textReader: reader);
            Assert.That(report.IsComplete, Is.False); Assert.That(report.TextCoverage, Is.Empty); Assert.That(reader.Reads, Is.Zero);
        }

        [Test]
        public void Coverage_ReportsInsufficientFormsInReferenceLanguageToo()
        {
            var config = Diagnostics(); config.Sets[0].AddEntry("key"); var reader = new Reader();
            reader.Text["en"]["key"] = "{0|coin}"; reader.Text["ru"]["key"] = "{0|монета|монеты}";
            var warnings = ProjectCoverageAnalyzer.Analyze(config, textReader: reader).Warnings;
            Assert.That(warnings.Exists(w => w.LanguageCode == "en" && w.Message.Contains("forms")), Is.True);
            Assert.That(warnings.Exists(w => w.LanguageCode == "ru" && w.Message.Contains("forms")), Is.True);
        }

        private LocalizationProjectConfig Diagnostics()
        {
            var config = LocalizationProjectConfig.Create(new[] { "en", "ru" }); _objects.Add(config);
            foreach (string code in new[] { "en", "ru" }) config.Sets[0].SetSources(code, new LocalizationSourceReference("test", code, LocalizationSourceKind.TextTable));
            return config;
        }
        private sealed class Reader : ILocalizationEditorTextReader
        {
            internal int Reads;
            internal readonly Dictionary<string, Dictionary<string, string>> Text = new() { ["en"] = new(), ["ru"] = new() };
            public LocalizationEditorTextSource Read(LocalizationSourceReference source) { Reads++; return new(Text[source.Address]); }
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
