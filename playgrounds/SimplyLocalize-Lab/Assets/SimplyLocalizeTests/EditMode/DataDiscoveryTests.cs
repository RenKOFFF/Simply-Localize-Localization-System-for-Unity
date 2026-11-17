using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using SimplyLocalize.Data;
using SimplyLocalize.Editor;
using SimplyLocalize.Editor.Utilities;
using SimplyLocalize.Editor.Windows;
using UnityEngine.TestTools;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using SimplyLocalize.Editor.Data;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SimplyLocalize.Tests
{
    public class DataDiscoveryTests
    {
        private string _root, _folder;
        private LocalizationConfig _config;
        private EditorLocalizationData _data;
        [SetUp] public void Setup()
        {
            string id = "Discovery_" + Guid.NewGuid().ToString("N");
            _root = "Assets/__" + id; _folder = _root + "/Resources/" + id;
            Directory.CreateDirectory(_folder);
            _config = ScriptableObject.CreateInstance<LocalizationConfig>(); _config.resourcesBasePath = id;
            foreach (string code in new[] { "en", "ru" })
            { var profile = ScriptableObject.CreateInstance<LanguageProfile>(); profile.languageCode = code; _config.languages.Add(profile); }
            _config.defaultLanguage = _config.languages[0]; _config.fallbackLanguage = _config.languages[0];
            _data = new EditorLocalizationData();
        }
        [TearDown] public void Cleanup()
        {
            EditorDataCache.SelectConfig(null); EditorDataCache.Invalidate();
            foreach (var profile in _config.languages ?? new List<LanguageProfile>()) if (profile) Object.DestroyImmediate(profile);
            Object.DestroyImmediate(_config);
            AssetDatabase.DeleteAsset(_root);
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
            if (File.Exists(_root + ".meta")) File.Delete(_root + ".meta");
        }
        private string Write(string code, string file, string key)
        {
            string path = _folder + "/" + code + "/text/" + file + ".json";
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, new JObject { ["custom"] = 42, ["translations"] = new JObject { [key] = code + " value" } }.ToString());
            return path;
        }
        [Test] public void FilesExclusiveToOtherLanguages_AreLoadedAndSavedAtTheirOriginalPaths()
        {
            Write("en", "main", "common"); string path = Write("ru", "extra", "only.ru");
            _data.Load(_config);
            Assert.That(_data.SourceFiles, Is.EquivalentTo(new[] { "main", "extra" }));
            Assert.That(_data.GetTranslation("only.ru", "ru"), Is.EqualTo("ru value"));
            _data.SetTranslation("only.ru", "ru", "edited"); _data.SaveAll();
            Assert.That((string)JObject.Parse(File.ReadAllText(path))["translations"]["only.ru"], Is.EqualTo("edited"));
            Assert.That((int)JObject.Parse(File.ReadAllText(path))["custom"], Is.EqualTo(42));
        }
        [Test] public void NestedFilesWithSameLeafName_RemainSeparate()
        {
            string a = Write("en", "chapter1/main", "one"), b = Write("ru", "chapter2/main", "two");
            _data.Load(_config);
            Assert.That(_data.SourceFiles, Is.EquivalentTo(new[] { "chapter1/main", "chapter2/main" }));
            Assert.That(_data.GetFileForKey("two"), Is.EqualTo("chapter2/main"));
            _data.SaveAll();
            Assert.That((string)JObject.Parse(File.ReadAllText(a))["translations"]["one"], Is.EqualTo("en value"));
            Assert.That((string)JObject.Parse(File.ReadAllText(b))["translations"]["two"], Is.EqualTo("ru value"));
            Assert.That(File.Exists(_folder + "/en/text/main.json"), Is.False);
        }
        [Test] public void EmptyLanguageSlotsAndMissingDefaultFolder_DoNotHideOtherLanguages()
        {
            Write("ru", "extra", "only.ru"); _config.languages.Insert(0, null); _config.defaultLanguage = null;
            Assert.DoesNotThrow(() => _data.Load(_config));
            Assert.That(_data.GetTranslation("only.ru", "ru"), Is.EqualTo("ru value"));
        }

        private void ExpectDiscoveryFailure(string message)
        {
            LogAssert.Expect(LogType.Warning, new Regex("Cannot load localization data:.*" + Regex.Escape(message), RegexOptions.Singleline));
            _data.Load(_config);
            Assert.That(_data.LoadError, Does.Contain(message)); Assert.That(_data.KeyCount, Is.Zero);
            Assert.Throws<InvalidOperationException>(() => _data.SaveAll());
            Assert.Throws<InvalidOperationException>(() => _data.SaveMeta());
        }

        [Test] public void SameResourcesAddressInTwoRoots_BlocksEditingWithoutChoosingOne()
        {
            string original = Write("en", "main", "first");
            string second = _root + "/Second/Resources/" + _config.resourcesBasePath + "/ru/text";
            Directory.CreateDirectory(second); File.WriteAllText(second + "/other.json", new JObject { ["translations"] = new JObject { ["second"] = "value" } }.ToString());
            byte[] before = File.ReadAllBytes(original);
            ExpectDiscoveryFailure("Several localization folders");
            Assert.That(_data.BasePath, Is.Null); Assert.That(File.ReadAllBytes(original), Is.EqualTo(before));
            Assert.That(File.Exists(_folder + "/_meta.json"), Is.False);
        }

        [Test] public void ResourcesWithOtherAddressesAndEditorOnlyData_DoNotCreateAmbiguity()
        {
            Write("en", "main", "real");
            Directory.CreateDirectory(_root + "/Second/Resources/OtherDataset");
            Directory.CreateDirectory(_root + "/Editor/Resources/" + _config.resourcesBasePath);
            _data.Load(_config);
            Assert.That(_data.LoadError, Is.Null); Assert.That(_data.AllKeys, Is.EqualTo(new[] { "real" }));
        }

        [Test] public void NewConflictingRootAfterLoad_IsRejectedBeforeSave()
        {
            string path = Write("en", "main", "key"); _data.Load(_config); byte[] before = File.ReadAllBytes(path);
            Directory.CreateDirectory(_root + "/Second/Resources/" + _config.resourcesBasePath);
            _data.SetTranslation("key", "en", "pending");
            Assert.Throws<InvalidOperationException>(() => _data.SaveAll());
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(before));
            Assert.That(_data.GetTranslation("key", "en"), Is.EqualTo("pending"));
        }

        [TestCase(false)] [TestCase(true)]
        public void KeyOwnedByDifferentFiles_IsNotSilentlyMovedOnSave(bool sameLanguage)
        {
            string a = Write("en", "a", "same"), b = Write(sameLanguage ? "en" : "ru", "b", "same");
            byte[] beforeA = File.ReadAllBytes(a), beforeB = File.ReadAllBytes(b);
            ExpectDiscoveryFailure("belongs to both");
            Assert.That(File.ReadAllBytes(a), Is.EqualTo(beforeA)); Assert.That(File.ReadAllBytes(b), Is.EqualTo(beforeB));
        }

        [TestCase("{bad")]
        [TestCase("{\"translations\":{\"same\":\"one\",\"same\":\"two\"}}")]
        [TestCase("{\"translations\":[]} ")]
        [TestCase("{\"translations\":{}} trailing")]
        public void MalformedOrAmbiguousJson_PublishesNoPartialDatasetAndBlocksSave(string json)
        {
            Write("en", "a", "valid"); string path = Write("ru", "broken", "bad"); File.WriteAllText(path, json);
            ExpectDiscoveryFailure("Invalid translation file");
            Assert.That(File.ReadAllText(path), Is.EqualTo(json));
            Write("ru", "broken", "repaired"); _data.Reload(_config);
            Assert.That(_data.LoadError, Is.Null); Assert.That(_data.KeyCount, Is.EqualTo(2)); _data.SaveAll();
        }

        [Test] public void NestedFiles_RoundTripThroughCsvCloneAndRename()
        {
            string path = Write("en", "chapter/main", "old"); _data.Load(_config);
            string csv = CsvExporter.Export(_data, new List<string> { "en", "ru" });
            CsvExporter.Import(_data, csv); _data.SaveAll();
            LocalizationKeyRenamer.RenameText(_data, "old", "new", false);
            var json = JObject.Parse(File.ReadAllText(path));
            Assert.That((string)json["translations"]["new"], Is.EqualTo("en value")); Assert.That(json["translations"]["old"], Is.Null);
        }

        [Test] public void ExistingUppercaseExtension_IsSavedWithoutCreatingAnotherFile()
        {
            string path = Write("en", "main", "key"); string upper = Path.ChangeExtension(path, ".JSON"); File.Move(path, upper);
            _data.Load(_config); _data.SetTranslation("key", "en", "edited"); _data.SaveAll();
            Assert.That((string)JObject.Parse(File.ReadAllText(upper))["translations"]["key"], Is.EqualTo("edited"));
            Assert.That(Directory.GetFiles(Path.GetDirectoryName(upper)), Has.Length.EqualTo(1));
        }

        [Test] public void ConfigSelection_IsDeterministicAndRequiresChoiceForAmbiguity()
        {
            var other = ScriptableObject.CreateInstance<LocalizationConfig>();
            try
            {
                var type = typeof(EditorDataCache).Assembly.GetType("SimplyLocalize.Editor.Data.EditorConfigSelection");
                var method = type.GetMethod("Choose", BindingFlags.Static | BindingFlags.NonPublic);
                object[] args = { new[] { _config, other }, null, null };
                Assert.That(method.Invoke(null, args), Is.Null); Assert.That((string)args[2], Does.Contain("Several"));
                args[1] = _config; Assert.That(method.Invoke(null, args), Is.SameAs(_config)); Assert.That(args[2], Is.Null);
                args = new object[] { new LocalizationConfig[] { null, other }, null, null };
                Assert.That(method.Invoke(null, args), Is.SameAs(other));
            }
            finally { Object.DestroyImmediate(other); }
        }

        [Test] public void WindowSelection_IsSharedWithInspectorCacheWithoutChangingStartup()
        {
            Write("en", "main", "selected.only");
            var window = ScriptableObject.CreateInstance<LocalizationEditorWindow>();
            var previous = EditorDataCache.Config;
            try
            {
                typeof(LocalizationEditorWindow).GetMethod("AssignConfig", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(window, new object[] { _config });
                Assert.That(EditorDataCache.Config, Is.SameAs(_config));
                Assert.That(EditorDataCache.AllKeys, Is.EqualTo(new[] { "selected.only" }));
                Assert.That(_config.autoInitialize, Is.False);
            }
            finally { Object.DestroyImmediate(window); EditorDataCache.SelectConfig(previous); }
        }

        [Test] public void Coverage_DistinguishesPresenceEmptyFallbackAndWhitespaceWithoutChangingRuntime()
        {
            var provider = new Provider();
            provider.Text["en"] = new Dictionary<string, string> { ["fallback"] = "Hello", ["empty"] = "", ["only.en"] = "English" };
            provider.Text["ru"] = new Dictionary<string, string> { ["fallback"] = "", ["empty"] = "", ["space"] = " \n" };
            var report = CoverageAnalyzer.Analyze(_config, null, provider);
            var ru = report.TextCoverage.Single(c => c.LanguageCode == "ru");
            Assert.That(ru.TotalCount, Is.EqualTo(4)); Assert.That(ru.PresentCount, Is.EqualTo(3));
            Assert.That(ru.TranslatedCount, Is.EqualTo(1)); Assert.That(ru.AvailableCount, Is.EqualTo(3));
            Assert.That(ru.EmptyKeys, Is.EquivalentTo(new[] { "empty", "fallback" })); Assert.That(ru.WhitespaceKeys, Is.EqualTo(new[] { "space" }));
            Assert.That(ru.UnavailableKeys, Is.EqualTo(new[] { "empty" }));
            Assert.That(report.Warnings.Any(w => w.Message.Contains("Whitespace-only")), Is.True);
            var project = LocalizationProjectConfig.Create(new[] { "en", "ru" });
            try
            {
                foreach (string key in new[] { "fallback", "empty", "only.en", "space" }) project.Sets[0].AddEntry(key);
                foreach (string code in new[] { "en", "ru" })
                    project.Sets[0].SetSources(code, new LocalizationSourceReference("test", "text", LocalizationSourceKind.TextTable));
                project.MainChannel.FallbackLanguage = "en";
                var registry = new LocalizationSourceRegistry(false); registry.Register("test", provider);
                using var runtime = new LocalizationRuntime(project, registry);
                Assert.That(runtime.InitializeAsync("ru").IsCompletedSuccessfully, Is.True);
                var snapshot = runtime.GetChannel();
                var unchangedReport = CoverageAnalyzer.Analyze(_config, null, provider);
                Assert.That(unchangedReport.TextCoverage.Single(c => c.LanguageCode == "ru").PresentCount, Is.EqualTo(3));
                Assert.That(runtime.GetChannel(), Is.SameAs(snapshot));
                Assert.That(runtime.Get("empty"), Is.EqualTo("empty"));
                Assert.That(runtime.Get("fallback"), Is.EqualTo("Hello")); Assert.That(runtime.Get("space"), Is.EqualTo(" \n"));
            }
            finally { Object.DestroyImmediate(project); }
        }

        [Test] public void Coverage_WithFailedDiscovery_CannotReportAnUnqualifiedSuccessfulCheck()
        {
            Write("en", "a", "same"); Write("ru", "b", "same"); ExpectDiscoveryFailure("belongs to both");
            var report = CoverageAnalyzer.Analyze(_config, _data, new Provider());
            Assert.That(report.IsComplete, Is.False); Assert.That(report.Warnings.Any(w => w.Message.Contains("Editor discovery failed")), Is.True);
        }

        [Test] public void Coverage_FullDiskSnapshotDoesNotResurrectADeletedKeyFromAnOldImport()
        {
            Write("en", "main", "current"); _data.Load(_config);
            var provider = new Provider(); provider.Text["en"] = new Dictionary<string, string> { ["removed"] = "stale" };
            var report = CoverageAnalyzer.Analyze(_config, _data, provider);
            var en = report.TextCoverage.Single(c => c.LanguageCode == "en");
            Assert.That(en.TotalCount, Is.EqualTo(1)); Assert.That(en.TranslatedCount, Is.EqualTo(1));
            Assert.That(report.Warnings.Any(w => w.Key == "removed"), Is.False);
        }

        [Test] public void Coverage_RejectsOverlayFromAnotherConfiguration()
        {
            Write("en", "main", "wrong.context"); _data.Load(_config);
            _config.resourcesBasePath += "/other";
            var report = CoverageAnalyzer.Analyze(_config, _data, new Provider());
            Assert.That(report.IsComplete, Is.False); Assert.That(report.TextCoverage.All(c => c.TotalCount == 0), Is.True);
            Assert.That(report.Warnings.Any(w => w.Message.Contains("another Resources path")), Is.True);
        }

        [Test] public void LocalPackageData_IsDetectedWithoutWritingIntoThePackage()
        {
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(LocalizationConfig).Assembly);
            Assert.That(package.source, Is.EqualTo(UnityEditor.PackageManager.PackageSource.Local));
            string name = "__Discovery_" + Guid.NewGuid().ToString("N");
            string folder = Path.Combine(package.resolvedPath, name, "Resources", _config.resourcesBasePath, "en", "text");
            Directory.Delete(_folder); // Empty fixture folder; leave the package as the only matching data root.
            try
            {
                Directory.CreateDirectory(folder);
                string path = Path.Combine(folder, "main.json");
                File.WriteAllText(path, new JObject { ["translations"] = new JObject { ["key"] = "value" } }.ToString());
                AssetDatabase.Refresh(); byte[] before = File.ReadAllBytes(path);
                ExpectDiscoveryFailure("in a package");
                Assert.That(File.ReadAllBytes(path), Is.EqualTo(before));
            }
            finally { Assert.That(AssetDatabase.DeleteAsset(package.assetPath + "/" + name), Is.True); }
        }

        private sealed class Provider : IEditorLocalizationDataSource, ILocalizationSourceProvider
        {
            internal readonly Dictionary<string, Dictionary<string, string>> Text = new();
            public Dictionary<string, string> LoadTextData(string languageCode) => Text.TryGetValue(languageCode, out var entries) ? entries : new();
            public List<LocalizationAssetTable> LoadAssetTables(string languageCode) => new();
            public T LoadAsset<T>(string languageCode, string path) where T : Object => null;
            public bool HasTextData(string languageCode) => Text.ContainsKey(languageCode);
            public LocalizationProviderCapabilities GetCapabilities(LocalizationSourceKind kind) => new(LocalizationLoadUnit.Table, false, false);
            public System.Threading.Tasks.Task<LocalizationSourceLease> LoadAsync(LocalizationSourceRequest request, System.Threading.CancellationToken token)
                => System.Threading.Tasks.Task.FromResult(new LocalizationSourceLease(LocalizationSourceData.FromText(LoadTextData(request.Language)), () => System.Threading.Tasks.Task.CompletedTask));
        }
    }
}
