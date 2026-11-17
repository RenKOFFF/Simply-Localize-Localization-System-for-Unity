using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using SimplyLocalize.Components;
using SimplyLocalize.Editor;
using SimplyLocalize.Editor.Data;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace SimplyLocalize.Tests
{
    public class KeyRenameTests
    {
        private const string Old = "stage7.old", New = "stage7.new";
        private string root, entryId;
        private LocalizationProjectConfig config;
        private ProjectNotesTestScope notesCleanup;
        private Scene active;
        private bool temporaryRunnerScene;
        private readonly List<Scene> scenes = new();
        private LocalizationEntryReference Reference => new(config.MainChannelId, entryId);
        private string TextPath(string language) => root + "/Resources/SimplyLocalize/Translations/" + language + "/Common.json";
        [SetUp] public void Setup()
        {
            active = SceneManager.GetActiveScene(); root = "Assets/SimplyLocalizeRename_" + Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(root); AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            temporaryRunnerScene = active.path.Length == 0;
            // Unity forbids additive scene creation beside an untitled Test Runner scene.
            if (temporaryRunnerScene) EditorSceneManager.SaveScene(active, root + "/Runner.unity");
            config = LocalizationProjectAuthoring.Create(new[] { "en", "ru" }, "en", "en", root + "/Project.asset", false);
            notesCleanup = new ProjectNotesTestScope(config); entryId = config.Sets[0].AddEntry(Old).Id;
            config.Sets[0].AddEntry("other"); Save();
            foreach (string language in new[] { "en", "ru" }) File.WriteAllText(TextPath(language), new JObject
            { ["custom"] = "preserve", ["translations"] = new JObject { [Old] = language + " value", ["other"] = "other value" } }.ToString());
            using var edit = new LocalizationProjectEdit(config, config.Sets[0].Id); edit.SetDescription(entryId, "description"); edit.Commit();
        }
        [TearDown] public void Cleanup()
        {
            StageUtility.GoToMainStage();
            foreach (var scene in scenes) if (scene.IsValid()) EditorSceneManager.CloseScene(scene, true);
            scenes.Clear(); if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active);
            if (temporaryRunnerScene) EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            notesCleanup?.Dispose();
            foreach (string file in Directory.GetFiles(root, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
            AssetDatabase.DeleteAsset(root);
        }
        private void Save() { EditorUtility.SetDirty(config); AssetDatabase.SaveAssetIfDirty(config); }
        private LocalizationProjectChange Rename(string key = New)
        { using var edit = new LocalizationProjectEdit(config, config.Sets[0].Id); edit.RenameTextEntry(entryId, key); return edit.Commit(); }
        private void Resolves(LocalizationEntryReference reference, string key = New)
        {
            Assert.That(config.CreateEntryIndex().Resolve(reference, out var found), Is.EqualTo(LocalizationReferenceStatus.Found));
            Assert.That(found.Key, Is.EqualTo(key));
        }
        private RenameReferenceAsset Asset(string name = "Reference")
        {
            var asset = ScriptableObject.CreateInstance<RenameReferenceAsset>();
            asset.reference = Reference; asset.references.Add(Reference); asset.key = Old; asset.literal = Old;
            asset.nested.Add(new RenameNestedReference { reference = Reference, key = Old, literal = Old });
            asset.managed = new RenameNestedReference { reference = Reference, key = Old, literal = Old };
            AssetDatabase.CreateAsset(asset, root + "/" + name + ".asset"); return asset;
        }
        private Scene NewScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            scenes.Add(scene); SceneManager.SetActiveScene(scene); return scene;
        }
        private GameObject Consumer()
        {
            var go = new GameObject("References"); go.SetActive(false);
            var text = go.AddComponent<LocalizedText>(); text.Entry = Reference; text.Key = Old;
            var custom = go.AddComponent<RenameReferenceComponent>(); custom.reference = Reference; custom.key = Old; custom.literal = Old;
            return go;
        }
        private Dictionary<string, byte[]> Snapshot() => Directory.GetFiles(root, "*", SearchOption.AllDirectories).ToDictionary(p => p, File.ReadAllBytes);
        private static void Unchanged(Dictionary<string, byte[]> before) { foreach (var pair in before) Assert.That(File.ReadAllBytes(pair.Key), Is.EqualTo(pair.Value), pair.Key); }

        [Test] public void TextRenamePreservesTranslationsNotesGuidsAndSerializedReferences()
        {
            var asset = Asset(); string path = AssetDatabase.GetAssetPath(asset);
            var sub = ScriptableObject.CreateInstance<RenameReferenceAsset>(); sub.reference = Reference;
            AssetDatabase.AddObjectToAsset(sub, asset); AssetDatabase.SaveAssetIfDirty(asset);
            byte[] bytes = File.ReadAllBytes(path), notes = File.ReadAllBytes(LocalizationProjectNotes.GetAssetPath(config));
            string guid = AssetDatabase.AssetPathToGUID(path); Rename();
            foreach (var reference in new[] { asset.reference, sub.reference, asset.references[0], asset.nested[0].reference, asset.managed.reference }) Resolves(reference);
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(bytes)); Assert.That(AssetDatabase.AssetPathToGUID(path), Is.EqualTo(guid));
            Assert.That(asset.key, Is.EqualTo(Old)); Assert.That(asset.literal, Is.EqualTo(Old)); Assert.That(asset.managed.literal, Is.EqualTo(Old));
            Assert.That(File.ReadAllBytes(LocalizationProjectNotes.GetAssetPath(config)), Is.EqualTo(notes));
            Assert.That(LocalizationProjectNotes.Load(config)[entryId], Is.EqualTo("description"));
            foreach (string language in new[] { "en", "ru" })
            {
                var json = JObject.Parse(File.ReadAllText(TextPath(language)));
                Assert.That((string)json["translations"][New], Is.EqualTo(language + " value")); Assert.That(json["translations"][Old], Is.Null);
                Assert.That((string)json["custom"], Is.EqualTo("preserve"));
            }
        }
        [Test] public void EqualTextAndAssetKeysInDifferentSetsAreIndependent()
        {
            var assets = config.AddSet("Assets"); config.MainChannel.IncludeSet(assets.Id);
            string assetId = assets.AddEntry(Old, LocalizationEntryKind.Asset).Id; Save();
            LocalizationProjectAuthoring.CreateAssetTable(config, assets.Id, "en");
            var resource = Asset("Resource");
            using (var edit = new LocalizationProjectAssetEdit(config)) { edit.SetAsset(assetId, "en", resource); edit.Commit(); }
            var assetReference = new LocalizationEntryReference(config.MainChannelId, assetId); Rename(); Resolves(Reference); Resolves(assetReference, Old);
            using (var edit = new LocalizationProjectAssetEdit(config)) { edit.RenameAssetEntry(assetId, "asset.new"); edit.Commit(); }
            Resolves(Reference); Resolves(assetReference, "asset.new");
            Assert.That(EditorProjectAssetData.Load(config, setId: assets.Id).TryGetDirect(assetId, "en", out var cell), Is.True);
            Assert.That(cell.Asset, Is.EqualTo(resource));
        }
        [TestCase(false)][TestCase(true)] public void ChainedAndCyclicRenamesPreserveEachEntryIdentity(bool cycle)
        {
            string other = config.Sets[0].Entries.Single(e => e.Key == "other").Id;
            var otherRef = new LocalizationEntryReference(config.MainChannelId, other);
            using var edit = new LocalizationProjectEdit(config, config.Sets[0].Id);
            edit.RenameTextEntry(other, "temporary"); edit.RenameTextEntry(entryId, "other");
            edit.RenameTextEntry(other, cycle ? Old : "third"); edit.Commit();
            Resolves(Reference, "other"); Resolves(otherRef, cycle ? Old : "third");
        }
        [Test] public void ClosedSceneAndUnicodeKeyKeepBytesAndSceneSetup()
        {
            var scene = NewScene(); Consumer(); string path = root + "/Closed.unity";
            EditorSceneManager.SaveScene(scene, path); EditorSceneManager.CloseScene(scene, true);
            SceneManager.SetActiveScene(active); var setup = EditorSceneManager.GetSceneManagerSetup();
            byte[] bytes = File.ReadAllBytes(path); string unusual = "ключ/\"日本語"; Rename(unusual);
            Assert.That(EditorSceneManager.GetSceneManagerSetup().Select(s => (s.path, s.isLoaded, s.isActive)), Is.EqualTo(setup.Select(s => (s.path, s.isLoaded, s.isActive))));
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(bytes));
            scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive); scenes.Add(scene);
            Resolves(scene.GetRootGameObjects()[0].GetComponent<LocalizedText>().Entry, unusual);
        }
        [Test] public void PrefabVariantsNestedInstancesAndOverridesAreNotRewritten()
        {
            var scene = NewScene(); var go = Consumer();
            string basePath = root + "/Base.prefab", variantPath = root + "/Variant.prefab", nestedPath = root + "/Nested.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, basePath); Object.DestroyImmediate(go);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            var custom = instance.GetComponent<RenameReferenceComponent>(); custom.literal = "User override";
            PrefabUtility.RecordPrefabInstancePropertyModifications(custom);
            PrefabUtility.SaveAsPrefabAsset(instance, variantPath); Object.DestroyImmediate(instance);
            var parent = new GameObject("Parent"); instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene); instance.transform.SetParent(parent.transform);
            PrefabUtility.SaveAsPrefabAsset(parent, nestedPath); Object.DestroyImmediate(parent);
            instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            string scenePath = root + "/Instance.unity"; EditorSceneManager.SaveScene(scene, scenePath);
            var before = new[] { basePath, variantPath, nestedPath, scenePath }.ToDictionary(p => p, File.ReadAllBytes); Rename(); Unchanged(before);
            Resolves(instance.GetComponent<LocalizedText>().Entry);
            foreach (string path in new[] { basePath, variantPath, nestedPath })
            {
                var loaded = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var text = loaded.GetComponentInChildren<LocalizedText>(true); Resolves(text.Entry);
                    if (path != basePath) { using var serialized = new SerializedObject(text); Assert.That(serialized.FindProperty("_entry").prefabOverride, Is.False); }
                    if (path == variantPath) Assert.That(loaded.GetComponent<RenameReferenceComponent>().literal, Is.EqualTo("User override"));
                }
                finally { PrefabUtility.UnloadPrefabContents(loaded); }
            }
        }
        [TestCase("readonly")][TestCase("locked")][TestCase("dirty")]
        public void UnwritableReferenceDoesNotBlockRenameOrLosePendingUserEdit(string reason)
        {
            var asset = Asset(); string path = AssetDatabase.GetAssetPath(asset); byte[] bytes = File.ReadAllBytes(path);
            if (reason == "readonly") File.SetAttributes(path, FileAttributes.ReadOnly);
            if (reason == "dirty") { asset.literal = "pending edit"; EditorUtility.SetDirty(asset); }
            using var locked = reason == "locked" ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read) : null;
            Rename(); Resolves(asset.reference); Assert.That(File.ReadAllBytes(path), Is.EqualTo(bytes));
            if (reason == "dirty") { Assert.That(asset.literal, Is.EqualTo("pending edit")); Assert.That(EditorUtility.IsDirty(asset), Is.True); EditorUtility.ClearDirty(asset); }
        }
        [Test] public void DirtySavedAndUntitledScenesAreNotSavedByRename()
        {
            var saved = NewScene(); var go = Consumer(); string path = root + "/Dirty.unity"; EditorSceneManager.SaveScene(saved, path);
            byte[] bytes = File.ReadAllBytes(path); go.name = "User pending edit"; EditorSceneManager.MarkSceneDirty(saved);
            var untitled = NewScene(); var other = Consumer(); Rename();
            Resolves(go.GetComponent<LocalizedText>().Entry); Resolves(other.GetComponent<LocalizedText>().Entry);
            Assert.That(saved.isDirty, Is.True); Assert.That(go.name, Is.EqualTo("User pending edit"));
            Assert.That(untitled.path, Is.Empty); Assert.That(File.ReadAllBytes(path), Is.EqualTo(bytes));
        }
        [Test] public void ConflictingKeyAndExternalChangesPreserveDataAndReferences()
        {
            var asset = Asset(); var before = Snapshot(); Assert.Throws<ArgumentException>(() => Rename("other")); Unchanged(before); Resolves(asset.reference, Old);
            using var edit = new LocalizationProjectEdit(config, config.Sets[0].Id); edit.RenameTextEntry(entryId, New);
            File.AppendAllText(TextPath("ru"), " "); before = Snapshot();
            Assert.Throws<InvalidOperationException>(() => edit.Commit()); Unchanged(before); Resolves(asset.reference, Old);
        }
        [Test] public void RenameUndoRedoChangesTheIndexWithoutRewritingConsumers()
        {
            var asset = Asset(); string path = AssetDatabase.GetAssetPath(asset); byte[] bytes = File.ReadAllBytes(path);
            using var record = LocalizationProjectUndo.Record(Rename(), "Rename entry"); Resolves(asset.reference);
            Undo.PerformUndo(); Resolves(asset.reference, Old); Undo.PerformRedo(); Resolves(asset.reference);
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(bytes));
        }
        [Test] public void CyclicManagedReferencesKeepIdentityWithoutTraversal()
        {
            var asset = Asset(); asset.managed.next = asset.managed; EditorUtility.SetDirty(asset); AssetDatabase.SaveAssetIfDirty(asset);
            byte[] bytes = File.ReadAllBytes(AssetDatabase.GetAssetPath(asset)); Rename();
            Resolves(asset.managed.reference); Assert.That(asset.managed.next, Is.SameAs(asset.managed));
            Assert.That(asset.managed.key, Is.EqualTo(Old)); Assert.That(File.ReadAllBytes(AssetDatabase.GetAssetPath(asset)), Is.EqualTo(bytes));
        }
        [Test] public void UnloadedSceneRemainsUnloadedAndResolvesRenamedEntryWhenOpened()
        {
            var scene = NewScene(); Consumer(); string path = root + "/Unloaded.unity";
            EditorSceneManager.SaveScene(scene, path); EditorSceneManager.CloseScene(scene, false); SceneManager.SetActiveScene(active);
            var setup = EditorSceneManager.GetSceneManagerSetup(); Rename();
            Assert.That(EditorSceneManager.GetSceneManagerSetup().Select(s => (s.path, s.isLoaded, s.isActive)), Is.EqualTo(setup.Select(s => (s.path, s.isLoaded, s.isActive))));
            scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive); scenes.Add(scene);
            Resolves(scene.GetRootGameObjects()[0].GetComponent<LocalizedText>().Entry);
        }
        [Test] public void LocalUpmReferenceRemainsReadableAndIsNeverWrittenDuringRename()
        {
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(LocalizationProjectConfig).Assembly);
            Assert.That(package.source, Is.EqualTo(UnityEditor.PackageManager.PackageSource.Local));
            string path = package.assetPath + "/__RenameTest_" + Guid.NewGuid().ToString("N") + ".asset";
            var asset = ScriptableObject.CreateInstance<RenameReferenceAsset>(); asset.reference = Reference;
            try
            {
                AssetDatabase.CreateAsset(asset, path); string disk = Path.Combine(package.resolvedPath, Path.GetFileName(path)); byte[] bytes = File.ReadAllBytes(disk);
                Rename(); Resolves(asset.reference); Assert.That(File.ReadAllBytes(disk), Is.EqualTo(bytes));
            }
            finally { AssetDatabase.DeleteAsset(path); if (asset) Object.DestroyImmediate(asset); }
        }
        [TestCase(false)][TestCase(true)] public void AssetRenameRejectsReadOnlyOrConflictingTableWithoutChangingOtherSources(bool conflict)
        {
            var set = config.AddSet("Assets"); config.MainChannel.IncludeSet(set.Id);
            string id = set.AddEntry(Old, LocalizationEntryKind.Asset).Id; Save();
            foreach (string language in new[] { "en", "ru" }) LocalizationProjectAuthoring.CreateAssetTable(config, set.Id, language);
            var resource = Asset("Resource");
            using (var edit = new LocalizationProjectAssetEdit(config))
            { foreach (string language in new[] { "en", "ru" }) edit.SetAsset(id, language, resource); edit.Commit(); }
            var source = config.Sets.Single(s => s.Id == set.Id).Sources.Single(s => s.Language == "ru").Sources.Single();
            string path = new ResourcesEditorTextReader().ResolveSourceAssetPath(source);
            if (conflict)
            {
                var table = AssetDatabase.LoadAssetAtPath<LocalizationAssetTable>(path);
                table.Set(New, resource); EditorUtility.SetDirty(table); AssetDatabase.SaveAssetIfDirty(table);
            }
            else File.SetAttributes(path, FileAttributes.ReadOnly);
            var before = Snapshot();
            Assert.Catch<Exception>(() =>
            { using var edit = new LocalizationProjectAssetEdit(config); edit.RenameAssetEntry(id, New); edit.Commit(); });
            Unchanged(before); Resolves(new LocalizationEntryReference(config.MainChannelId, id), Old);
        }
        [Test] public void PendingPrefabStageIsNotSavedOrChangedByRename()
        {
            var scene = NewScene(); var go = Consumer(); string path = root + "/Editing.prefab";
            PrefabUtility.SaveAsPrefabAsset(go, path); Object.DestroyImmediate(go); EditorSceneManager.SaveScene(scene, root + "/Empty.unity");
            try
            {
                var stage = PrefabStageUtility.OpenPrefab(path);
                stage.prefabContentsRoot.GetComponent<RenameReferenceComponent>().literal = "Pending stage edit";
                EditorSceneManager.MarkSceneDirty(stage.scene); byte[] bytes = File.ReadAllBytes(path); Rename();
                Resolves(stage.prefabContentsRoot.GetComponent<LocalizedText>().Entry);
                Assert.That(stage.scene.isDirty, Is.True); Assert.That(File.ReadAllBytes(path), Is.EqualTo(bytes));
                Assert.That(stage.prefabContentsRoot.GetComponent<RenameReferenceComponent>().literal, Is.EqualTo("Pending stage edit"));
            }
            finally
            {
                var stage = PrefabStageUtility.GetCurrentPrefabStage();
                if (stage != null)
                    typeof(PrefabStage).GetMethod("SavePrefab", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(stage, null);
                StageUtility.GoToMainStage();
            }
        }
        [TestCase(false)][TestCase(true)] public void MoveKeepsReferenceAndReportsUnavailableTargetSet(bool includeTarget)
        {
            var asset = Asset(); var target = config.AddSet("Target"); if (includeTarget) config.MainChannel.IncludeSet(target.Id);
            foreach (string language in new[] { "en", "ru" }) target.SetSources(language, config.Sets[0].Sources.Single(s => s.Language == language).Sources.ToArray());
            Save();
            using var edit = new LocalizationProjectEdit(config, config.Sets[0].Id, target.Id); edit.MoveTextEntry(entryId, target.Id); edit.Commit();
            var status = config.CreateEntryIndex().Resolve(asset.reference, out var location);
            Assert.That(status, Is.EqualTo(includeTarget ? LocalizationReferenceStatus.Found : LocalizationReferenceStatus.SetNotAvailable));
            if (includeTarget) Assert.That(location.SetId, Is.EqualTo(target.Id));
        }
        [Test] public void RemovedEntryIsNotReboundToANewEntryWithTheSameKey()
        {
            var asset = Asset();
            using var edit = new LocalizationProjectEdit(config, config.Sets[0].Id); edit.RemoveTextEntry(entryId); string other = edit.Draft.Sets[0].AddEntry(Old).Id; edit.Commit();
            Assert.That(other, Is.Not.EqualTo(entryId)); Assert.That(config.CreateEntryIndex().Resolve(asset.reference, out _), Is.EqualTo(LocalizationReferenceStatus.MissingEntry));
        }
    }
}
