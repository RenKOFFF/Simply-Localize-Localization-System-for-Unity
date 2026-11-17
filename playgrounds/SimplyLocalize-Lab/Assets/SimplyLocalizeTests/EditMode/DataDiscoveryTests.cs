using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using SimplyLocalize.Editor.Data;
using SimplyLocalize.Editor.Utilities;
using SimplyLocalize.Editor.Windows;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SimplyLocalize.Tests
{
    public class DataDiscoveryTests
    {
        private string root, addressRoot, folder;
        private LocalizationProjectConfig config;
        [SetUp] public void Setup()
        {
            addressRoot = "Discovery_" + Guid.NewGuid().ToString("N");
            root = "Assets/__" + addressRoot; folder = root + "/Resources/" + addressRoot;
            Directory.CreateDirectory(folder); AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            config = LocalizationProjectConfig.Create(new[] { "en", "ru" });
            AssetDatabase.CreateAsset(config, root + "/Project.asset");
        }
        [TearDown] public void Cleanup() { EditorProjectSelection.Invalidate(); AssetDatabase.DeleteAsset(root); }
        private string Write(string code, string file, string key)
        {
            string path = folder + "/" + code + "/text/" + file + ".json";
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, new JObject { ["custom"] = 42, ["translations"] = new JObject { [key] = code + " value" } }.ToString());
            var set = config.Sets[0];
            if (!set.Entries.Any(e => e.Key == key)) set.AddEntry(key);
            var source = new LocalizationSourceReference("resources", addressRoot + "/" + code + "/text/" + file, LocalizationSourceKind.TextTable);
            var existing = set.Sources.FirstOrDefault(s => s.Language == code)?.Sources ?? Array.Empty<LocalizationSourceReference>();
            if (!existing.Any(s => s.Address == source.Address)) set.SetSources(code, existing.Concat(new[] { source }).ToArray());
            Save(); return path;
        }
        private void Save() { EditorUtility.SetDirty(config); AssetDatabase.SaveAssetIfDirty(config); }
        private string Id(string key) => config.Sets[0].Entries.Single(e => e.Key == key).Id;
        private EditorProjectTextData Load() { AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport); return EditorProjectTextData.Load(config); }
        private static string Value(EditorProjectTextData data, string id, string language) => data.TryGetDirect(id, language, out var cell) ? cell.Value : null;
        private void Set(string key, string language, string value)
        { using var edit = new LocalizationProjectEdit(config, config.Sets[0].Id); edit.SetTranslation(Id(key), language, value); edit.Commit(); }

        [Test] public void FilesExclusiveToOtherLanguagesAreSavedAtTheirOriginalPaths()
        {
            Write("en", "main", "common"); string path = Write("ru", "extra", "only.ru");
            Assert.That(Value(Load(), Id("only.ru"), "ru"), Is.EqualTo("ru value"));
            Set("only.ru", "ru", "edited");
            var json = JObject.Parse(File.ReadAllText(path));
            Assert.That((string)json["translations"]["only.ru"], Is.EqualTo("edited")); Assert.That((int)json["custom"], Is.EqualTo(42));
            Assert.That(File.Exists(folder + "/en/text/extra.json"), Is.False);
        }
        [Test] public void NestedFilesWithSameLeafNameRemainSeparate()
        {
            string a = Write("en", "chapter1/main", "one"), b = Write("ru", "chapter2/main", "two");
            Load(); byte[] original = File.ReadAllBytes(a); Set("two", "ru", "edited");
            Assert.That(File.ReadAllBytes(a), Is.EqualTo(original));
            Assert.That((string)JObject.Parse(File.ReadAllText(b))["translations"]["two"], Is.EqualTo("edited"));
            Assert.That(File.Exists(folder + "/ru/text/main.json"), Is.False);
        }
        [Test] public void DefaultLanguageWithoutSourcesDoesNotHideOtherLanguages()
        {
            Write("ru", "extra", "only.ru"); var data = Load();
            Assert.That(Value(data, Id("only.ru"), "en"), Is.Null);
            Assert.That(Value(data, Id("only.ru"), "ru"), Is.EqualTo("ru value"));
        }
        [Test] public void SameExactResourcesAddressInTwoRootsBlocksEditing()
        {
            string path = Write("en", "main", "first");
            string other = root + "/Second/Resources/" + addressRoot + "/en/text/main.json";
            Directory.CreateDirectory(Path.GetDirectoryName(other)); File.Copy(path, other); byte[] before = File.ReadAllBytes(path);
            Assert.Throws<InvalidOperationException>(() => Load());
            Assert.Throws<InvalidOperationException>(() => { using var edit = new LocalizationProjectEdit(config, config.Sets[0].Id); });
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(before)); Assert.That(File.ReadAllBytes(other), Is.EqualTo(before));
        }
        [Test] public void OtherAddressesAndEditorOnlyDuplicatesDoNotCreateAmbiguity()
        {
            string path = Write("en", "main", "real");
            string hidden = root + "/Editor/Resources/" + addressRoot + "/en/text/main.json";
            Directory.CreateDirectory(Path.GetDirectoryName(hidden)); File.Copy(path, hidden);
            string other = root + "/Second/Resources/OtherDataset/main.json";
            Directory.CreateDirectory(Path.GetDirectoryName(other)); File.WriteAllText(other, "{broken");
            Assert.That(Value(Load(), Id("real"), "en"), Is.EqualTo("en value"));
        }
        [Test] public void NewConflictingAddressAfterLoadBlocksSaveWithoutLosingDraft()
        {
            string path = Write("en", "main", "key"); Load();
            using var edit = new LocalizationProjectEdit(config, config.Sets[0].Id); edit.SetTranslation(Id("key"), "en", "pending"); byte[] before = File.ReadAllBytes(path);
            string other = root + "/Second/Resources/" + addressRoot + "/en/text/main.json";
            Directory.CreateDirectory(Path.GetDirectoryName(other)); File.Copy(path, other); AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Assert.Throws<InvalidOperationException>(() => edit.Commit());
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(before));
            Assert.That(AssetDatabase.DeleteAsset(other), Is.True); edit.Commit();
            Assert.That(Value(Load(), Id("key"), "en"), Is.EqualTo("pending"));
        }
        [TestCase(false)][TestCase(true)] public void SameKeyInDifferentLanguageSourcesHasExplicitOwnership(bool sameLanguage)
        {
            string a = Write("en", "a", "same"), b = Write(sameLanguage ? "en" : "ru", "b", "same");
            byte[] before = File.ReadAllBytes(a);
            if (sameLanguage)
            { Assert.Throws<InvalidOperationException>(() => Load()); Assert.That(File.ReadAllBytes(a), Is.EqualTo(before)); }
            else
            { Load(); Set("same", "ru", "edited"); Assert.That(File.ReadAllBytes(a), Is.EqualTo(before)); Assert.That((string)JObject.Parse(File.ReadAllText(b))["translations"]["same"], Is.EqualTo("edited")); }
        }
        [TestCase("{bad")]
        [TestCase("{\"translations\":{\"same\":\"one\",\"same\":\"two\"}}")]
        [TestCase("{\"translations\":[]} ")]
        [TestCase("{\"translations\":{}} trailing")]
        [TestCase("{\"translations\":null}")][TestCase("{}")]
        [TestCase("{\"translations\":{},\"translations\":{}}")]
        public void MalformedSourceCannotPublishPartialDataOrPassCoverage(string json)
        {
            Write("en", "a", "valid"); string path = Write("ru", "broken", "bad"); File.WriteAllText(path, json);
            Assert.Catch<Exception>(() => Load()); Assert.Catch<Exception>(() => { using var edit = new LocalizationProjectEdit(config, config.Sets[0].Id); });
            Assert.That(ProjectCoverageAnalyzer.Analyze(config).IsComplete, Is.False);
            Assert.That(File.ReadAllText(path), Is.EqualTo(json));
            Write("ru", "broken", "bad"); Assert.That(Value(Load(), Id("bad"), "ru"), Is.EqualTo("ru value"));
        }
        [Test] public void ExcessiveJsonDepthIsRejectedBeforeRecursiveRuntimeParsing()
        {
            string path = Write("en", "main", "key");
            File.WriteAllText(path, "{\"translations\":{},\"custom\":" + new string('[', 70) + "0" + new string(']', 70) + "}");
            Assert.Catch<Exception>(() => Load());
            Assert.Catch<Exception>(() => { using var edit = new LocalizationProjectEdit(config, config.Sets[0].Id); });
            Assert.That(ProjectCoverageAnalyzer.Analyze(config).IsComplete, Is.False);
        }
        [Test] public void NestedFilesRoundTripThroughCsvCloneAndRename()
        {
            string path = Write("en", "chapter/main", "old"); Load(); string entry = Id("old");
            string csv = ProjectCsv.Export(config, includeSources: true);
            using (var plan = ProjectCsv.Preview(config, csv)) Assert.That(plan.Apply().HasChanges, Is.False);
            using (var edit = new LocalizationProjectEdit(config, config.Sets[0].Id)) { edit.RenameTextEntry(entry, "new"); edit.Commit(); }
            var json = JObject.Parse(File.ReadAllText(path));
            Assert.That((string)json["translations"]["new"], Is.EqualTo("en value")); Assert.That(json["translations"]["old"], Is.Null);
        }
        [Test] public void ExistingUppercaseExtensionIsSavedWithoutCreatingAnotherFile()
        {
            string path = Write("en", "main", "key"), upper = Path.ChangeExtension(path, ".JSON");
            File.Move(path, upper); Load(); string guid = AssetDatabase.AssetPathToGUID(upper);
            Set("key", "en", "edited");
            Assert.That((string)JObject.Parse(File.ReadAllText(upper))["translations"]["key"], Is.EqualTo("edited"));
            Assert.That(Directory.GetFiles(Path.GetDirectoryName(upper)).Where(p => !p.EndsWith(".meta")).ToArray(), Has.Length.EqualTo(1));
            Assert.That(AssetDatabase.AssetPathToGUID(upper), Is.EqualTo(guid));
        }
        [Test] public void WindowSelectionIsSharedWithInspectorWithoutChangingStartup()
        {
            Write("en", "main", "selected.only"); Load();
            var keys = new[] { "SimplyLocalize.ProjectEditor.Config", "SimplyLocalize.ProjectEditor.Channel", "SimplyLocalize.ProjectEditor.Set" };
            var previous = keys.Select(k => SessionState.GetString(k, "")).ToArray();
            var window = ScriptableObject.CreateInstance<LocalizationEditorWindow>();
            try
            {
                Assert.That(window.TrySelectConfig(config), Is.True); Assert.That(EditorProjectSelection.Config, Is.SameAs(config));
                Assert.That(Value(EditorProjectSelection.Current.Text, Id("selected.only"), "en"), Is.EqualTo("en value"));
                Assert.That(config.AutoInitialize, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(window);
                for (int i = 0; i < keys.Length; i++) SessionState.SetString(keys[i], previous[i]);
                EditorProjectSelection.Invalidate();
            }
        }
        [Test] public void CoverageReadsExternalDeletionWithoutResurrectingOldImportedValue()
        {
            string path = Write("en", "main", "current"); var before = Load();
            File.WriteAllText(path, "{\"translations\":{}}");
            var report = ProjectCoverageAnalyzer.Analyze(config);
            Assert.That(report.TextCoverage.Single(c => c.LanguageCode == "en").TranslatedCount, Is.Zero);
            Assert.That(Value(before, Id("current"), "en"), Is.EqualTo("en value"));
        }
    }
}
