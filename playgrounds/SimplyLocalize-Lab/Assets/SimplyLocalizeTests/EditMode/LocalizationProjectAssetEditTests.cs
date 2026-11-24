using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using SimplyLocalize.Editor.Data;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SimplyLocalize.Tests
{
    public class LocalizationProjectAssetEditTests
    {
        private string root, prefix, tablePath, appearancePath, configPath, entry;
        private LocalizationProjectConfig config;
        private LocalizationAssetTable table;
        private LocalizationAppearance appearance;
        private Texture2D texture;
        [SetUp] public void Setup()
        {
            prefix = "SimplyLocalizeAssetEdit_" + Guid.NewGuid().ToString("N"); root = "Assets/" + prefix;
            Directory.CreateDirectory(root + "/Resources/" + prefix); AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            config = LocalizationProjectConfig.Create(new[] { "en", "ru" }); entry = config.Sets[0].AddEntry("image", LocalizationEntryKind.Asset).Id;
            config.Sets[0].SetSources("en", new LocalizationSourceReference("resources", prefix + "/Table", LocalizationSourceKind.AssetTable));
            config.Languages[0].Appearance = new("resources", prefix + "/Appearance", LocalizationSourceKind.Appearance);
            tablePath = root + "/Resources/" + prefix + "/Table.asset"; appearancePath = root + "/Resources/" + prefix + "/Appearance.asset";
            table = ScriptableObject.CreateInstance<LocalizationAssetTable>(); table.Set("image", null); AssetDatabase.CreateAsset(table, tablePath);
            appearance = ScriptableObject.CreateInstance<LocalizationAppearance>(); AssetDatabase.CreateAsset(appearance, appearancePath);
            texture = new Texture2D(1, 1); AssetDatabase.CreateAsset(texture, root + "/Texture.asset");
            configPath = root + "/Project.asset"; AssetDatabase.CreateAsset(config, configPath); AssetDatabase.SaveAssets();
        }
        [TearDown] public void Cleanup() => AssetDatabase.DeleteAsset(root);

        [Test] public void IsolatedNativeDraftAndConfigCommitTogetherAndUndoRedoRetainIdentity()
        {
            byte[] before = File.ReadAllBytes(tablePath); string guid = AssetDatabase.AssetPathToGUID(tablePath);
            using var edit = new LocalizationProjectAssetEdit(config);
            var draft = edit.Edit(table); draft.RenameKey("image", "picture"); draft.Set("picture", texture);
            edit.Draft.Sets[0].RenameEntry(entry, "picture");
            Assert.That(table.HasKey("image"), Is.True); Assert.That(File.ReadAllBytes(tablePath), Is.EqualTo(before));
            using var record = LocalizationProjectUndo.Record(edit.Commit(), "Edit asset table");
            Assert.That(table.Get("picture") == texture, Is.True); Assert.That(config.Sets[0].Entries[0].Id, Is.EqualTo(entry));
            Assert.That(EditorUtility.IsDirty(table) || EditorUtility.IsDirty(config), Is.False);
            Undo.PerformUndo(); Assert.That(AssetDatabase.LoadAssetAtPath<LocalizationAssetTable>(tablePath).HasKey("image"), Is.True);
            Assert.That(config.Sets[0].Entries[0].Key, Is.EqualTo("image")); Assert.That(File.ReadAllBytes(tablePath), Is.EqualTo(before));
            Undo.PerformRedo(); Assert.That(AssetDatabase.LoadAssetAtPath<LocalizationAssetTable>(tablePath).Get("picture") == texture, Is.True);
            Assert.That(AssetDatabase.AssetPathToGUID(tablePath), Is.EqualTo(guid));
        }
        [Test] public void NewNativeAssetsCrossReferencesAndSourceRegistrationUndoRedoKeepGuids()
        {
            string newTablePath = root + "/New/Resources/" + prefix + "/Ru.asset", newAppearancePath = root + "/New/Resources/" + prefix + "/RuAppearance.asset";
            using var edit = new LocalizationProjectAssetEdit(config);
            var newTable = edit.Create<LocalizationAssetTable>(newTablePath); var newAppearance = edit.Create<LocalizationAppearance>(newAppearancePath);
            newAppearance.overrideTypography = true; newAppearance.fontSizeMultiplier = 1.5f;
            newTable.Set("image", texture); newTable.Set("appearance", newAppearance);
            edit.Draft.Sets[0].SetSources("ru", new LocalizationSourceReference("resources", prefix + "/Ru", LocalizationSourceKind.AssetTable));
            edit.Draft.Languages[1].Appearance = new("resources", prefix + "/RuAppearance", LocalizationSourceKind.Appearance);
            Assert.That(File.Exists(newTablePath), Is.False);
            using var record = LocalizationProjectUndo.Record(edit.Commit(), "Create localized assets");
            string tableGuid = AssetDatabase.AssetPathToGUID(newTablePath), appearanceGuid = AssetDatabase.AssetPathToGUID(newAppearancePath);
            Assert.That(tableGuid, Is.Not.Empty); Assert.That(appearanceGuid, Is.Not.Empty);
            var saved = AssetDatabase.LoadAssetAtPath<LocalizationAssetTable>(newTablePath);
            Assert.That(saved.Get<LocalizationAppearance>("appearance").fontSizeMultiplier, Is.EqualTo(1.5f));
            Assert.That(AssetDatabase.GetAssetPath(saved.Get("appearance")), Is.EqualTo(newAppearancePath));
            Undo.PerformUndo(); Assert.That(File.Exists(newTablePath) || File.Exists(newTablePath + ".meta"), Is.False);
            Assert.That(File.Exists(newAppearancePath) || File.Exists(newAppearancePath + ".meta"), Is.False); Assert.That(config.Languages[1].Appearance, Is.Null);
            Undo.PerformRedo(); Assert.That(AssetDatabase.AssetPathToGUID(newTablePath), Is.EqualTo(tableGuid)); Assert.That(AssetDatabase.AssetPathToGUID(newAppearancePath), Is.EqualTo(appearanceGuid));
            Assert.That(AssetDatabase.LoadAssetAtPath<LocalizationAssetTable>(newTablePath).Get<LocalizationAppearance>("appearance").fontSizeMultiplier, Is.EqualTo(1.5f));
        }
        [Test] public void ReadOnlyAssetFailsPreflightBeforeOtherAssetOrConfigWrites()
        {
            byte[] tableBytes = File.ReadAllBytes(tablePath), configBytes = File.ReadAllBytes(configPath);
            using var edit = new LocalizationProjectAssetEdit(config); edit.Edit(table).Set("image", texture); edit.Edit(appearance).fontSizeMultiplier = 1.5f; edit.Draft.AutoDetectLanguage = true;
            var attributes = File.GetAttributes(appearancePath); File.SetAttributes(appearancePath, attributes | FileAttributes.ReadOnly);
            try { Assert.Catch<Exception>(() => edit.Commit()); } finally { File.SetAttributes(appearancePath, attributes); }
            Assert.That(File.ReadAllBytes(tablePath), Is.EqualTo(tableBytes)); Assert.That(File.ReadAllBytes(configPath), Is.EqualTo(configBytes));
            Assert.That(table.Get("image"), Is.Null); Assert.That(appearance.fontSizeMultiplier, Is.EqualTo(1));
        }
        [Test] public void ExternalNativeFileChangesArePreservedAndNotAcceptedIntoDraft()
        {
            using var edit = new LocalizationProjectAssetEdit(config); edit.Edit(appearance).fontSizeMultiplier = 1.5f;
            File.AppendAllText(appearancePath, "\n# External edit\n"); byte[] external = File.ReadAllBytes(appearancePath);
            Assert.Throws<InvalidOperationException>(() => edit.Commit()); Assert.That(File.ReadAllBytes(appearancePath), Is.EqualTo(external));
            Assert.That(appearance.fontSizeMultiplier, Is.EqualTo(1));
        }
        [Test] public void UnsavedInspectorChangesAreNotOverwritten()
        {
            using var edit = new LocalizationProjectAssetEdit(config); appearance.fontSizeMultiplier = 1.25f; EditorUtility.SetDirty(appearance);
            Assert.Throws<InvalidOperationException>(() => edit.Edit(appearance)); Assert.That(appearance.fontSizeMultiplier, Is.EqualTo(1.25f)); Assert.That(EditorUtility.IsDirty(appearance), Is.True);
        }
        [Test] public void NoOpDoesNotReserializeNativeAssetsOrCreateUndoChanges()
        {
            byte[] before = File.ReadAllBytes(tablePath); var time = File.GetLastWriteTimeUtc(tablePath);
            using var edit = new LocalizationProjectAssetEdit(config); edit.Edit(table);
            Assert.That(edit.Commit().HasChanges, Is.False); Assert.That(File.ReadAllBytes(tablePath), Is.EqualTo(before)); Assert.That(File.GetLastWriteTimeUtc(tablePath), Is.EqualTo(time));
        }
        [Test] public void ResetDiscardsStagedAssetsAndRestoresReferencesWithoutAcceptingExternalChanges()
        {
            table.Set("image", texture); EditorUtility.SetDirty(table); AssetDatabase.SaveAssetIfDirty(table);
            using var edit = new LocalizationProjectAssetEdit(config);
            var draft = edit.Edit(table); draft.Set("image", appearance);
            string path = root + "/Discarded.asset"; var created = edit.Create<LocalizationAppearance>(path);
            edit.Draft.AutoDetectLanguage = !config.AutoDetectLanguage;
            Assert.That(edit.HasChanges, Is.True); edit.ResetChanges();
            Assert.That(edit.HasChanges, Is.False); Assert.That(draft.Get("image") == texture, Is.True);
            Assert.That(created == null, Is.True); Assert.That(File.Exists(path), Is.False);
            File.AppendAllText(tablePath, "\n# External edit after opening draft\n"); var bytes = File.ReadAllBytes(tablePath);
            edit.ResetChanges(); draft.Set("image", appearance);
            Assert.Throws<InvalidOperationException>(() => edit.Commit()); Assert.That(File.ReadAllBytes(tablePath), Is.EqualTo(bytes));
        }
        [Test] public void ResetAfterFailedSaveDoesNotPublishRevertedAssetValues()
        {
            using var edit = new LocalizationProjectAssetEdit(config); edit.SetAsset(entry, "en", texture);
            edit.Edit(appearance).fontSizeMultiplier = 1.5f;
            var attributes = File.GetAttributes(appearancePath); File.SetAttributes(appearancePath, attributes | FileAttributes.ReadOnly);
            try { Assert.Catch<Exception>(() => edit.Commit()); } finally { File.SetAttributes(appearancePath, attributes); }
            edit.ResetChanges(); edit.Edit(appearance).fontSizeMultiplier = 1.25f; edit.Commit();
            Assert.That(table.Get("image"), Is.Null); Assert.That(appearance.fontSizeMultiplier, Is.EqualTo(1.25f));
        }
        [Test] public void DuplicateResourcesAddressAndForeignFileAfterStagingArePreserved()
        {
            using var edit = new LocalizationProjectAssetEdit(config);
            Assert.Throws<IOException>(() => edit.Create<LocalizationAssetTable>(root + "/Other/Resources/" + prefix + "/Table.asset"));
            string staged = root + "/Resources/" + prefix + "/Staged.asset"; edit.Create<LocalizationAssetTable>(staged);
            Assert.Throws<IOException>(() => edit.Create<LocalizationAssetTable>(root + "/Other/Resources/" + prefix + "/Staged.asset"));
            string path = root + "/Foreign.asset"; edit.Create<LocalizationAppearance>(path);
            File.Copy(appearancePath, path); byte[] foreign = File.ReadAllBytes(path);
            Assert.Throws<InvalidOperationException>(() => edit.Commit()); Assert.That(File.ReadAllBytes(path), Is.EqualTo(foreign));
            Assert.That(File.Exists(staged), Is.False);
        }
        [Test] public void TransientObjectReferencesAreRejectedBeforeSerialization()
        {
            byte[] before = File.ReadAllBytes(tablePath); var transient = new Texture2D(1, 1);
            try
            {
                using var edit = new LocalizationProjectAssetEdit(config); edit.Edit(table).Set("image", transient);
                Assert.That(Assert.Throws<InvalidOperationException>(() => edit.Commit()).Message, Does.Contain("Save referenced assets"));
                Assert.That(File.ReadAllBytes(tablePath), Is.EqualTo(before));
            }
            finally { Object.DestroyImmediate(transient); }
        }
        private LocalizationAssetTable NewTable(string name, Object value = null, bool entryExists = false)
        {
            var result = ScriptableObject.CreateInstance<LocalizationAssetTable>(); if (entryExists) result.Set("image", value);
            AssetDatabase.CreateAsset(result, root + "/Resources/" + prefix + "/" + name + ".asset"); AssetDatabase.SaveAssetIfDirty(result); return result;
        }
        private LocalizationSourceReference Source(string name) => new("resources", prefix + "/" + name, LocalizationSourceKind.AssetTable);
        private void SaveConfig() { EditorUtility.SetDirty(config); AssetDatabase.SaveAssetIfDirty(config); }
        [Test] public void RenameSharedEntryKeepsOldKeyForOtherRegisteredConsumer()
        {
            table.Set("image", texture); EditorUtility.SetDirty(table); AssetDatabase.SaveAssetIfDirty(table);
            var other = config.AddSet("Shared"); other.AddEntry("image", LocalizationEntryKind.Asset); other.SetSources("en", Source("Table")); config.MainChannel.IncludeSet(other.Id); SaveConfig();
            using var edit = new LocalizationProjectAssetEdit(config); edit.RenameAssetEntry(entry, "picture");
            using var record = LocalizationProjectUndo.Record(edit.Commit(), "Rename shared asset entry");
            Assert.That(table.Get("picture") == texture && table.Get("image") == texture, Is.True); Assert.That(config.Sets[0].Entries[0].Id, Is.EqualTo(entry));
            Undo.PerformUndo(); Assert.That(table.HasKey("picture"), Is.False); Assert.That(table.Get("image") == texture, Is.True);
        }
        [Test] public void MoveEntryTransfersAllLanguagesAndKeepsItsIdentity()
        {
            table.Set("image", texture); EditorUtility.SetDirty(table); AssetDatabase.SaveAssetIfDirty(table);
            var ru = NewTable("Ru", appearance, true); config.Sets[0].SetSources("ru", Source("Ru"));
            var target = config.AddSet("Target"); string targetId = target.Id;
            var enTarget = NewTable("TargetEn"); var ruTarget = NewTable("TargetRu");
            target.SetSources("en", Source("TargetEn")); target.SetSources("ru", Source("TargetRu")); config.MainChannel.IncludeSet(targetId); SaveConfig();
            using var edit = new LocalizationProjectAssetEdit(config); edit.MoveAssetEntry(entry, targetId); var change = edit.Commit();
            Assert.That(config.Sets[0].Entries.Any(e => e.Id == entry), Is.False); Assert.That(config.Sets.Single(s => s.Id == targetId).Entries[0].Id, Is.EqualTo(entry));
            Assert.That(enTarget.Get("image") == texture, Is.True); Assert.That(ruTarget.Get("image") == appearance, Is.True);
            Assert.That(table.HasKey("image") || ru.HasKey("image"), Is.False);
            change.Undo(); Assert.That(config.Sets[0].Entries[0].Id, Is.EqualTo(entry)); Assert.That(ru.Get("image") == appearance, Is.True);
        }
        [Test] public void MoveRefusesForeignTargetKeyAndDifferentLanguagesSharingOneDestination()
        {
            var target = config.AddSet("Target"); var targetTable = NewTable("Target", texture, true); target.SetSources("en", Source("Target")); config.MainChannel.IncludeSet(target.Id); SaveConfig();
            using (var edit = new LocalizationProjectAssetEdit(config)) Assert.Throws<InvalidOperationException>(() => edit.MoveAssetEntry(entry, target.Id));
            Assert.That(targetTable.Get("image") == texture, Is.True);
            targetTable.Remove("image"); EditorUtility.SetDirty(targetTable); AssetDatabase.SaveAssetIfDirty(targetTable);
            NewTable("Ru", appearance, true); config.Sets[0].SetSources("ru", Source("Ru")); target.SetSources("ru", Source("Target")); SaveConfig();
            using (var edit = new LocalizationProjectAssetEdit(config))
            { Assert.That(Assert.Throws<InvalidOperationException>(() => edit.MoveAssetEntry(entry, target.Id)).Message, Does.Contain("Different language")); }
            Assert.That(targetTable.HasKey("image"), Is.False); Assert.That(config.Sets[0].Entries[0].Id, Is.EqualTo(entry));
        }
        [Test] public void NewEntryRequiresExplicitDestinationWhenSeveralTablesAreConnected()
        {
            var second = NewTable("Second"); config.Sets[0].SetSources("en", Source("Table"), Source("Second")); SaveConfig();
            using var edit = new LocalizationProjectAssetEdit(config); string id = edit.Draft.Sets[0].AddEntry("new", LocalizationEntryKind.Asset).Id;
            Assert.Throws<InvalidOperationException>(() => edit.SetAsset(id, "en", texture));
            edit.SetAsset(id, "en", texture, prefix + "/Second"); edit.Commit();
            Assert.That(second.Get("new") == texture, Is.True); Assert.That(table.HasKey("new"), Is.False);
        }
        [Test] public void RemovingSharedDefinitionKeepsValueUntilLastRegisteredConsumerIsRemoved()
        {
            var other = config.AddSet("Shared"); string otherEntry = other.AddEntry("image", LocalizationEntryKind.Asset).Id; other.SetSources("en", Source("Table")); SaveConfig();
            using (var edit = new LocalizationProjectAssetEdit(config)) { edit.RemoveAssetEntry(entry); edit.Commit(); }
            Assert.That(table.HasKey("image"), Is.True);
            using (var edit = new LocalizationProjectAssetEdit(config)) { edit.RemoveAssetEntry(otherEntry); edit.Commit(); }
            Assert.That(table.HasKey("image"), Is.False);
        }
        [Test] public void SourceRoutingCannotChangeAfterEntryChangesHaveBeenStaged()
        {
            using var edit = new LocalizationProjectAssetEdit(config); edit.SetAsset(entry, "en", texture);
            edit.Draft.Sets[0].SetSources("en", Source("Other"));
            Assert.That(Assert.Throws<InvalidOperationException>(() => edit.Commit()).Message, Does.Contain("routing changed")); Assert.That(table.Get("image"), Is.Null);
        }
        [Test] public void AddressBecomingAmbiguousAfterSourceEditBlocksCommit()
        {
            using var edit = new LocalizationProjectAssetEdit(config); edit.SetAsset(entry, "en", texture);
            string duplicate = root + "/Duplicate/Resources/" + prefix + "/Table.asset";
            Directory.CreateDirectory(Path.GetDirectoryName(duplicate)); File.Copy(tablePath, duplicate); AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Assert.That(Assert.Throws<InvalidOperationException>(() => edit.Commit()).Message, Does.Contain("Ambiguous")); Assert.That(table.Get("image"), Is.Null);
        }
    }
}
