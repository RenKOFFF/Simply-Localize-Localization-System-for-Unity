using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using SimplyLocalize.Editor;
using SimplyLocalize.Editor.Data;
using UnityEditor;
using UnityEngine;

namespace SimplyLocalize.Tests
{
    public class ProjectAuthoringTests
    {
        private string root;
        [SetUp] public void Setup()
        {
            root = "Assets/SimplyLocalizeAuthoringTests_" + Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(root); AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }
        [TearDown] public void Cleanup() => AssetDatabase.DeleteAsset(root);
        private LocalizationProjectConfig Create() => LocalizationProjectAuthoring.Create(new[] { "en", "ru" }, "en", "en", root + "/Project.asset", false);
        private string SourcePath(string language) => root + "/Resources/SimplyLocalize/Translations/" + language + "/Common.json";

        [TestCase("Resources/CON.asset")][TestCase("Resources/AUX.asset")][TestCase("Resources/LPT1.asset")]
        [TestCase("Resources/COM¹.asset")][TestCase("Resources/LPT²/Appearance.asset")]
        [TestCase("Resources/Folder./Appearance.asset")][TestCase("Resources/Folder /Appearance.asset")]
        [TestCase("Resources/../Appearance.asset")][TestCase("Resources//Appearance.asset")][TestCase("Resources/a\\b.asset")]
        [TestCase("Resources/.hidden/Appearance.asset")][TestCase("Resources/Folder~/Appearance.asset")]
        [TestCase("Resources/cvs/Appearance.asset")][TestCase("Resources/CVS/Appearance.asset")][TestCase("Resources/.Appearance.asset")]
        public void InvalidNewAssetPathsFailBeforeNativeCreationAndLeaveNoDirectories(string relative)
        {
            var config = Create(); string path = root + "/" + relative;
            var files = Directory.GetFiles(root, "*", SearchOption.AllDirectories).ToDictionary(p => p, File.ReadAllBytes);
            var folders = Directory.GetDirectories(root, "*", SearchOption.AllDirectories); string json = EditorJsonUtility.ToJson(config);
            Assert.Throws<FormatException>(() => LocalizationProjectAuthoring.Create(new[] { "en" }, "en", null, path, false));
            using (var edit = new LocalizationProjectAssetEdit(config)) Assert.Throws<FormatException>(() => edit.Create<LocalizationAppearance>(path));
            Assert.Catch<Exception>(() => LocalizationProjectAuthoring.CreateAppearance(config, "en", path));
            Assert.Catch<Exception>(() => LocalizationProjectAuthoring.CreateAssetTable(config, config.Sets[0].Id, "en", path));
            Assert.That(Directory.GetFiles(root, "*", SearchOption.AllDirectories), Is.EquivalentTo(files.Keys));
            foreach (var file in files) Assert.That(File.ReadAllBytes(file.Key), Is.EqualTo(file.Value));
            Assert.That(Directory.GetDirectories(root, "*", SearchOption.AllDirectories), Is.EquivalentTo(folders));
            Assert.That(EditorJsonUtility.ToJson(config), Is.EqualTo(json)); Assert.That(EditorUtility.IsDirty(config), Is.False);
        }
        [Test] public void HiddenParentIsRejectedBeforeNativeCreation()
        {
            string folder = root + "/Hidden"; Directory.CreateDirectory(folder);
            var attributes = File.GetAttributes(folder); File.SetAttributes(folder, attributes | FileAttributes.Hidden);
            try
            {
                Assert.Throws<FormatException>(() => LocalizationProjectAuthoring.Create(new[] { "en" }, "en", null, folder + "/Project.asset", false));
                Assert.That(Directory.GetFiles(folder, "*", SearchOption.AllDirectories), Is.Empty);
            }
            finally { File.SetAttributes(folder, attributes); }
        }
        [TestCase("con")][TestCase("aux")][TestCase("prn")][TestCase("nul")]
        public void StandardLanguageCodeCanUsePortableStorageWithoutChangingIdentity(string code)
        {
            var config = LocalizationProjectAuthoring.Create(new[] { "en", code }, code, "en", root + "/Project.asset", false);
            Assert.That(config.MainChannel.DefaultLanguage, Is.EqualTo(code)); Assert.That(config.Languages.Last().Code, Is.EqualTo(code));
            string address = config.Sets[0].Sources.Single(s => s.Language == code).Sources[0].Address;
            Assert.That(address, Is.EqualTo("SimplyLocalize/Translations/lang-" + code + "/Common"));
            Assert.That(File.Exists(root + "/Resources/" + address + ".json"), Is.True);
            string entry;
            using (var edit = new LocalizationProjectEdit(config, config.Sets[0].Id))
            { entry = edit.Draft.Sets[0].AddEntry("hello").Id; edit.SetTranslation(entry, code, "Hello"); edit.Commit(); }
            Assert.That(EditorProjectTextData.Load(config).TryGetDirect(entry, code, out var cell), Is.True); Assert.That(cell.Value, Is.EqualTo("Hello"));
            LocalizationProjectAuthoring.CreateAppearance(config, code);
            Assert.That(config.Languages.Last().Appearance.Address, Is.EqualTo("SimplyLocalize/Appearance/lang-" + code));
            LocalizationProjectAuthoring.CreateAssetTable(config, config.Sets[0].Id, code);
            Assert.That(config.Sets[0].Sources.Single(s => s.Language == code).Sources.Any(s => s.Kind == LocalizationSourceKind.AssetTable
                && s.Address.Contains("/lang-" + code + "/")), Is.True);
        }
        [Test] public void AddingReservedCodeAndCsvImportUsePortableSourceAddresses()
        {
            var config = Create(); LocalizationProjectAuthoring.AddLanguage(config, "con");
            Assert.That(File.Exists(SourcePath("lang-con")), Is.True);
            using var plan = ProjectCsv.Preview(config, "Key,aux\nhello,Hello"); plan.Apply();
            Assert.That(config.Languages.Any(l => l.Code == "aux"), Is.True); Assert.That(File.Exists(SourcePath("lang-aux")), Is.True);
            string entry = config.Sets[0].Entries.Single().Id;
            Assert.That(EditorProjectTextData.Load(config).TryGetDirect(entry, "aux", out var cell), Is.True); Assert.That(cell.Value, Is.EqualTo("Hello"));
        }
        [TestCase("CON", "Translations-CON")][TestCase("AUX", "Translations-AUX")]
        [TestCase("COM³", "Translations-COM³")]
        [TestCase("Menu:Main", "Menu_Main")][TestCase("Folder/Main", "Folder_Main")][TestCase("...", "Translations")]
        [TestCase("CVS", "Translations-CVS")][TestCase(".Menu", "Menu")][TestCase("Menu~", "Menu")]
        public void AutomaticCsvSourceNamesArePortableAndDoNotRenameTheSet(string setName, string stem)
        {
            var config = Create(); config.Sets[0].Name = setName; EditorUtility.SetDirty(config); AssetDatabase.SaveAssetIfDirty(config);
            using var plan = ProjectCsv.Preview(config, "Key,pl\nhello,Cześć"); plan.Apply();
            Assert.That(config.Sets[0].Name, Is.EqualTo(setName)); Assert.That(File.Exists(root + "/Resources/SimplyLocalize/Translations/pl/" + stem + ".json"), Is.True);
        }

