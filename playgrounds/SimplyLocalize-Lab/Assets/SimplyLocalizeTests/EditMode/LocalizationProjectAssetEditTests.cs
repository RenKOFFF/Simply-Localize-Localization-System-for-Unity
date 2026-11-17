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
    }
}
