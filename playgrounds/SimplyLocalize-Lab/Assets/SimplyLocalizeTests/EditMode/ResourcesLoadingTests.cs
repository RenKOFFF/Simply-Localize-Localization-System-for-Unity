using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
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
        private readonly List<LocalizationProjectConfig> _projects = new();

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
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            yield return Done(Localization.ShutdownAsync());
            foreach (var config in _projects) Object.DestroyImmediate(config);
            _projects.Clear();
            Object.DestroyImmediate(_config);
            Object.DestroyImmediate(_en);
            Object.DestroyImmediate(_ru);
            if (!string.IsNullOrEmpty(_root)) AssetDatabase.DeleteAsset(_root);
        }

        [UnityTest]
        public IEnumerator Resources_ReadsRegisteredJsonFilesAndGlobalFallback()
        {
            yield return Done(Localization.InitializeAsync(Project(), "ru"));
            Assert.That(Localization.Get("greeting"), Is.EqualTo("Привет"));
            Assert.That(Localization.Get("second"), Is.EqualTo("Second file"));
            Assert.That(Localization.Get("fallback"), Is.EqualTo("English fallback"));
            Assert.That(Localization.Get("empty"), Is.EqualTo("empty"));
            Assert.That(Localization.Runtime.GetChannel().TryGetText("empty", out _), Is.False);
        }

        [UnityTest]
        public IEnumerator Resources_FailedReloadKeepsState_AndCanRecover()
        {
            foreach (string language in new[] { "ru", "en" })
            {
                Write("ru", "first", "{\"translations\":{\"greeting\":\"Привет\"}}");
                Write("en", "first", "{\"translations\":{\"fallback\":\"English fallback\"}}");
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                yield return Done(Localization.InitializeAsync(Project(), "ru"));
                var before = Localization.Runtime.GetChannel();
                Write("ru", "first", "{\"translations\":{\"greeting\":\"Updated\"}}");
                Write(language, "first", "not a JSON object");
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                var reload = Localization.ReloadAsync(); yield return Done(reload, false);
                Assert.Throws<FormatException>(() => reload.GetAwaiter().GetResult());
                Assert.That(Localization.Runtime.GetChannel(), Is.SameAs(before));
                Assert.That(Localization.Get("greeting"), Is.EqualTo("Привет"));
                Assert.That(Localization.Get("fallback"), Is.EqualTo("English fallback"));
                Write("ru", "first", "{\"translations\":{\"greeting\":\"Updated\"}}");
                Write("en", "first", "{\"translations\":{\"fallback\":\"English fallback\"}}");
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                yield return Done(Localization.ReloadAsync());
                Assert.That(Localization.Get("greeting"), Is.EqualTo("Updated"));
            }
        }

        [UnityTest]
        public IEnumerator Initialize_MalformedSourceNamesFileAndPreservesActiveRuntime()
        {
            yield return Done(Localization.InitializeAsync(Project(), "ru"));
            var original = Localization.Runtime;
            foreach (string invalidJson in new[] { "not JSON", "{\"translations\":{\"bad\":t", "{\"translations\":{\"bad\":n",
                "{\"translations\":{\"bad\":999999999999999999999999999999999999}}" })
            {
                Write("en", "broken", invalidJson);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                var attempt = Localization.InitializeAsync(Project(true), "en");
                yield return Done(attempt, false);
                var error = Assert.Throws<FormatException>(() => attempt.GetAwaiter().GetResult());
                StringAssert.Contains($"{_basePath}/en/text/broken", error.Message);
                Assert.That(error.InnerException, Is.Not.Null);
                Assert.That(Localization.Runtime, Is.SameAs(original));
                Assert.That(Localization.Get("greeting"), Is.EqualTo("Привет"));
            }
        }
        [UnityTest]
        public IEnumerator Coverage_LoadsOtherLanguageFilesNestedFilesAndAllTablesWithoutChangingLiveState()
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
            yield return Done(Localization.InitializeAsync(Project(), "ru"));
            var live = Localization.Runtime;
            var edited = new EditorLocalizationData();
            edited.AddKey("greeting", "first");
            edited.SetTranslation("greeting", "ru", "");
            var report = CoverageAnalyzer.Analyze(_config, edited);
            var ru = report.TextCoverage.Find(c => c.LanguageCode == "ru");
            Assert.That(ru.TotalCount, Is.EqualTo(6));
            Assert.That(ru.TranslatedCount, Is.EqualTo(2)); // second + nested; edited greeting is empty
            Assert.That(ru.AvailableCount, Is.EqualTo(4)); // plus fallback + other-root
            Assert.That(report.AssetCoverage.Find(c => c.LanguageCode == "ru").AvailableCount, Is.EqualTo(2));
            Assert.That(Localization.Runtime, Is.SameAs(live));
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

        private LocalizationProjectConfig Project(bool broken = false)
        {
            var project = LocalizationProjectConfig.Create(new[] { "en", "ru" }); _projects.Add(project);
            var set = project.Sets[0];
            foreach (string key in new[] { "greeting", "fallback", "empty", "second", "bad" }) set.AddEntry(key);
            var en = new List<LocalizationSourceReference> { new("resources", _basePath + "/en/text/first", LocalizationSourceKind.TextTable) };
            if (broken) en.Add(new("resources", _basePath + "/en/text/broken", LocalizationSourceKind.TextTable));
            set.SetSources("en", en.ToArray());
            set.SetSources("ru", new LocalizationSourceReference("resources", _basePath + "/ru/text/first", LocalizationSourceKind.TextTable),
                new LocalizationSourceReference("resources", _basePath + "/ru/text/second", LocalizationSourceKind.TextTable));
            project.MainChannel.SetFallback("ru", "en");
            return project;
        }
        private static IEnumerator Done(Task task, bool success = true)
        {
            float deadline = Time.realtimeSinceStartup + 15;
            while (!task.IsCompleted && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(task.IsCompleted, Is.True);
            if (success) task.GetAwaiter().GetResult();
        }
        private void Write(string language, string name, string json)
        {
            string folder = $"{_root}/Resources/{_basePath}/{language}/text";
            Directory.CreateDirectory(Path.GetDirectoryName($"{folder}/{name}.json"));
            File.WriteAllText($"{folder}/{name}.json", json);
        }
    }
}
