using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using SimplyLocalize.Editor;
using SimplyLocalize.Editor.Data;
using UnityEditor;

namespace SimplyLocalize.Tests
{
    internal sealed class ProjectNotesTestScope : IDisposable
    {
        private readonly string path;
        private readonly List<string> createdFolders = new();
        internal ProjectNotesTestScope(LocalizationProjectConfig config)
        {
            path = LocalizationProjectNotes.GetAssetPath(config);
            Assert.That(File.Exists(path), Is.False, "A fresh test config must not own pre-existing translator notes.");
            for (string folder = Path.GetDirectoryName(path).Replace('\\', '/'); folder != "Assets"; folder = Path.GetDirectoryName(folder).Replace('\\', '/'))
                if (!Directory.Exists(folder)) createdFolders.Add(folder);
        }
        public void Dispose()
        {
            if (File.Exists(path)) AssetDatabase.DeleteAsset(path);
            foreach (string folder in createdFolders)
                if (Directory.Exists(folder) && Directory.GetFileSystemEntries(folder).Length == 0) AssetDatabase.DeleteAsset(folder);
        }
    }
    public class ProjectNotesTests
    {
        private string root, entry, source, notesPath;
        private LocalizationProjectConfig config;
        private ProjectNotesTestScope cleanup;
        [SetUp] public void Setup()
        {
            root = "Assets/SimplyLocalizeNotes_" + Guid.NewGuid().ToString("N"); Directory.CreateDirectory(root); AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            config = LocalizationProjectAuthoring.Create(new[] { "en" }, "en", "en", root + "/Project.asset", false);
            entry = config.Sets[0].AddEntry("hello").Id; EditorUtility.SetDirty(config); AssetDatabase.SaveAssetIfDirty(config);
            source = root + "/Resources/SimplyLocalize/Translations/en/Common.json"; notesPath = LocalizationProjectNotes.GetAssetPath(config); cleanup = new ProjectNotesTestScope(config);
        }
        [TearDown] public void Cleanup() { cleanup?.Dispose(); AssetDatabase.DeleteAsset(root); }
        [Test] public void NotesAndTranslationsCommitTogetherAndNativeUndoRestoresFilesAndGuids()
        {
            using var edit = new LocalizationProjectEdit(config, config.Sets[0].Id); edit.SetDescription(entry, "Greeting shown on the title screen"); edit.SetTranslation(entry, "en", "Hello");
            Assert.That(File.Exists(notesPath), Is.False); using var undo = LocalizationProjectUndo.Record(edit.Commit(), "Translator note");
            string guid = AssetDatabase.AssetPathToGUID(notesPath); Assert.That(guid, Is.Not.Empty);
            Assert.That(LocalizationProjectNotes.Load(config)[entry], Does.Contain("title screen")); Assert.That(ResourcesEditorTextReader.ResourceAddress(notesPath), Is.Null);
            Assert.That(AssetDatabase.GetDependencies(AssetDatabase.GetAssetPath(config), true), Does.Not.Contain(notesPath));
            Undo.PerformUndo(); Assert.That(File.Exists(notesPath), Is.False); Assert.That(File.ReadAllText(source), Does.Not.Contain("Hello"));
            Undo.PerformRedo(); Assert.That(AssetDatabase.AssetPathToGUID(notesPath), Is.EqualTo(guid)); Assert.That(LocalizationProjectNotes.Load(config)[entry], Does.Contain("title screen"));
        }
        [Test] public void NotesFollowIdentityAcrossEntryRenameMoveAndConfigAssetMove()
        {
            using (var edit = new LocalizationProjectEdit(config, config.Sets[0].Id)) { edit.SetDescription(entry, "Context"); edit.Commit(); }
            var other = config.AddSet("Other"); config.MainChannel.IncludeSet(other.Id); EditorUtility.SetDirty(config); AssetDatabase.SaveAssetIfDirty(config);
            using (var edit = new LocalizationProjectEdit(config, config.Sets[0].Id, other.Id)) { edit.RenameTextEntry(entry, "welcome"); edit.MoveTextEntry(entry, other.Id); edit.Commit(); }
            Assert.That(AssetDatabase.MoveAsset(AssetDatabase.GetAssetPath(config), root + "/Renamed.asset"), Is.Empty);
            Assert.That(LocalizationProjectNotes.GetAssetPath(config), Is.EqualTo(notesPath)); Assert.That(LocalizationProjectNotes.Load(config)[entry], Is.EqualTo("Context"));
        }
        [Test] public void ExternalNotesEditPreventsTranslationsFromBeingOverwritten()
        {
            using (var edit = new LocalizationProjectEdit(config, config.Sets[0].Id)) { edit.SetDescription(entry, "Original"); edit.Commit(); }
            using var pending = new LocalizationProjectEdit(config, config.Sets[0].Id); pending.SetDescription(entry, "Pending"); pending.SetTranslation(entry, "en", "Pending");
            File.WriteAllText(notesPath, "{\"descriptions\":{\"" + entry + "\":\"External\"}}"); byte[] before = File.ReadAllBytes(source);
            Assert.Throws<InvalidOperationException>(() => pending.Commit()); Assert.That(LocalizationProjectNotes.Load(config)[entry], Is.EqualTo("External")); Assert.That(File.ReadAllBytes(source), Is.EqualTo(before));
        }
        [Test] public void ReadOnlyTranslationFailureCreatesNoNotesAndResetDiscardsPendingNote()
        {
            using var edit = new LocalizationProjectEdit(config, config.Sets[0].Id); edit.SetDescription(entry, "Unwanted"); edit.SetTranslation(entry, "en", "Pending");
            var attributes = File.GetAttributes(source); File.SetAttributes(source, attributes | FileAttributes.ReadOnly);
            try { Assert.Catch<Exception>(() => edit.Commit()); } finally { File.SetAttributes(source, attributes); }
            Assert.That(File.Exists(notesPath), Is.False); edit.ResetChanges(); edit.SetTranslation(entry, "en", "Corrected"); edit.Commit(); Assert.That(File.Exists(notesPath), Is.False);
        }
        [Test] public void MalformedNotesAreReportedAndNotOverwritten()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(notesPath)); File.WriteAllText(notesPath, "{bad json}"); AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            using var edit = new LocalizationProjectEdit(config, config.Sets[0].Id);
            Assert.Catch<Exception>(() => edit.SetDescription(entry, "Replacement")); Assert.That(File.ReadAllText(notesPath), Is.EqualTo("{bad json}"));
        }
        [Test] public void NoOpKeepsExactNotesBytesAndDeletingEntryRemovesOnlyItsNote()
        {
            using (var edit = new LocalizationProjectEdit(config, config.Sets[0].Id)) { edit.SetDescription(entry, "Context"); edit.Commit(); }
            string foreign = Guid.NewGuid().ToString("N");
            File.WriteAllText(notesPath, "{ \"descriptions\":{\"" + entry + "\":\"Context\",\"" + foreign + "\":\"Keep\"},\"custom\":42 }"); byte[] before = File.ReadAllBytes(notesPath);
            using (var edit = new LocalizationProjectEdit(config, config.Sets[0].Id)) { edit.SetDescription(entry, "Context"); Assert.That(edit.Commit().HasChanges, Is.False); }
            Assert.That(File.ReadAllBytes(notesPath), Is.EqualTo(before));
            using (var edit = new LocalizationProjectEdit(config, config.Sets[0].Id)) { edit.RemoveTextEntry(entry); edit.Commit(); }
            Assert.That(LocalizationProjectNotes.Load(config).ContainsKey(entry), Is.False); Assert.That(LocalizationProjectNotes.Load(config)[foreign], Is.EqualTo("Keep"));
            Assert.That(File.ReadAllText(notesPath), Does.Contain("custom"));
        }
    }
}