        [Test] public void CreationBuildsOneLightweightConfigAndExactSourcesWithoutProfiles()
        {
            var config = Create(); config.ValidateOrThrow();
            Assert.That(config.Channels.Count, Is.EqualTo(1)); Assert.That(config.Sets.Count, Is.EqualTo(1));
            Assert.That(config.Languages.Select(l => l.Code), Is.EqualTo(new[] { "en", "ru" }));
            Assert.That(config.Languages.All(l => l.Appearance == null), Is.True);
            Assert.That(config.Sets[0].Sources.Single(s => s.Language == "ru").Sources[0].Address, Is.EqualTo("SimplyLocalize/Translations/ru/Common"));
            Assert.That(File.Exists(SourcePath("ru")), Is.True); Assert.That(AssetDatabase.FindAssets("t:LanguageProfile", new[] { root }), Is.Empty);
            Assert.That(EditorUtility.IsDirty(config), Is.False);
        }
        [Test] public void AddingLanguageKeepsIdsAndIndependentChannelsAndUndoRedoKeepFileGuid()
        {
            var config = Create(); string setId = config.Sets[0].Id;
            string voiceId = config.AddChannel("Voice", new[] { "en" }).Id; EditorUtility.SetDirty(config); AssetDatabase.SaveAssetIfDirty(config);
            byte[] en = File.ReadAllBytes(SourcePath("en"));
            var change = LocalizationProjectAuthoring.AddLanguage(config, "pl");
            using var record = LocalizationProjectUndo.Record(change, "Add language");
            string guid = AssetDatabase.AssetPathToGUID(SourcePath("pl"));
            Assert.That(guid, Is.Not.Empty); Assert.That(config.Sets[0].Id, Is.EqualTo(setId));
            Assert.That(config.Channels.Single(c => c.Id == voiceId).Languages, Is.EqualTo(new[] { "en" }));
            Assert.That(config.MainChannel.Languages, Does.Contain("pl")); Assert.That(File.ReadAllBytes(SourcePath("en")), Is.EqualTo(en));
            Undo.PerformUndo(); Assert.That(config.Languages.Select(l => l.Code), Does.Not.Contain("pl"));
            Assert.That(File.Exists(SourcePath("pl")), Is.False); Assert.That(File.Exists(SourcePath("pl") + ".meta"), Is.False);
            Undo.PerformRedo(); Assert.That(config.MainChannel.Languages, Does.Contain("pl"));
            Assert.That(AssetDatabase.AssetPathToGUID(SourcePath("pl")), Is.EqualTo(guid));
        }
        [Test] public void NewSourceAndItsFirstTranslationCommitTogether()
        {
            var config = Create(); string setId = config.Sets[0].Id;
            using var edit = new LocalizationProjectEdit(config, setId);
            var language = edit.Draft.AddLanguage("pl"); edit.Draft.MainChannel.SetLanguages(new[] { "en", "ru", "pl" });
            string entryId = edit.Draft.Sets[0].AddEntry("play").Id;
            edit.CreateTextSource(setId, language.Code, SourcePath("pl")); edit.SetTranslation(entryId, "pl", "Graj");
            Assert.That(File.Exists(SourcePath("pl")), Is.False);
            edit.Commit();
            var data = EditorProjectTextData.Load(config); Assert.That(data.TryResolve(entryId, "pl", out var value), Is.True); Assert.That(value.Value, Is.EqualTo("Graj"));
        }
        [Test] public void ForeignFileAppearingAfterStagingIsPreservedAndConfigDoesNotChange()
        {
            var config = Create(); using var edit = new LocalizationProjectEdit(config, config.Sets[0].Id);
            edit.Draft.AddLanguage("pl"); edit.CreateTextSource(config.Sets[0].Id, "pl", SourcePath("pl"));
            Directory.CreateDirectory(Path.GetDirectoryName(SourcePath("pl"))); File.WriteAllText(SourcePath("pl"), "Foreign");
            Assert.Throws<InvalidOperationException>(() => edit.Commit());
            Assert.That(File.ReadAllText(SourcePath("pl")), Is.EqualTo("Foreign")); Assert.That(config.Languages.Select(l => l.Code), Does.Not.Contain("pl"));
        }
        [Test] public void AddLanguageFailureLeavesNoNewSourceOrMetadata()
        {
            var config = Create(); string path = AssetDatabase.GetAssetPath(config); var attributes = File.GetAttributes(path);
            File.SetAttributes(path, attributes | FileAttributes.ReadOnly);
            try { Assert.Catch<Exception>(() => LocalizationProjectAuthoring.AddLanguage(config, "pl")); }
            finally { File.SetAttributes(path, attributes); }
            Assert.That(File.Exists(SourcePath("pl")), Is.False); Assert.That(File.Exists(SourcePath("pl") + ".meta"), Is.False);
            Assert.That(config.Languages.Select(l => l.Code), Does.Not.Contain("pl"));
        }
        [Test] public void ExistingDataIsNotAdoptedOrOverwrittenBySetup()
        {
            string file = SourcePath("en"); Directory.CreateDirectory(Path.GetDirectoryName(file)); File.WriteAllText(file, "Existing content");
            Assert.Throws<IOException>(() => Create());
            Assert.That(File.ReadAllText(file), Is.EqualTo("Existing content")); Assert.That(File.Exists(root + "/Project.asset"), Is.False);
        }
        [Test] public void InvalidSelectionCreatesNothing()
        {
            Assert.Throws<ArgumentException>(() => LocalizationProjectAuthoring.Create(new[] { "en" }, "ru", null, root + "/Project.asset", false));
            Assert.That(Directory.GetFiles(root, "*", SearchOption.AllDirectories), Is.Empty);
        }
        [Test] public void ExternalBackendLanguageDefinitionDoesNotInvokeAHiddenResourcesWriter()
        {
            var config = Create(); config.Sets[0].SetSources("en", new LocalizationSourceReference("custom", "translations", LocalizationSourceKind.TextTable));
            EditorUtility.SetDirty(config); AssetDatabase.SaveAssetIfDirty(config);
            LocalizationProjectAuthoring.AddLanguage(config, "pl");
            Assert.That(config.MainChannel.Languages, Does.Contain("pl")); Assert.That(File.Exists(SourcePath("pl")), Is.False);
            Assert.That(config.Sets[0].Sources.Any(s => s.Language == "pl"), Is.False);
        }
        [Test] public void CreatingAssetTableKeepsTextSourcesAndUndoRedoPreserveNewTableGuid()
        {
            var config = Create(); string set = config.Sets[0].Id; byte[] text = File.ReadAllBytes(SourcePath("en"));
            using var record = LocalizationProjectUndo.Record(LocalizationProjectAuthoring.CreateAssetTable(config, set, "en"), "Create asset table");
            var sources = config.Sets[0].Sources.Single(s => s.Language == "en").Sources;
            Assert.That(sources.Count, Is.EqualTo(2)); var source = sources.Single(s => s.Kind == LocalizationSourceKind.AssetTable);
            string path = new ResourcesEditorTextReader().ResolveSourceAssetPath(source), guid = AssetDatabase.AssetPathToGUID(path);
            Assert.That(path, Does.EndWith("/SimplyLocalize/Assets/en/CommonAssets.asset")); Assert.That(File.ReadAllBytes(SourcePath("en")), Is.EqualTo(text));
            Undo.PerformUndo(); Assert.That(File.Exists(path), Is.False); Assert.That(config.Sets[0].Sources.Single(s => s.Language == "en").Sources.Count, Is.EqualTo(1));
            Undo.PerformRedo(); Assert.That(AssetDatabase.AssetPathToGUID(path), Is.EqualTo(guid));
        }
        [Test] public void NewAppearanceUsesAnotherFileInsteadOfOverwritingDisconnectedData()
        {
            var config = Create(); LocalizationProjectAuthoring.CreateAppearance(config, "en");
            string path = new ResourcesEditorTextReader().ResolveSourceAssetPath(config.Languages[0].Appearance); byte[] bytes = File.ReadAllBytes(path);
            LocalizationProjectAuthoring.CreateAppearance(config, "en");
            string next = new ResourcesEditorTextReader().ResolveSourceAssetPath(config.Languages[0].Appearance);
            Assert.That(next, Is.Not.EqualTo(path)); Assert.That(File.ReadAllBytes(path), Is.EqualTo(bytes)); Assert.That(File.Exists(next), Is.True);
        }
    }
}
