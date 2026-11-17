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
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            yield return Done(Localization.ShutdownAsync());
            foreach (var config in _projects) Object.DestroyImmediate(config);
            _projects.Clear();
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
        public IEnumerator CoverageLoadsRegisteredNestedFilesAndTablesWithoutChangingLiveState()
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
            var project = Project(); var set = project.Sets[0];
            set.AddEntry("nested"); set.AddEntry("other-root");
            for (int i = 0; i < 2; i++) set.AddEntry("asset" + i, LocalizationEntryKind.Asset);
            var en = set.Sources.Single(s => s.Language == "en").Sources.ToList();
            en.Add(new LocalizationSourceReference("resources", _basePath + "/en/text/extra", LocalizationSourceKind.TextTable));
            for (int i = 0; i < 2; i++) en.Add(new LocalizationSourceReference("resources", _basePath + "/en/table" + i, LocalizationSourceKind.AssetTable));
            set.SetSources("en", en.ToArray());
            set.SetSources("ru", set.Sources.Single(s => s.Language == "ru").Sources.Concat(new[] {
                new LocalizationSourceReference("resources", _basePath + "/ru/text/nested/extra", LocalizationSourceKind.TextTable) }).ToArray());
            yield return Done(Localization.InitializeAsync(project, "ru"));
            var live = Localization.Runtime;
            var snapshot = live.GetChannel();
            // Saved disk edits are visible to the Editor, while the live runtime retains its ready snapshot.
            Write("ru", "first", "{\"translations\":{\"greeting\":\"\",\"empty\":\"\"}}");
            var report = ProjectCoverageAnalyzer.Analyze(project);
            var ru = report.TextCoverage.Find(c => c.LanguageCode == "ru");
            Assert.That(ru.TotalCount, Is.EqualTo(6));
            Assert.That(ru.TranslatedCount, Is.EqualTo(2)); // second + nested; saved greeting is empty
            Assert.That(ru.AvailableCount, Is.EqualTo(4)); // plus fallback + other-root
            Assert.That(report.AssetCoverage.Find(c => c.LanguageCode == "ru").AvailableCount, Is.EqualTo(2));
            Assert.That(Localization.Runtime, Is.SameAs(live));
            Assert.That(Localization.Get("greeting"), Is.EqualTo("Привет"));
            Assert.That(live.GetChannel(), Is.SameAs(snapshot)); Assert.That(texture != null, Is.True);
        }

        [Test]
        public void Coverage_FailedLoadCannotLookLikeACompleteCheck()
        {
            Write("ru", "broken", "bad JSON");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var project = Project(); var set = project.Sets[0];
            set.SetSources("ru", set.Sources.Single(s => s.Language == "ru").Sources.Concat(new[] {
                new LocalizationSourceReference("resources", _basePath + "/ru/text/broken", LocalizationSourceKind.TextTable) }).ToArray());
            var report = ProjectCoverageAnalyzer.Analyze(project);
            Assert.That(report.IsComplete, Is.False);
            Assert.That(report.TextCoverage, Is.Empty);
            Assert.That(report.Warnings.Exists(w => w.Message.Contains(_basePath + "/ru/text/broken")), Is.True);
        }

        [Test]
        public void LanguageWithoutSourcesHasNoDirectEditorData()
        {
            var project = Project(); project.AddLanguage("pl"); project.MainChannel.SetLanguages(new[] { "en", "ru", "pl" });
            var data = EditorProjectTextData.Load(project); string entry = project.Sets[0].Entries.Single(e => e.Key == "fallback").Id;
            Assert.That(data.TryGetDirect(entry, "pl", out _), Is.False); Assert.That(data.GetUnregistered("pl"), Is.Empty); LogAssert.NoUnexpectedReceived();
        }
        [UnityTest] public IEnumerator MissingRegisteredResourceFailsWithoutReplacingActiveRuntime()
        {
            yield return Done(Localization.InitializeAsync(Project(), "ru")); var runtime = Localization.Runtime;
            var project = Project(); string address = _basePath + "/en/text/not-created";
            project.Sets[0].SetSources("en", new LocalizationSourceReference("resources", address, LocalizationSourceKind.TextTable));
            var task = Localization.InitializeAsync(project, "en"); yield return Done(task, false);
            Assert.That(Assert.Throws<InvalidOperationException>(() => task.GetAwaiter().GetResult()).Message, Does.Contain(address));
            Assert.That(Localization.Runtime, Is.SameAs(runtime)); Assert.That(Localization.Get("greeting"), Is.EqualTo("Привет"));
        }

        private LocalizationProjectConfig Project(bool broken = false)
        {
            var project = LocalizationProjectConfig.Create(new[] { "en", "ru" }); _projects.Add(project);
            var set = project.Sets[0];
            foreach (string key in new[] { "greeting", "fallback", "empty", "second" }) set.AddEntry(key);
            if (broken) set.AddEntry("bad");
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
