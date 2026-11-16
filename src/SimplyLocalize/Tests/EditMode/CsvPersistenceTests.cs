using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using SimplyLocalize.Editor.Data;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace SimplyLocalize.Tests
{
    public class CsvPersistenceTests
    {
        private string _root, _folder, _enPath, _ruPath, _metaPath;
        private LocalizationConfig _config;
        private readonly List<Object> _objects = new();
        private EditorLocalizationData _data;

        [SetUp]
        public void SetUp()
        {
            string id = "CsvTests_" + Guid.NewGuid().ToString("N");
            _root = "Assets/__" + id;
            _folder = Path.GetFullPath(Path.Combine(_root, "Resources", id));
            _enPath = Path.Combine(_folder, "en", "text", "menu.json");
            _ruPath = Path.Combine(_folder, "ru", "text", "menu.json");
            _metaPath = Path.Combine(_folder, "_meta.json");
            _config = ScriptableObject.CreateInstance<LocalizationConfig>(); _objects.Add(_config);
            _config.resourcesBasePath = id;
            foreach (string code in new[] { "en", "ru" })
            {
                var profile = ScriptableObject.CreateInstance<LanguageProfile>(); _objects.Add(profile);
                profile.languageCode = code; _config.languages.Add(profile);
                string path = Path.Combine(_folder, code, "text", "menu.json");
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, "{\"translations\":{\"key\":\"original " + code + "\"},\"note\":\"preserve\"}");
            }
            _config.defaultLanguage = _config.languages[0];
            File.WriteAllText(_metaPath, "{\"key\":{\"description\":\"original description\",\"previousKeys\":[\"old-key\"]}}");
            _data = new EditorLocalizationData(); _data.Load(_config);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var obj in _objects) if (obj != null && !AssetDatabase.Contains(obj)) Object.DestroyImmediate(obj);
            _objects.Clear();
            AssetDatabase.DeleteAsset(_root);
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
            if (File.Exists(_root + ".meta")) File.Delete(_root + ".meta");
        }

        [Test]
        public void DefaultImport_SkipsEmptyTranslationsAndDescriptions()
        {
            Assert.That(CsvExporter.Import(_data, "Key,File,Description,en,ru\nkey,menu,,,new ru"), Is.EqualTo(1));
            Assert.That(_data.GetTranslation("key", "en"), Is.EqualTo("original en"));
            Assert.That(_data.GetDescription("key"), Is.EqualTo("original description"));
            Assert.That(_data.GetTranslation("key", "ru"), Is.EqualTo("new ru"));
            Assert.That(_data.GetPreviousKeys("key"), Is.EqualTo(new[] { "old-key" }));
        }

        [Test]
        public void ExplicitClear_ChangesOnlyPresentColumnsAndPreservesHistory()
        {
            CsvExporter.Import(_data, "Key,File,Description,en\nkey,menu,,", CsvEmptyCellPolicy.Clear);
            Assert.That(_data.GetTranslation("key", "en"), Is.Empty);
            Assert.That(_data.GetTranslation("key", "ru"), Is.EqualTo("original ru"));
            Assert.That(_data.GetDescription("key"), Is.Empty);
            Assert.That(_data.GetPreviousKeys("key"), Is.EqualTo(new[] { "old-key" }));
            _data.SaveAll();
            var reload = new EditorLocalizationData(); reload.Load(_config);
            Assert.That(reload.GetPreviousKeys("key"), Is.EqualTo(new[] { "old-key" }));
        }

        [Test]
        public void ExportImport_RoundTripsWhitespaceQuotesNewlinesAndUnicode()
        {
            const string value = "  Привет, \"мир\"\r\nline\rnext\n😀  ";
            const string key = " key,\"quoted\" ";
            _data.AddKey(key, "menu");
            _data.SetTranslation(key, "en", value);
            _data.SetDescription(key, value);
            string csv = CsvExporter.Export(_data, new List<string> { "en", "ru" });
            var target = new EditorLocalizationData(); target.Load(_config);
            CsvExporter.Import(target, csv);
            Assert.That(target.GetTranslation(key, "en"), Is.EqualTo(value));
            Assert.That(target.GetDescription(key), Is.EqualTo(value));
            CsvExporter.Import(target, "Key,File,en\nkey,menu,   ");
            Assert.That(target.GetTranslation("key", "en"), Is.EqualTo("   "));
        }

        [TestCase("\n")]
        [TestCase("\r\n")]
        [TestCase("\r")]
        public void LegacyHeader_BomAndRecordSeparatorsRemainSupported(string newline)
        {
            CsvExporter.Import(_data, "\uFEFF\"Key\",\"File\",\"en\"" + newline + "key,menu,updated" + newline);
            Assert.That(_data.GetTranslation("key", "en"), Is.EqualTo("updated"));
        }

        [TestCase("Other,File,en\nkey,menu,new")]
        [TestCase("Key,File,Description\nkey,menu,new")]
        [TestCase("Key,File,en,en\nkey,menu,new,new")]
        [TestCase("Key,File,en,EN\nkey,menu,new,new")]
        [TestCase("Key,File,EN\nkey,menu,new")]
        [TestCase("Key,File,\nkey,menu,new")]
        [TestCase("Key,File,en\nkey,menu,new\nkey,menu,duplicate")]
        [TestCase("Key,File,en\nkey,menu,new\nnext,menu")]
        [TestCase("Key,File,en\nkey,menu,new\nnext,menu,x,extra")]
        [TestCase("Key,File,en\nkey,menu,new\n,menu,x")]
        [TestCase("Key,File,en\nkey,menu,new\nnext,menu,\"unterminated")]
        [TestCase("Key,File,en\nkey,menu,new\nnext,menu,\"x\"junk")]
        [TestCase("Key,File,en\nkey,menu,new\nnext,menu,x\"y")]
        [TestCase("Key,File,en\nkey,other,new")]
        [TestCase("Key,File,en\nnext,MENU,new")]
        public void InvalidCsv_DoesNotApplyEarlierRowsOrWriteAnything(string csv)
        {
            string before = CsvExporter.Export(_data, new List<string> { "en", "ru" });
            var disk = Snapshot();
            Assert.Throws<FormatException>(() => CsvExporter.Import(_data, csv));
            Assert.That(CsvExporter.Export(_data, new List<string> { "en", "ru" }), Is.EqualTo(before));
            AssertSnapshot(disk);
        }

        [TestCase("../escape")]
        [TestCase("a/b")]
        [TestCase("a\\b")]
        [TestCase("C:escape")]
        [TestCase("CON")]
        [TestCase("aux.json")]
        [TestCase("LPT1")]
        [TestCase("menu.")]
        public void UnsafeFileAndLanguageNames_AreRejectedBeforeMutation(string name)
        {
            Assert.Throws<FormatException>(() => CsvExporter.Import(_data, "Key,File,en\nnext," + name + ",value"));
            Assert.Throws<FormatException>(() => CsvExporter.Import(_data, "Key,File," + name + "\nnext,menu,value"));
            Assert.That(_data.KeyCount, Is.EqualTo(1));
        }

        [Test]
        public void PreviewAndCancel_AreReadOnly_AndStalePreviewCannotApply()
        {
            var disk = Snapshot();
            var plan = CsvExporter.Preview(_data, "Key,File,Description,en,fr\nnext,menu,new note,hello,bonjour");
            Assert.That(plan.NewLanguages, Is.EqualTo(new[] { "fr" }));
            Assert.That(plan.Changes.Count, Is.GreaterThan(0));
            Assert.That(_data.KeyCount, Is.EqualTo(1));
            Assert.That(_config.languages.Count, Is.EqualTo(2));
            AssertSnapshot(disk);
            _data.SetTranslation("key", "en", "manual edit");
            Assert.Throws<InvalidOperationException>(() => plan.Apply(_data, true));
            Assert.That(_data.GetTranslation("key", "en"), Is.EqualTo("manual edit"));
            AssertSnapshot(disk);
        }

        [Test]
        public void RepeatedImport_IsIdempotent_AndUsesGlobalForNewKeysWithEmptyFile()
        {
            const string csv = "Key,File,en\nnew,,value";
            CsvExporter.Preview(_data, csv).Apply(_data, true);
            var first = Snapshot();
            var plan = CsvExporter.Preview(_data, csv);
            Assert.That(plan.Changes, Is.Empty);
            plan.Apply(_data, true);
            AssertSnapshot(first);
            Assert.That(_data.GetFileForKey("new"), Is.EqualTo("global"));
        }

        [Test]
        public void SaveAll_PreservesAdditionalJsonFieldsAndRoundTripsNewLanguage()
        {
            CsvExporter.Import(_data, "Key,File,Description,en,fr\nkey,menu,changed,edited,bonjour");
            _data.SaveAll();
            Assert.That(JObject.Parse(File.ReadAllText(_enPath))["note"].Value<string>(), Is.EqualTo("preserve"));
            Assert.That(JObject.Parse(File.ReadAllText(Path.Combine(_folder, "fr/text/menu.json")))["translations"]["key"].Value<string>(), Is.EqualTo("bonjour"));
            Assert.That(_data.GetPreviousKeys("key"), Is.EqualTo(new[] { "old-key" }));
        }

        [Test]
        public void MetadataError_PreventsAnyTranslationWrite_AndImportDoesNotChangeMemory()
        {
            File.WriteAllText(_metaPath, "{");
            LogAssert.Expect(LogType.Warning, new Regex("Failed to load meta"));
            _data.Reload(_config);
            var disk = Snapshot();
            var plan = CsvExporter.Preview(_data, "Key,File,en\nkey,menu,edited");
            Assert.Throws<InvalidOperationException>(() => plan.Apply(_data, true));
            Assert.That(_data.GetTranslation("key", "en"), Is.EqualTo("original en"));
            AssertSnapshot(disk);
        }

        [Test]
        public void ExternalTranslationEdit_IsNeverOverwritten()
        {
            var plan = CsvExporter.Preview(_data, "Key,File,en\nkey,menu,edited");
            File.WriteAllText(_ruPath, "{\"translations\":{\"key\":\"external\"}}");
            var disk = Snapshot();
            Assert.Throws<InvalidOperationException>(() => plan.Apply(_data, true));
            AssertSnapshot(disk);
            Assert.That(_data.GetTranslation("key", "en"), Is.EqualTo("original en"));
        }

        [Test]
        public void ExistingButUnloadedLanguageFile_IsNotOverwritten()
        {
            string path = Path.Combine(_folder, "fr/text/menu.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, "{\"translations\":{\"other\":\"keep\"}}");
            var disk = Snapshot();
            var plan = CsvExporter.Preview(_data, "Key,File,fr\nkey,menu,new");
            Assert.Throws<InvalidOperationException>(() => plan.Apply(_data, true));
            AssertSnapshot(disk);
        }

        [Test, Platform("Win")]
        public void LateWriteFailure_RollsBackEarlierFilesAndMetadata_AndCanRetry()
        {
            // Metadata is enlisted last, so its sharing violation occurs after language replacements.
            var disk = Snapshot();
            var plan = CsvExporter.Preview(_data, "Key,File,Description,en,ru\nkey,menu,changed,new en,new ru");
            using (new FileStream(_metaPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                Assert.Throws<IOException>(() => plan.Apply(_data, true));
            AssertSnapshot(disk);
            Assert.That(_data.GetDescription("key"), Is.EqualTo("original description"));
            plan.Apply(_data, true);
            Assert.That(_data.GetTranslation("key", "ru"), Is.EqualTo("new ru"));
        }

        [Test, Platform("Win")]
        public void SingleFileFailure_DoesNotTruncateAndKeepsPendingEdit()
        {
            var bytes = File.ReadAllBytes(_enPath);
            _data.SetTranslation("key", "en", "pending");
            using (new FileStream(_enPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                Assert.Throws<IOException>(() => _data.SaveFile("menu", "en"));
            Assert.That(File.ReadAllBytes(_enPath), Is.EqualTo(bytes));
            Assert.That(_data.GetTranslation("key", "en"), Is.EqualTo("pending"));
            _data.SaveFile("menu", "en");
            StringAssert.Contains("pending", File.ReadAllText(_enPath));
        }

        [Test]
        public void SaveFile_RejectsTraversalAndMissingBasePath()
        {
            Assert.Throws<FormatException>(() => _data.SaveFile("../outside", "en"));
            Assert.Throws<FormatException>(() => _data.SaveFile("menu", "../outside"));
            Assert.Throws<InvalidOperationException>(() => new EditorLocalizationData().SaveFile("menu", "en"));
        }

        [Test]
        public void NewProfiles_AreCreatedOnlyOnSuccessfulApplyAndSurviveReload()
        {
            SaveConfigAsset();
            var plan = CsvExporter.Preview(_data, "Key,File,en,fr\nkey,menu,hello,bonjour");
            plan.ApplyAndSave(_data, _config);
            Assert.That(_config.GetProfile("fr"), Is.Not.Null);
            Assert.That(AssetDatabase.Contains(_config.GetProfile("fr")), Is.True);
            var reload = new EditorLocalizationData(); reload.Load(_config);
            Assert.That(reload.GetTranslation("key", "fr"), Is.EqualTo("bonjour"));
            CsvExporter.Preview(_data, "Key,File,en,fr\nkey,menu,hello,bonjour").ApplyAndSave(_data, _config);
            Assert.That(_config.languages.Count(p => p.Code == "fr"), Is.EqualTo(1));
        }

        [Test, Platform("Win")]
        public void FailedImport_DoesNotCreateProfilesOrChangeConfig()
        {
            SaveConfigAsset();
            var disk = Snapshot();
            var plan = CsvExporter.Preview(_data, "Key,File,Description,en,fr\nkey,menu,changed,hello,bonjour");
            using (new FileStream(_metaPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                Assert.Throws<IOException>(() => plan.ApplyAndSave(_data, _config));
            Assert.That(_config.GetProfile("fr"), Is.Null);
            Assert.That(_data.LoadedLanguages, Does.Not.Contain("fr"));
            AssertSnapshot(disk);
            Assert.That(Directory.Exists(Path.Combine(_folder, "fr")), Is.False);
        }

        [TestCase("")]
        [TestCase("Key,File,fr")]
        public void EmptyImport_DoesNotSavePendingEditsOrAddProfiles(string csv)
        {
            var disk = Snapshot();
            _data.SetTranslation("key", "en", "pending");
            CsvExporter.Preview(_data, csv).ApplyAndSave(_data, _config);
            AssertSnapshot(disk);
            Assert.That(_data.GetTranslation("key", "en"), Is.EqualTo("pending"));
            Assert.That(_data.LoadedLanguages, Does.Not.Contain("fr"));
        }

        [Test]
        public void InvalidUtf8_RejectsImportWithoutReplacingCharacters()
        {
            string path = Path.Combine(_root, "invalid.csv");
            File.WriteAllBytes(path, Encoding.UTF8.GetBytes("Key,File,en\nkey,menu,").Concat(new byte[] { 0xff }).ToArray());
            var disk = Snapshot();
            Assert.Throws<DecoderFallbackException>(() => CsvExporter.ImportFromFile(_data, path));
            Assert.That(_data.GetTranslation("key", "en"), Is.EqualTo("original en"));
            AssertSnapshot(disk);
        }

        [Test, Platform("Win")]
        public void SaveFileAllLanguages_RollsBackEarlierLanguageWhenLaterWriteFails()
        {
            var disk = Snapshot();
            _data.SetTranslation("key", "en", "new en"); _data.SetTranslation("key", "ru", "new ru");
            using (new FileStream(_ruPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                Assert.Throws<IOException>(() => _data.SaveFileAllLanguages("menu"));
            AssertSnapshot(disk);
        }

        [Test]
        public void ReadOnlyDestination_RejectsEntireBatchBeforeAnyWrite()
        {
            var disk = Snapshot();
            var attributes = File.GetAttributes(_ruPath);
            _data.SetTranslation("key", "en", "edited"); _data.SetTranslation("key", "ru", "edited");
            try
            {
                File.SetAttributes(_ruPath, attributes | FileAttributes.ReadOnly);
                Assert.Throws<UnauthorizedAccessException>(() => _data.SaveAll());
                AssertSnapshot(disk);
            }
            finally { File.SetAttributes(_ruPath, attributes); }
        }

        [Test]
        public void FailureAfterExternalWrites_RestoresConfigProfileAndTranslations()
        {
            // Unity owns asset serialization: fail after that callback writes, not before JSON commit.
            SaveConfigAsset();
            var disk = Snapshot();
            string configPath = AssetDatabase.GetAssetPath(_config);
            string profilePath = _root + "/new-profile.asset";
            WithTransaction((tx, call) =>
            {
                call("Add", new object[] { _enPath, File.ReadAllBytes(_enPath), Encoding.UTF8.GetBytes("updated") });
                call("TrackExternal", new object[] { configPath });
                call("TrackExternal", new object[] { profilePath });
                var error = Assert.Throws<TargetInvocationException>(() => call("Execute", new object[] { (Action)(() =>
                {
                    call("TouchExternal", new object[] { configPath });
                    File.WriteAllText(configPath, "changed config");
                    call("TouchExternal", new object[] { profilePath });
                    File.WriteAllText(profilePath, "new profile");
                    throw new IOException("Simulated failure after asset writes");
                }) }));
                Assert.That(error.InnerException, Is.TypeOf<IOException>());
            });
            AssertSnapshot(disk);
        }

        [Test]
        public void ExternalFileAppearingBeforeUnityWrite_IsNotDeletedByRollback()
        {
            string path = _root + "/external.asset";
            WithTransaction((tx, call) =>
            {
                call("TrackExternal", new object[] { path });
                Assert.Throws<TargetInvocationException>(() => call("Execute", new object[] { (Action)(() =>
                {
                    File.WriteAllText(path, "someone else's file");
                    call("TouchExternal", new object[] { path });
                }) }));
            });
            Assert.That(File.ReadAllText(path), Is.EqualTo("someone else's file"));
        }

        private static void WithTransaction(Action<object, Action<string, object[]>> action)
        {
            var type = typeof(EditorLocalizationData).Assembly.GetType("SimplyLocalize.Editor.Data.EditorFileTransaction");
            using var tx = (IDisposable)Activator.CreateInstance(type, true);
            action(tx, (method, args) => type.GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(tx, args));
        }

        [Test]
        public void ChangedConfigPath_RejectsPreviewWithoutSavingToOldFolder()
        {
            var disk = Snapshot();
            var plan = CsvExporter.Preview(_data, "Key,File,en\nkey,menu,edited");
            _config.resourcesBasePath += "_changed";
            Assert.Throws<InvalidOperationException>(() => plan.ApplyAndSave(_data, _config));
            AssertSnapshot(disk);
            Assert.That(_data.GetTranslation("key", "en"), Is.EqualTo("original en"));
        }

        [Test]
        public void ExtraJsonFields_PreserveDecimalPrecisionAndDateStrings()
        {
            File.WriteAllText(_enPath, "{\"translations\":{\"key\":\"original en\"},\"amount\":0.1234567890123456789012345678,\"date\":\"2026-11-16T00:00:00Z\"}");
            _data.Reload(_config);
            _data.SetTranslation("key", "en", "edited");
            _data.SaveAll();
            string result = File.ReadAllText(_enPath);
            StringAssert.Contains("0.1234567890123456789012345678", result);
            StringAssert.Contains("2026-11-16T00:00:00Z", result);
        }

        [Test, Platform("Win")]
        public void LockedConfig_RejectsImportBeforeNativeSerialization()
        {
            SaveConfigAsset();
            var disk = Snapshot();
            string configPath = AssetDatabase.GetAssetPath(_config);
            var plan = CsvExporter.Preview(_data, "Key,File,en,fr\nkey,menu,hello,bonjour");
            using (new FileStream(configPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                Assert.Throws<IOException>(() => plan.ApplyAndSave(_data, _config));
            Assert.That(_config.GetProfile("fr"), Is.Null);
            Assert.That(_data.LoadedLanguages, Does.Not.Contain("fr"));
            Assert.That(EditorUtility.IsDirty(_config), Is.False);
            AssertSnapshot(disk);
            Assert.That(File.Exists(_root + "/Profile_fr.asset.meta"), Is.False);
            Assert.That(Directory.Exists(Path.Combine(_folder, "fr")), Is.False);
            plan.ApplyAndSave(_data, _config);
            Assert.That(_config.GetProfile("fr"), Is.Not.Null);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ConfigSerializerFailure_AfterProfileCreation_RollsBackWholeImport(bool pendingConfigEdit)
        {
            SaveConfigAsset();
            string originalName = _config.name;
            if (pendingConfigEdit) { _config.name = "PendingConfig"; EditorUtility.SetDirty(_config); }
            var disk = Snapshot();
            var plan = CsvExporter.Preview(_data, "Key,File,en,fr\nkey,menu,hello,bonjour");
            var service = typeof(CsvImportPlan).Assembly.GetType("SimplyLocalize.Editor.Data.CsvProfileImport");
            bool reachedSerializer = false;
            Action<LocalizationConfig> fail = config =>
            {
                reachedSerializer = config.GetProfile("fr") != null && AssetDatabase.Contains(config.GetProfile("fr"));
                AssetDatabase.SaveAssetIfDirty(config);
                throw new IOException("Simulated failure after config serialization");
            };
            var error = Assert.Throws<TargetInvocationException>(() => service.GetMethod("Apply", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { plan, _data, _config, fail }));
            Assert.That(error.InnerException, Is.TypeOf<IOException>());
            Assert.That(reachedSerializer, Is.True);
            Assert.That(_config.GetProfile("fr"), Is.Null);
            Assert.That(_data.LoadedLanguages, Does.Not.Contain("fr"));
            Assert.That(EditorUtility.IsDirty(_config), Is.EqualTo(pendingConfigEdit));
            Assert.That(_config.name, Is.EqualTo(pendingConfigEdit ? "PendingConfig" : originalName));
            AssertSnapshot(disk);
            Assert.That(File.Exists(_root + "/Profile_fr.asset.meta"), Is.False);
            Assert.That(Directory.Exists(Path.Combine(_folder, "fr")), Is.False);
            plan.ApplyAndSave(_data, _config);
            Assert.That(_config.GetProfile("fr"), Is.Not.Null);
        }

        private void SaveConfigAsset()
        {
            AssetDatabase.Refresh();
            foreach (var profile in _config.languages) AssetDatabase.CreateAsset(profile, _root + "/" + profile.Code + ".asset");
            AssetDatabase.CreateAsset(_config, _root + "/Config.asset");
            AssetDatabase.SaveAssets();
        }

        private Dictionary<string, byte[]> Snapshot() => Directory.GetFiles(_root, "*", SearchOption.AllDirectories)
            .Where(p => !p.EndsWith(".meta")).ToDictionary(Path.GetFullPath, File.ReadAllBytes);

        private void AssertSnapshot(Dictionary<string, byte[]> expected)
        {
            var actual = Snapshot();
            Assert.That(actual.Keys, Is.EquivalentTo(expected.Keys));
            foreach (var entry in expected) Assert.That(actual[entry.Key], Is.EqualTo(entry.Value), entry.Key);
        }
    }
}
