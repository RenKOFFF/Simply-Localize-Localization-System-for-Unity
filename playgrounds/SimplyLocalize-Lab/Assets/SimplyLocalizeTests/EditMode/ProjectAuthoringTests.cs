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
    }
}
