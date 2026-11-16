using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using SimplyLocalize.Editor.Data;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace SimplyLocalize.Tests
{
    public class MetadataPersistenceTests
    {
        private string _root, _folder, _path;
        private LocalizationConfig _config;
        private LanguageProfile _en;

        [SetUp]
        public void SetUp()
        {
            string id = "MetadataTests_" + Guid.NewGuid().ToString("N");
            _root = Path.Combine(Application.dataPath, "__" + id);
            _folder = Path.Combine(_root, "Resources", id);
            _path = Path.Combine(_folder, "_meta.json");
            Directory.CreateDirectory(Path.Combine(_folder, "en", "text"));
            File.WriteAllText(Path.Combine(_folder, "en", "text", "test.json"),
                "{\"translations\":{\"key\":\"value\"}}");
            _en = ScriptableObject.CreateInstance<LanguageProfile>();
            _en.languageCode = "en";
            _config = ScriptableObject.CreateInstance<LocalizationConfig>();
            _config.resourcesBasePath = id;
            _config.languages.Add(_en);
            _config.defaultLanguage = _en;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_config);
            Object.DestroyImmediate(_en);
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
            if (File.Exists(_root + ".meta")) File.Delete(_root + ".meta");
        }

        private EditorLocalizationData Load(string json = null)
        {
            if (json != null) File.WriteAllText(_path, json);
            var data = new EditorLocalizationData();
            data.Load(_config);
            return data;
        }

        [Test]
        public void EscapedMetadata_RoundTripsWithoutLosingCharacters()
        {
            var data = Load("{\"key\":{\"description\":\"\\u041F\\b\\f\\u0001\\n\\t\\r\\\\\\\"/\",\"previousKeys\":[\"\\u043A\\\\old\"]}}");
            Assert.That(data.GetDescription("key"), Is.EqualTo("П\b\f\u0001\n\t\r\\\"/"));
            Assert.That(data.GetPreviousKeys("key"), Is.EqualTo(new[] { "к\\old" }));
            const string unusualKey = "Ключ/\"\\\u0002";
            data.SetDescription(unusualKey, "Unicode key");
            data.SaveMeta();
            var reloaded = Load();
            Assert.That(reloaded.GetDescription(unusualKey), Is.EqualTo("Unicode key"));
            Assert.That(reloaded.GetDescription("key"), Is.EqualTo(data.GetDescription("key")));
            Assert.That(reloaded.GetPreviousKeys("key"), Is.EqualTo(data.GetPreviousKeys("key")));
        }

        [TestCase("null")]
        [TestCase("42")]
        [TestCase("true")]
        [TestCase("{}")]
        [TestCase("[]")]
        [Timeout(3000)]
        public void NonStringHistory_IsRejectedWithoutHangingOrPublishingPartialMetadata(string element)
        {
            string json = "{\"good\":{\"description\":\"ok\"},\"key\":{\"previousKeys\":[\"old\"," + element + "]}}";
            LogAssert.Expect(LogType.Warning, new Regex(@"Failed to load meta.*_meta\.json.*previousKeys\[1\].*line", RegexOptions.Singleline));
            var data = Load(json);
            Assert.That(data.GetMeta("good"), Is.Null);
            Assert.That(data.GetMeta("key"), Is.Null);
            Assert.Throws<InvalidOperationException>(() => data.SaveMeta());
            Assert.That(File.ReadAllText(_path), Is.EqualTo(json));
        }

        [TestCase("")]
        [TestCase("{")]
        [TestCase("[]")]
        [TestCase("null")]
        [TestCase("{\"key\":{\"description\":42}}")]
        [TestCase("{\"key\":{\"previousKeys\":\"old\"}}")]
        [TestCase("{\"key\":null}")]
        [TestCase("{\"key\":{\"description\":\"unterminated}}")]
        [TestCase("{\"key\":{\"description\":\"ok\"}} trailing")]
        [TestCase("{\"key\":{},\"key\":{}}")]
        [TestCase("{\"key\":{\"description\":\"a\",\"description\":\"b\"}}")]
        public void InvalidFile_IsDiagnosedAndNeverOverwritten(string json)
        {
            LogAssert.Expect(LogType.Warning, new Regex(@"Failed to load meta.*_meta\.json", RegexOptions.Singleline));
            var data = Load(json);
            data.SetDescription("key", "new description");
            var error = Assert.Throws<InvalidOperationException>(() => data.SaveMeta());
            StringAssert.Contains("_meta.json", error.Message);
            Assert.That(File.ReadAllText(_path), Is.EqualTo(json));
        }

        [Test]
        public void RepairRequiresReload_ThenSavingWorks()
        {
            LogAssert.Expect(LogType.Warning, new Regex("Failed to load meta"));
            var data = Load("{");
            File.WriteAllText(_path, "{\"key\":{\"description\":\"repaired\"}}");
            Assert.Throws<InvalidOperationException>(() => data.SaveMeta());
            data.Reload(_config);
            Assert.That(data.GetDescription("key"), Is.EqualTo("repaired"));
            data.SetDescription("key", "edited");
            data.SaveMeta();
            Assert.That(Load().GetDescription("key"), Is.EqualTo("edited"));
        }

        [Test]
        public void InvalidUtf8_IsNotSilentlyReplacedAndSaved()
        {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes("{\"key\":{\"description\":\"x\"}}");
            bytes[Array.IndexOf(bytes, (byte)'x')] = 0xff;
            File.WriteAllBytes(_path, bytes);
            LogAssert.Expect(LogType.Warning, new Regex(@"Failed to load meta.*_meta\.json", RegexOptions.Singleline));
            var data = Load();
            Assert.Throws<InvalidOperationException>(() => data.SaveMeta());
            Assert.That(File.ReadAllBytes(_path), Is.EqualTo(bytes));
        }

        [Test, Timeout(3000)]
        public void ExcessiveNesting_IsDiagnosedWithoutPublishingOrOverwriting()
        {
            string json = "{\"key\":{\"extra\":" + new string('[', 100) + "0" + new string(']', 100) + "}}";
            LogAssert.Expect(LogType.Warning, new Regex(@"Failed to load meta.*MaxDepth", RegexOptions.Singleline));
            var data = Load(json);
            Assert.That(data.GetMeta("key"), Is.Null);
            Assert.Throws<InvalidOperationException>(() => data.SaveMeta());
            Assert.That(File.ReadAllText(_path), Is.EqualTo(json));
        }

        [Test, Platform("Win")]
        public void ReadFailure_BlocksSavingEvenAfterFileBecomesReadable()
        {
            const string original = "{\"key\":{\"description\":\"keep\"}}";
            File.WriteAllText(_path, original);
            EditorLocalizationData data;
            using (new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                LogAssert.Expect(LogType.Warning, new Regex(@"Failed to load meta.*_meta\.json", RegexOptions.Singleline));
                data = Load();
            }
            Assert.Throws<InvalidOperationException>(() => data.SaveMeta());
            Assert.That(File.ReadAllText(_path), Is.EqualTo(original));
            data.Reload(_config);
            Assert.That(data.GetDescription("key"), Is.EqualTo("keep"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ExternalChangeOrDeletion_RequiresReload(bool delete)
        {
            var data = Load("{\"key\":{\"description\":\"original\"}}");
            data.SetDescription("key", "local edit");
            const string external = "{\"key\":{\"description\":\"external edit\"}}";
            if (delete) File.Delete(_path); else File.WriteAllText(_path, external);
            Assert.Throws<InvalidOperationException>(() => data.SaveMeta());
            if (delete) Assert.That(File.Exists(_path), Is.False);
            else Assert.That(File.ReadAllText(_path), Is.EqualTo(external));
        }

        [Test]
        public void FileCreatedAfterLoad_IsNotOverwritten()
        {
            var data = Load();
            File.WriteAllText(_path, "{}");
            data.SetDescription("key", "local");
            Assert.Throws<InvalidOperationException>(() => data.SaveMeta());
            Assert.That(File.ReadAllText(_path), Is.EqualTo("{}"));
        }

        [Test]
        public void UnknownFields_SurviveEditsAndMetadataRename()
        {
            var data = Load("{\"key\":{\"description\":\"old\",\"extra\":{\"text\":\"} ] \\\" \",\"date\":\"2026-01-02T00:00:00Z\",\"flags\":[true,null,7]}}}");
            var extra = JObject.Parse(File.ReadAllText(_path))["key"]["extra"].DeepClone();
            data.SetDescription("key", null);
            data.RenameKey("key", "renamed");
            data.SaveMeta();
            var written = JObject.Parse(File.ReadAllText(_path));
            Assert.That(JToken.DeepEquals(written["renamed"]["extra"], extra), Is.True);
            Assert.That(Load().GetPreviousKeys("renamed"), Is.EqualTo(new[] { "key" }));
        }

        [Test]
        public void UnknownOnlyMetadata_IsNotDeletedAsEmpty()
        {
            var data = Load("{\"key\":{\"extra\":{\"note\":\"keep\"}}}");
            data.SetDescription("key", null);
            data.SaveMeta();
            Assert.That(JObject.Parse(File.ReadAllText(_path))["key"]["extra"]["note"].Value<string>(), Is.EqualTo("keep"));
        }

        [Test]
        public void LegacyNullFields_AndMissingFileRemainSupported()
        {
            var data = Load("{\"key\":{\"description\":null,\"previousKeys\":null}}");
            Assert.That(data.GetDescription("key"), Is.Null);
            Assert.That(data.GetPreviousKeys("key"), Is.Empty);
            data.SaveMeta();
            Assert.That(File.Exists(_path), Is.False);
            data.SetDescription("key", "новое описание 😀");
            data.SaveMeta();
            Assert.That(Load().GetDescription("key"), Is.EqualTo("новое описание 😀"));
            Assert.That(data.HasPendingAssetRefresh, Is.True);
        }

        [Test]
        public void PublicMutableHistory_WithNullEntryDoesNotCorruptFile()
        {
            const string original = "{\"key\":{\"previousKeys\":[\"old\"]}}";
            var data = Load(original);
            data.GetMeta("key").previousKeys.Add(null);
            Assert.Throws<FormatException>(() => data.SaveMeta());
            Assert.That(File.ReadAllText(_path), Is.EqualTo(original));
            Assert.That(data.GetMeta("key").previousKeys.Count, Is.EqualTo(2));
        }

        [Test]
        public void MissingBasePath_FailsExplicitly()
        {
            var data = new EditorLocalizationData();
            data.SetDescription("key", "unsaved");
            Assert.Throws<InvalidOperationException>(() => data.SaveMeta());
            Assert.That(data.GetDescription("key"), Is.EqualTo("unsaved"));
        }

        [Test, Platform("Win")]
        public void ReplacementFailure_PreservesOriginalAndPendingEdits_ThenCanRetry()
        {
            const string original = "{\"key\":{\"description\":\"old\"}}";
            var data = Load(original);
            data.SetDescription("key", "new");
            // Reading is allowed, but Windows denies replacement without FileShare.Delete.
            using (new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Assert.Throws<IOException>(() => data.SaveMeta());
                Assert.That(File.ReadAllText(_path), Is.EqualTo(original));
            }
            Assert.That(Directory.GetFiles(_folder, "*.tmp"), Is.Empty);
            Assert.That(data.GetDescription("key"), Is.EqualTo("new"));
            data.SaveMeta();
            Assert.That(Load().GetDescription("key"), Is.EqualTo("new"));
        }

        [Test]
        public void RepeatedSaveAndReload_PreservesHistoryAndDoesNotLeaveTemporaryFiles()
        {
            var data = Load();
            data.SetDescription("key", "description");
            data.RenameKey("key", "renamed");
            data.SaveMeta();
            string first = File.ReadAllText(_path);
            data.SaveMeta();
            Assert.That(File.ReadAllText(_path), Is.EqualTo(first));
            Assert.That(Load().FindKeyByPreviousName("key"), Is.EqualTo("renamed"));
            Assert.That(Directory.GetFiles(_folder).Select(Path.GetFileName), Is.EqualTo(new[] { "_meta.json" }));
        }
    }
}
