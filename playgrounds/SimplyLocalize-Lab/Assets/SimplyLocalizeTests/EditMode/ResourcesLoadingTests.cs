using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using SimplyLocalize.Data;
using SimplyLocalize.Editor.Utilities;
using SimplyLocalize.Editor.Data;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace SimplyLocalize.Tests
{
    public class ResourcesLoadingTests
    {
        private string _root;
        private string _basePath;
        private LocalizationConfig _config;
        private LanguageProfile _en;
        private LanguageProfile _ru;
        private LocalizationManager _manager;

        [SetUp]
        public void SetUp()
        {
            // Unique temporary assets; never overwrite the project's own localization data.
            _basePath = "SimplyLocalizeRegression_" + Guid.NewGuid().ToString("N");
            _root = "Assets/" + _basePath;
            Write("en", "first", "{\"translations\":{\"fallback\":\"English fallback\"}}");
            Write("ru", "first", "{\"translations\":{\"greeting\":\"Привет\",\"empty\":\"\"}}");
            Write("ru", "second", "{\"translations\":{\"second\":\"Second file\"}}");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            _config = ScriptableObject.CreateInstance<LocalizationConfig>();
            _en = ScriptableObject.CreateInstance<LanguageProfile>();
            _ru = ScriptableObject.CreateInstance<LanguageProfile>();
            _en.languageCode = "en";
            _ru.languageCode = "ru";
            _config.languages.Add(_en);
            _config.languages.Add(_ru);
            _config.fallbackLanguage = _en;
            _config.resourcesBasePath = _basePath;
            _manager = new LocalizationManager(_config);
        }

        [TearDown]
        public void TearDown()
        {
            Localization.Shutdown();
            _manager?.ClearCaches();
            Object.DestroyImmediate(_config);
            Object.DestroyImmediate(_en);
            Object.DestroyImmediate(_ru);
            if (!string.IsNullOrEmpty(_root)) AssetDatabase.DeleteAsset(_root);
        }

        [Test]
        public void Resources_MergesExistingJsonFilesAndLoadsGlobalFallback()
        {
            _manager.SetLanguage("ru");
            Assert.That(_manager.Get("greeting"), Is.EqualTo("Привет"));
            Assert.That(_manager.Get("second"), Is.EqualTo("Second file"));
            Assert.That(_manager.Get("fallback"), Is.EqualTo("English fallback"));
            Assert.That(_manager.Get("empty"), Is.EqualTo("empty"));
            Assert.That(_manager.HasTranslation("empty", "ru"), Is.True);
        }

        [TestCase("ru")]
        [TestCase("en")]
        public void Resources_FailedReloadKeepsState_AndCanRecover(string language)
        {
            _manager.SetLanguage("ru");
            Write("ru", "first", "{\"translations\":{\"greeting\":\"Updated\"}}");
            Write(language, "broken", "not a JSON object");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Assert.Throws<FormatException>(() => _manager.Reload());
            Assert.That(_manager.Get("greeting"), Is.EqualTo("Привет"));
            Assert.That(_manager.Get("fallback"), Is.EqualTo("English fallback"));

            Write(language, "broken", "{\"translations\":{}}");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            _manager.Reload();
            Assert.That(_manager.Get("greeting"), Is.EqualTo("Updated"));
        }

        [TestCase("not JSON")]
        [TestCase("{\"translations\":{\"bad\":t")]
        [TestCase("{\"translations\":{\"bad\":n")]
        [TestCase("{\"translations\":{\"bad\":999999999999999999999999999999999999}}")]
        public void Initialize_MalformedSourceNamesFileAndPreservesActiveManager(string invalidJson)
        {
            Localization.Initialize(_config, "ru");
            var original = Localization.Manager;
            Write("en", "broken", invalidJson);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var error = Assert.Throws<FormatException>(() => Localization.Initialize(_config, "en"));
            StringAssert.Contains($"Resources/{_basePath}/en/text/broken", error.Message);
            Assert.That(error.InnerException, Is.Not.Null);
            Assert.That(Localization.Manager, Is.SameAs(original));
            Assert.That(Localization.Get("greeting"), Is.EqualTo("Привет"));
        }

        [Test]
        public void Coverage_LoadsOtherLanguageFilesNestedFilesAndAllTablesWithoutChangingLiveState()
        {
            Write("ru", "nested/extra", "{\"translations\":{\"nested\":\"Nested\"}}");
            string secondRoot = $"{_root}/Second/Resources/{_basePath}/en/text";
            Directory.CreateDirectory(secondRoot);
            File.WriteAllText($"{secondRoot}/extra.json", "{\"translations\":{\"other-root\":\"Other\"}}");
            var texture = new Texture2D(1, 1);
            AssetDatabase.CreateAsset(texture, $"{_root}/source.asset");
            for (int i = 0; i < 2; i++)
            {
                var table = ScriptableObject.CreateInstance<LocalizationAssetTable>();
                table.Set("asset" + i, texture);
                AssetDatabase.CreateAsset(table, $"{_root}/Resources/{_basePath}/en/table{i}.asset");
            }
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Localization.Initialize(_config, "ru");
            var live = Localization.Manager;
            var edited = new EditorLocalizationData();
            edited.AddKey("greeting", "first");
            edited.SetTranslation("greeting", "ru", "");
            var report = CoverageAnalyzer.Analyze(_config, edited);
            var ru = report.TextCoverage.Find(c => c.LanguageCode == "ru");
            Assert.That(ru.TotalCount, Is.EqualTo(6));
            Assert.That(ru.TranslatedCount, Is.EqualTo(2)); // second + nested; edited greeting is empty
            Assert.That(ru.AvailableCount, Is.EqualTo(4)); // plus fallback + other-root
            Assert.That(report.AssetCoverage.Find(c => c.LanguageCode == "ru").AvailableCount, Is.EqualTo(2));
            Assert.That(Localization.Manager, Is.SameAs(live));
            Assert.That(Localization.Get("greeting"), Is.EqualTo("Привет"));
            Assert.That(edited.KeyCount, Is.EqualTo(1));
        }

        [Test]
        public void Coverage_FailedLoadCannotLookLikeACompleteCheck()
        {
            Write("ru", "broken", "bad JSON");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var report = CoverageAnalyzer.Analyze(_config);
            Assert.That(report.IsComplete, Is.False);
            Assert.That(report.Warnings.Exists(w => w.Message.Contains("broken") && w.LanguageCode == "ru"), Is.True);
        }

        [Test]
        public void Resources_MissingLanguageReturnsEmptyCollections()
        {
            var provider = new ResourcesDataProvider(_basePath);
            Assert.That(provider.HasTextData("missing"), Is.False);
            LogAssert.Expect(LogType.Warning, new Regex("No text files found.*missing"));
            Assert.That(provider.LoadTextData("missing"), Is.Empty);
            Assert.That(provider.LoadAssetTables("missing"), Is.Empty);
        }

        private void Write(string language, string name, string json)
        {
            string folder = $"{_root}/Resources/{_basePath}/{language}/text";
            Directory.CreateDirectory(Path.GetDirectoryName($"{folder}/{name}.json"));
            File.WriteAllText($"{folder}/{name}.json", json);
        }
    }
}
