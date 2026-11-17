using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using SimplyLocalize.Editor.Data;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace SimplyLocalize.Tests
{
    public class LocalizationProjectEditTests
    {
        private string root, addressRoot, configPath, setId, entryId;
        private LocalizationProjectConfig config;
        [SetUp] public void Setup()
        {
            addressRoot = "SimplyLocalizeProjectEditTests_" + Guid.NewGuid().ToString("N"); root = "Assets/" + addressRoot;
            Directory.CreateDirectory(root + "/Resources/" + addressRoot);
            config = LocalizationProjectConfig.Create(new[] { "en", "ru" }); setId = config.Sets[0].Id;
            entryId = config.Sets[0].AddEntry("hello").Id;
            config.Languages[0].Appearance = new("resources", "Appearance/NotLoadedByTextEditing", LocalizationSourceKind.Appearance);
            foreach (string code in new[] { "en", "ru" })
            {
                Write(code, "{\"translations\":{\"hello\":\"" + code + "\",\"unregistered\":\"Keep\"},\"extension\":{\"enabled\":true,\"date\":\"2026-01-01\"}}");
                config.Sets[0].SetSources(code, Source(code));
            }
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            configPath = root + "/Project.asset"; AssetDatabase.CreateAsset(config, configPath); SaveConfig();
        }
        [TearDown] public void Cleanup() { if (root != null) AssetDatabase.DeleteAsset(root); }
        private string PathFor(string name) => root + "/Resources/" + addressRoot + "/" + name + ".json";
        private void Write(string name, string value) => File.WriteAllText(PathFor(name), value, new UTF8Encoding(false));
        private LocalizationSourceReference Source(string name) => new("resources", addressRoot + "/" + name, LocalizationSourceKind.TextTable);
        private JObject Read(string name) => JObject.Parse(File.ReadAllText(PathFor(name)));
        private string Text(string name, string key) => Read(name)["translations"]?[key]?.Value<string>();
        private void SaveConfig() { EditorUtility.SetDirty(config); AssetDatabase.SaveAssetIfDirty(config); }
        private LocalizationContentSet AddTarget(string name = "Target")
        {
            var set = config.AddSet(name); config.MainChannel.IncludeSet(set.Id);
            foreach (string code in new[] { "en", "ru" }) { Write(name + code, "{\"translations\":{}}"); set.SetSources(code, Source(name + code)); }
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport); SaveConfig(); return set;
        }
        [Test] public void DraftIsIsolatedAndCommitPreservesUnknownDataAndConfigBytesForTextOnlyEdits()
        {
            byte[] configBefore = File.ReadAllBytes(configPath);
            using var edit = new LocalizationProjectEdit(config, setId);
            edit.SetTranslation(entryId, "ru", "Привет");
            Assert.That(Text("ru", "hello"), Is.EqualTo("ru"));
            Assert.That(edit.Draft.Languages[0].Appearance.Address, Is.EqualTo(config.Languages[0].Appearance.Address));
            var change = edit.Commit();
            Assert.That(change.IsApplied, Is.True); Assert.That(Text("ru", "hello"), Is.EqualTo("Привет"));
            Assert.That(Text("ru", "unregistered"), Is.EqualTo("Keep"));
            Assert.That(Read("ru")["extension"]["date"].Value<string>(), Is.EqualTo("2026-01-01"));
            Assert.That(File.ReadAllBytes(configPath), Is.EqualTo(configBefore));
            Assert.Throws<InvalidOperationException>(() => edit.Commit());
        }
        [Test] public void RenameKeepsIdentityAndUndoRedoRestoreBothFilesAndSavedConfig()
        {
            using var edit = new LocalizationProjectEdit(config, setId); edit.RenameTextEntry(entryId, "welcome");
            Assert.That(config.Sets[0].Entries[0].Key, Is.EqualTo("hello"));
            var change = edit.Commit();
            Assert.That(config.Sets[0].Entries[0].Id, Is.EqualTo(entryId)); Assert.That(Text("en", "welcome"), Is.EqualTo("en"));
            Assert.That(Text("en", "hello"), Is.Null); Assert.That(EditorUtility.IsDirty(config), Is.False);
            change.Undo(); Assert.That(config.Sets[0].Entries[0].Key, Is.EqualTo("hello")); Assert.That(Text("en", "hello"), Is.EqualTo("en"));
            change.Redo(); Assert.That(config.Sets[0].Entries[0].Key, Is.EqualTo("welcome")); Assert.That(Text("ru", "welcome"), Is.EqualTo("ru"));
            AssetDatabase.ImportAsset(configPath, ImportAssetOptions.ForceUpdate);
            var reloaded = AssetDatabase.LoadAssetAtPath<LocalizationProjectConfig>(configPath);
            Assert.That(reloaded.Sets[0].Entries[0].Id, Is.EqualTo(entryId)); Assert.That(reloaded.Sets[0].Entries[0].Key, Is.EqualTo("welcome"));
        }
        [Test] public void MoveTransfersEveryTranslationAndKeepsStableEntryRouting()
        {
            string targetId = AddTarget().Id;
            using var edit = new LocalizationProjectEdit(config, setId, targetId); edit.MoveTextEntry(entryId, targetId);
            var change = edit.Commit();
            Assert.That(config.Sets.Single(s => s.Id == setId).Entries, Is.Empty);
            Assert.That(config.Sets.Single(s => s.Id == targetId).Entries[0].Id, Is.EqualTo(entryId));
            Assert.That(Text("Targeten", "hello"), Is.EqualTo("en")); Assert.That(Text("Targetru", "hello"), Is.EqualTo("ru"));
            Assert.That(Text("en", "hello"), Is.Null); Assert.That(Text("en", "unregistered"), Is.EqualTo("Keep"));
            change.Undo(); Assert.That(Text("en", "hello"), Is.EqualTo("en")); Assert.That(Text("Targeten", "hello"), Is.Null);
        }
        [Test] public void RenameAndDeletePreserveAKeyStillUsedByAnotherSetSharingTheSource()
        {
            var shared = config.AddSet("Shared"); string otherId = shared.AddEntry("hello").Id;
            foreach (string code in new[] { "en", "ru" }) shared.SetSources(code, Source(code));
            config.MainChannel.IncludeSet(shared.Id); SaveConfig();
            using (var edit = new LocalizationProjectEdit(config, setId)) { edit.RenameTextEntry(entryId, "welcome"); edit.Commit(); }
            Assert.That(Text("en", "hello"), Is.EqualTo("en")); Assert.That(Text("en", "welcome"), Is.EqualTo("en"));
            using (var edit = new LocalizationProjectEdit(config, shared.Id)) { edit.RemoveTextEntry(otherId); edit.Commit(); }
            Assert.That(Text("en", "hello"), Is.Null); Assert.That(Text("en", "welcome"), Is.EqualTo("en"));
        }
        [Test] public void ExternalFileChangeRejectsWholeCommitWithoutTouchingOtherFilesOrConfig()
        {
            byte[] enBefore = File.ReadAllBytes(PathFor("en")), configBefore = File.ReadAllBytes(configPath);
            using var edit = new LocalizationProjectEdit(config, setId); edit.RenameTextEntry(entryId, "welcome");
            Write("ru", "{\"translations\":{\"hello\":\"External\"}}");
            Assert.Throws<InvalidOperationException>(() => edit.Commit());
            Assert.That(File.ReadAllBytes(PathFor("en")), Is.EqualTo(enBefore)); Assert.That(Text("ru", "hello"), Is.EqualTo("External"));
            Assert.That(File.ReadAllBytes(configPath), Is.EqualTo(configBefore)); Assert.That(config.Sets[0].Entries[0].Key, Is.EqualTo("hello"));
            Assert.That(edit.Draft.Sets[0].Entries[0].Key, Is.EqualTo("welcome"), "Failed save preserves the pending draft.");
        }
        [Test] public void ExternalConfigMemoryOrDiskChangeCannotBeOverwritten()
        {
            using var edit = new LocalizationProjectEdit(config, setId); edit.SetTranslation(entryId, "en", "Changed");
            config.MainChannel.Name = "Changed elsewhere";
            Assert.Throws<InvalidOperationException>(() => edit.Commit()); Assert.That(Text("en", "hello"), Is.EqualTo("en"));
            config.MainChannel.Name = "Main";
            File.AppendAllText(configPath, "\n# external edit\n");
            Assert.Throws<InvalidOperationException>(() => edit.Commit()); Assert.That(Text("en", "hello"), Is.EqualTo("en"));
        }
        [Test] public void ReadOnlySecondFileFailsBeforePublishingFirstFileOrConfig()
        {
            byte[] before = File.ReadAllBytes(PathFor("en"));
            using var edit = new LocalizationProjectEdit(config, setId); edit.RenameTextEntry(entryId, "welcome");
            string locked = PathFor("ru"); var flags = File.GetAttributes(locked); File.SetAttributes(locked, flags | FileAttributes.ReadOnly);
            try
            {
                Assert.Catch<Exception>(() => edit.Commit());
                Assert.That(File.ReadAllBytes(PathFor("en")), Is.EqualTo(before)); Assert.That(config.Sets[0].Entries[0].Key, Is.EqualTo("hello"));
            }
            finally { File.SetAttributes(locked, flags); }
        }
        [Test] public void UndoAndRedoRefuseExternalChangesWithoutReplacingThem()
        {
            using var edit = new LocalizationProjectEdit(config, setId); edit.SetTranslation(entryId, "en", "Changed"); var change = edit.Commit();
            byte[] saved = File.ReadAllBytes(PathFor("en")); Write("en", "{\"translations\":{\"hello\":\"External\"}}");
            Assert.Throws<InvalidOperationException>(() => change.Undo()); Assert.That(change.IsApplied, Is.True); Assert.That(Text("en", "hello"), Is.EqualTo("External"));
            File.WriteAllBytes(PathFor("en"), saved); change.Undo();
            File.AppendAllText(configPath, "\n# external after undo\n");
            Assert.Throws<InvalidOperationException>(() => change.Redo()); Assert.That(change.IsApplied, Is.False); Assert.That(Text("en", "hello"), Is.EqualTo("en"));
        }
        [Test] public void MoveCollisionLeavesDraftAndFilesUnchanged()
        {
            string target = AddTarget().Id; Write("Targetru", "{\"translations\":{\"hello\":\"Existing\"}}");
            using var edit = new LocalizationProjectEdit(config, setId, target);
            Assert.Throws<InvalidOperationException>(() => edit.MoveTextEntry(entryId, target));
            Assert.That(edit.Draft.Sets.Single(s => s.Id == setId).Entries[0].Id, Is.EqualTo(entryId));
            edit.Commit(); Assert.That(Text("en", "hello"), Is.EqualTo("en")); Assert.That(Text("Targeten", "hello"), Is.Null);
        }
        [Test] public void MissingValueWithSeveralSourcesRequiresExplicitDestination()
        {
            Write("extra", "{\"translations\":{}}"); config.Sets[0].SetSources("en", Source("en"), Source("extra"));
            string added = config.Sets[0].AddEntry("added").Id; AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport); SaveConfig();
            using var edit = new LocalizationProjectEdit(config, setId);
            Assert.Throws<InvalidOperationException>(() => edit.SetTranslation(added, "en", "New"));
            edit.SetTranslation(added, "en", "New", addressRoot + "/extra"); edit.Commit();
            Assert.That(Text("extra", "added"), Is.EqualTo("New")); Assert.That(Text("en", "added"), Is.Null);
        }
        [Test] public void SourceAmbiguityIntroducedAfterOpenPreventsWritingOldLocation()
        {
            using var edit = new LocalizationProjectEdit(config, setId); edit.SetTranslation(entryId, "en", "Changed");
            string duplicate = root + "/Duplicate/Resources/" + addressRoot + "/en.json";
            Directory.CreateDirectory(Path.GetDirectoryName(duplicate)); File.Copy(PathFor("en"), duplicate);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Assert.Throws<InvalidOperationException>(() => edit.Commit()); Assert.That(Text("en", "hello"), Is.EqualTo("en"));
        }
        [Test] public void NoOpKeepsExactBytesAndEditedDocumentKeepsEncodingAndExtensionFields()
        {
            var encoding = new UnicodeEncoding(false, true);
            File.WriteAllText(PathFor("en"), "{\r\n  \"translations\": {\"hello\":\"en\"},\r\n  \"extra\": [1, true, \"2026-01-01\"]\r\n}", encoding);
            byte[] before = File.ReadAllBytes(PathFor("en"));
            using (var edit = new LocalizationProjectEdit(config, setId)) edit.Commit();
            Assert.That(File.ReadAllBytes(PathFor("en")), Is.EqualTo(before));
            using (var edit = new LocalizationProjectEdit(config, setId)) { edit.SetTranslation(entryId, "en", "Changed"); edit.Commit(); }
            Assert.That(File.ReadAllBytes(PathFor("en")).Take(2), Is.EqualTo(encoding.GetPreamble()));
            Assert.That(File.ReadAllText(PathFor("en")), Does.Contain("\r\n")); Assert.That(Read("en")["extra"].Count(), Is.EqualTo(3));
        }
        [Test] public void MovingDifferentLanguagesIntoOneSharedDestinationRejectsLostTranslations()
        {
            string target = AddTarget().Id; var targetSet = config.Sets.Single(s => s.Id == target);
            targetSet.SetSources("ru", Source("Targeten")); SaveConfig();
            using var edit = new LocalizationProjectEdit(config, setId, target);
            Assert.Throws<InvalidOperationException>(() => edit.MoveTextEntry(entryId, target));
            Assert.That(edit.Draft.Sets[0].Entries[0].Id, Is.EqualTo(entryId)); Assert.That(Text("Targeten", "hello"), Is.Null);
        }
        [Test] public void NativeUnityUndoAndRedoRestoreFilesAndIdentityTogether()
        {
            using var edit = new LocalizationProjectEdit(config, setId); edit.RenameTextEntry(entryId, "welcome");
            using var record = LocalizationProjectUndo.Record(edit.Commit(), "Rename localization entry");
            Undo.PerformUndo(); Assert.That(config.Sets[0].Entries[0].Key, Is.EqualTo("hello")); Assert.That(Text("ru", "hello"), Is.EqualTo("ru"));
            Undo.PerformRedo(); Assert.That(config.Sets[0].Entries[0].Key, Is.EqualTo("welcome")); Assert.That(Text("ru", "welcome"), Is.EqualTo("ru"));
            Assert.That(config.Sets[0].Entries[0].Id, Is.EqualTo(entryId));
        }
        [Test] public void ChangingSourceRoutingCannotRedirectAPendingTextEditSilently()
        {
            using var edit = new LocalizationProjectEdit(config, setId); edit.SetTranslation(entryId, "en", "Changed");
            edit.Draft.Sets[0].SetSources("en", Source("ru"));
            Assert.Throws<InvalidOperationException>(() => edit.Commit());
            Assert.That(Text("en", "hello"), Is.EqualTo("en"));
            Assert.That(config.Sets[0].Sources.Single(s => s.Language == "en").Sources[0].Address, Is.EqualTo(addressRoot + "/en"));
        }
        [Test] public void NativeUndoUsesIndependentTransactionsInCorrectOrder()
        {
            using var first = new LocalizationProjectEdit(config, setId); first.SetTranslation(entryId, "en", "First");
            using var firstRecord = LocalizationProjectUndo.Record(first.Commit(), "First translation");
            using var second = new LocalizationProjectEdit(config, setId); second.SetTranslation(entryId, "en", "Second");
            using var secondRecord = LocalizationProjectUndo.Record(second.Commit(), "Second translation");
            Undo.PerformUndo(); Assert.That(Text("en", "hello"), Is.EqualTo("First"));
            Undo.PerformUndo(); Assert.That(Text("en", "hello"), Is.EqualTo("en"));
            Undo.PerformRedo(); Assert.That(Text("en", "hello"), Is.EqualTo("First"));
            Undo.PerformRedo(); Assert.That(Text("en", "hello"), Is.EqualTo("Second"));
        }
        [Test] public void NativeUndoRefusesExternalFileAndKeepsConfigAndOtherLanguages()
        {
            using var edit = new LocalizationProjectEdit(config, setId); edit.RenameTextEntry(entryId, "welcome");
            using var record = LocalizationProjectUndo.Record(edit.Commit(), "Rename localization entry");
            Write("ru", "{\"translations\":{\"welcome\":\"External\"}}");
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("Undo/Redo stopped:.*File changed"));
            Undo.PerformUndo();
            Assert.That(config.Sets[0].Entries[0].Key, Is.EqualTo("welcome")); Assert.That(Text("en", "welcome"), Is.EqualTo("en"));
            Assert.That(Text("ru", "welcome"), Is.EqualTo("External"));
        }
        [Test] public void UndoDoesNotOwnUnchangedSourceFiles()
        {
            using var edit = new LocalizationProjectEdit(config, setId); edit.SetTranslation(entryId, "en", "Changed"); var change = edit.Commit();
            Write("ru", "{\"translations\":{\"hello\":\"External\"}}"); change.Undo();
            Assert.That(Text("en", "hello"), Is.EqualTo("en")); Assert.That(Text("ru", "hello"), Is.EqualTo("External"));
            change.Redo(); Assert.That(Text("en", "hello"), Is.EqualTo("Changed")); Assert.That(Text("ru", "hello"), Is.EqualTo("External"));
        }
        [Test] public void NativeHistoryRejectsDuplicateRegistrationOfTheSameTransaction()
        {
            using var edit = new LocalizationProjectEdit(config, setId); edit.SetTranslation(entryId, "en", "Changed"); var change = edit.Commit();
            using var record = LocalizationProjectUndo.Record(change, "Translation");
            Assert.Throws<InvalidOperationException>(() => LocalizationProjectUndo.Record(change, "Duplicate"));
            Undo.PerformUndo(); Assert.That(Text("en", "hello"), Is.EqualTo("en"));
            Undo.PerformRedo(); Assert.That(Text("en", "hello"), Is.EqualTo("Changed"));
        }
    }
}
