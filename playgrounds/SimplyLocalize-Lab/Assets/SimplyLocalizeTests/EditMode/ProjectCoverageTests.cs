using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SimplyLocalize.Editor.Data;
using SimplyLocalize.Editor.Utilities;
using SimplyLocalize.Editor.Windows;
using SimplyLocalize.Pluralization;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace SimplyLocalize.Tests
{
    public class ProjectCoverageTests
    {
        private LocalizationProjectConfig config;
        private Reader reader;
        private Texture2D texture;
        [SetUp] public void Setup()
        {
            config = LocalizationProjectConfig.Create(new[] { "en", "ru" }); reader = new Reader(); texture = new Texture2D(1, 1);
            foreach (string code in new[] { "en", "ru" })
            {
                config.Sets[0].SetSources(code, new LocalizationSourceReference("test", code, LocalizationSourceKind.TextTable), new LocalizationSourceReference("test", code, LocalizationSourceKind.AssetTable));
                reader.Text[code] = new Dictionary<string, string>(); reader.Assets[code] = new Dictionary<string, Object>();
            }
        }
        [TearDown] public void Cleanup() { Object.DestroyImmediate(config); Object.DestroyImmediate(texture); }
        private CoverageReport Analyze(LocalizationPluralRules rules = null) => ProjectCoverageAnalyzer.Analyze(config, textReader: reader, assetReader: reader, pluralRules: rules);
        [Test] public void RegisteredTextCountsDistinguishMissingEmptyWhitespaceAndFallback()
        {
            foreach (string key in new[] { "direct", "empty", "space", "missing" }) { config.Sets[0].AddEntry(key); reader.Text["en"][key] = "English"; }
            reader.Text["ru"]["direct"] = "Прямой"; reader.Text["ru"]["empty"] = ""; reader.Text["ru"]["space"] = "  "; reader.Text["ru"]["extra"] = "Unregistered";
            var report = Analyze(); var ru = report.TextCoverage.Single(r => r.LanguageCode == "ru");
            Assert.That(report.IsComplete, Is.True); Assert.That(ru.TotalCount, Is.EqualTo(4)); Assert.That(ru.PresentCount, Is.EqualTo(3));
            Assert.That(ru.TranslatedCount, Is.EqualTo(2)); Assert.That(ru.AvailableCount, Is.EqualTo(4));
            Assert.That(ru.EmptyKeys, Is.EqualTo(new[] { "empty" })); Assert.That(ru.WhitespaceKeys, Is.EqualTo(new[] { "space" }));
            Assert.That(report.Warnings.Any(w => w.Key == "extra" && w.Message.Contains("Unregistered")), Is.True);
        }
        [Test] public void NullAssetIsPresentButRequiresFallbackAndDoesNotDestroyBorrowedObjects()
        {
            config.Sets[0].AddEntry("image", LocalizationEntryKind.Asset); reader.Assets["en"]["image"] = texture; reader.Assets["ru"]["image"] = null;
            var ru = Analyze().AssetCoverage.Single(r => r.LanguageCode == "ru");
            Assert.That(ru.PresentCount, Is.EqualTo(1)); Assert.That(ru.TranslatedCount, Is.Zero); Assert.That(ru.AvailableCount, Is.EqualTo(1)); Assert.That(texture != null, Is.True);
            reader.Assets["en"].Clear(); Assert.That(Analyze().AssetCoverage.Single(r => r.LanguageCode == "ru").UnavailableKeys, Does.Contain("image"));
        }
        [Test] public void FailedReaderProducesNoInventedMissingTotalsAndPreservesIndependentSection()
        {
            config.Sets[0].AddEntry("text"); config.Sets[0].AddEntry("image", LocalizationEntryKind.Asset); reader.FailText = true;
            var report = Analyze(); Assert.That(report.IsComplete, Is.False); Assert.That(report.TextCoverage, Is.Empty); Assert.That(report.AssetCoverage.Count, Is.EqualTo(2));
            Assert.That(report.Warnings.Any(w => w.Level == CoverageWarning.Severity.Error && w.Message.Contains("Cannot check text")), Is.True);
            reader.FailText = false; reader.FailAssets = true; report = Analyze(); Assert.That(report.AssetCoverage, Is.Empty); Assert.That(report.TextCoverage.Count, Is.EqualTo(2));
        }
        [Test] public void ParameterAndPluralWarningsUseActualParserAndInstanceRules()
        {
            config.Sets[0].AddEntry("line"); reader.Text["en"]["line"] = "{{literal}} {name} {0|item|items}"; reader.Text["ru"]["line"] = "{0|предмет|предмета} {extra}";
            var report = Analyze(); Assert.That(report.IsComplete, Is.False);
            Assert.That(report.Warnings.Any(w => w.Message.Contains("Missing params") && w.Message.Contains("{name}")), Is.True);
            Assert.That(report.Warnings.Any(w => w.Message.Contains("{literal}")), Is.False);
            Assert.That(report.Warnings.Any(w => w.LanguageCode == "ru" && w.Message.Contains("rule expects 4")), Is.True);
            var rules = new LocalizationPluralRules(); rules.Register("ru", new EastAsianPluralRule());
            Assert.That(Analyze(rules).Warnings.Any(w => w.LanguageCode == "ru" && w.Message.Contains("rule expects")), Is.False);
            Assert.That(Analyze().Warnings.Any(w => w.LanguageCode == "ru" && w.Message.Contains("rule expects 4")), Is.True);
        }
        [Test] public void UnknownPluralWarnsWithoutRejectingLanguageAndCustomRuleClearsWarning()
        {
            config.AddLanguage("x-test"); config.MainChannel.SetLanguages(new[] { "en", "ru", "x-test" });
            var report = Analyze(); Assert.That(report.IsComplete, Is.True); Assert.That(report.Warnings.Any(w => w.LanguageCode == "x-test" && w.Message.Contains("General plural")), Is.True);
            var rules = new LocalizationPluralRules(); rules.Register("x-test", new EastAsianPluralRule());
            Assert.That(Analyze(rules).Warnings.Any(w => w.LanguageCode == "x-test" && w.Message.Contains("General plural")), Is.False);
        }
        [Test] public void SelectedSetIsIsolatedAndExcludedSetIsAnExplicitError()
        {
            config.Sets[0].AddEntry("same"); reader.Text["en"]["same"] = "Main";
            var other = config.AddSet("Other"); other.AddEntry("same");
            var rejected = ProjectCoverageAnalyzer.Analyze(config, setId: other.Id, textReader: reader, assetReader: reader);
            Assert.That(rejected.IsComplete, Is.False); Assert.That(rejected.TextCoverage, Is.Empty);
            config.MainChannel.IncludeSet(other.Id);
            var report = ProjectCoverageAnalyzer.Analyze(config, setId: other.Id, textReader: reader, assetReader: reader);
            Assert.That(report.TextCoverage.Single(r => r.LanguageCode == "en").AvailableCount, Is.Zero);
            Assert.That(Analyze().TextCoverage.Single(r => r.LanguageCode == "en").TranslatedCount, Is.EqualTo(1));
        }
        [Test] public void ViewRefreshShowsReadFailuresAndUpdatedSavedSnapshotWithoutChangingConfig()
        {
            config.Sets[0].AddEntry("hello"); string before = UnityEditor.EditorJsonUtility.ToJson(config);
            using var view = new ProjectCoverageView(config, textReader: reader, assetReader: reader);
            Assert.That(view.Report.IsComplete, Is.True); reader.FailText = true; view.Reload();
            Assert.That(view.Q<HelpBox>("coverageStatus").messageType, Is.EqualTo(HelpBoxMessageType.Error)); Assert.That(view.Report.TextCoverage, Is.Empty);
            reader.FailText = false; reader.Text["en"]["hello"] = "Hello"; view.Reload();
            Assert.That(view.Report.TextCoverage.Single(r => r.LanguageCode == "ru").AvailableCount, Is.EqualTo(1)); Assert.That(UnityEditor.EditorJsonUtility.ToJson(config), Is.EqualTo(before));
        }
        private sealed class Reader : ILocalizationEditorTextReader, ILocalizationEditorAssetReader
        {
            internal readonly Dictionary<string, Dictionary<string, string>> Text = new();
            internal readonly Dictionary<string, Dictionary<string, Object>> Assets = new();
            internal bool FailText, FailAssets;
            LocalizationEditorTextSource ILocalizationEditorTextReader.Read(LocalizationSourceReference source)
            { if (FailText) throw new InvalidOperationException("Text source unavailable"); return new(Text[source.Address]); }
            LocalizationEditorAssetSource ILocalizationEditorAssetReader.Read(LocalizationSourceReference source)
            { if (FailAssets) throw new InvalidOperationException("Asset source unavailable"); return new(Assets[source.Address]); }
        }
    }
}
