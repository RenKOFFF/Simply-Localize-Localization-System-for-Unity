using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using SimplyLocalize.Components;
using SimplyLocalize.Editor.Data;
using SimplyLocalize.Editor.Utilities;
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
        private string _root, _base;
        private LocalizationConfig _config;
        private EditorLocalizationData _data;
        private Scene _active;
        private bool _temporaryRunnerScene;
        private readonly List<Scene> _scenes = new();
        private string[] Roots => new[] { _root };
        private string Disk(string path) => Path.GetFullPath(path);

        [SetUp] public void SetUp()
        {
            _active = SceneManager.GetActiveScene();
            string id = "Rename_" + Guid.NewGuid().ToString("N");
            _root = "Assets/__" + id;
            _base = _root + "/Resources/" + id;
            _config = ScriptableObject.CreateInstance<LocalizationConfig>();
            _config.resourcesBasePath = id;
            foreach (string code in new[] { "en", "ru" })
            {
                var profile = ScriptableObject.CreateInstance<LanguageProfile>();
                profile.languageCode = code;
                _config.languages.Add(profile);
                Directory.CreateDirectory(Disk(_base + "/" + code + "/text"));
                File.WriteAllText(Disk(_base + "/" + code + "/text/test.json"), new JObject
                { ["custom"] = "preserve", ["translations"] = new JObject { [Old] = code + " value", ["other"] = "other value" } }.ToString());
            }
            _config.defaultLanguage = _config.languages[0];
            File.WriteAllText(Disk(_base + "/_meta.json"), new JObject { [Old] = new JObject { ["description"] = "description", ["custom"] = 42 } }.ToString());
            AssetDatabase.Refresh();
            _temporaryRunnerScene = _active.path.Length == 0;
            if (_temporaryRunnerScene)
            {
                // Unity Test Runner supplies an untitled scene (sometimes with its default camera/light).
                EditorSceneManager.SaveScene(_active, _root + "/Runner.unity");
            }
            _data = new EditorLocalizationData(); _data.Load(_config);
        }

        [TearDown] public void TearDown()
        {
            foreach (var scene in _scenes)
                if (scene.IsValid()) EditorSceneManager.CloseScene(scene, true);
            _scenes.Clear();
            if (_active.IsValid() && _active.isLoaded) SceneManager.SetActiveScene(_active);
            if (_temporaryRunnerScene) EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            foreach (var profile in _config.languages) Object.DestroyImmediate(profile);
            Object.DestroyImmediate(_config);
            if (Directory.Exists(Disk(_root)))
            {
                foreach (string file in Directory.GetFiles(Disk(_root), "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
                AssetDatabase.DeleteAsset(_root);
            }
        }

        private RenameReferenceAsset Asset(string name = "Reference", string key = Old)
        {
            var asset = ScriptableObject.CreateInstance<RenameReferenceAsset>();
            asset.key = key; asset.preview = key; asset.literal = key;
            asset.keys.Add(key); asset.nested.Add(new RenameNestedReference { key = key, literal = key });
            asset.managed = new RenameNestedReference { key = key, literal = key };
            AssetDatabase.CreateAsset(asset, _root + "/" + name + ".asset");
            return asset;
        }
        private Scene Scene(string name = null)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            _scenes.Add(scene); SceneManager.SetActiveScene(scene);
            return scene;
        }
        private GameObject ObjectWithKeys(string key = Old)
        {
            var go = new GameObject("References"); go.SetActive(false);
            SetKey(go.AddComponent<LocalizedText>(), key);
            SetKey(go.AddComponent<LocalizedAudioClip>(), key);
            var custom = go.AddComponent<RenameReferenceComponent>(); custom.key = key; custom.nested.key = key; custom.literal = key;
            return go;
        }
        private static void SetKey(Object target, string key)
        {
            using var serialized = new SerializedObject(target);
            serialized.FindProperty("_key").stringValue = key; serialized.ApplyModifiedPropertiesWithoutUndo();
        }
        private static string Key(Object target) { using var s = new SerializedObject(target); return s.FindProperty("_key").stringValue; }
        private Dictionary<string, byte[]> Snapshot() => Directory.GetFiles(Disk(_root), "*", SearchOption.AllDirectories).ToDictionary(p => p, File.ReadAllBytes);
        private void AssertUnchanged(Dictionary<string, byte[]> before)
        {
            Assert.That(Directory.GetFiles(Disk(_root), "*", SearchOption.AllDirectories), Is.EquivalentTo(before.Keys));
            foreach (var pair in before) Assert.That(File.ReadAllBytes(pair.Key), Is.EqualTo(pair.Value), pair.Key);
            Assert.That(_data.GetTranslation(Old, "en"), Is.EqualTo("en value"));
            Assert.That(_data.GetFileForKey(New), Is.Null);
        }

        [Test] public void TextRename_PreservesTranslationsMetadataGuidsAndUnmarkedStrings()
        {
            var asset = Asset(); string path = AssetDatabase.GetAssetPath(asset), guid = AssetDatabase.AssetPathToGUID(path);
            var sub = ScriptableObject.CreateInstance<RenameReferenceAsset>(); sub.key = Old;
            AssetDatabase.AddObjectToAsset(sub, asset); AssetDatabase.SaveAssetIfDirty(asset);
            Assert.That(LocalizationKeyRenamer.RenameText(_data, Old, New, true, Roots), Is.EqualTo(2));
            Assert.That(asset.key, Is.EqualTo(New)); Assert.That(sub.key, Is.EqualTo(New));
            Assert.That(asset.preview, Is.EqualTo(New)); Assert.That(asset.keys, Is.EqualTo(new[] { New }));
            Assert.That(asset.nested[0].key, Is.EqualTo(New)); Assert.That(asset.managed.key, Is.EqualTo(New));
            Assert.That(asset.literal, Is.EqualTo(Old)); Assert.That(asset.nested[0].literal, Is.EqualTo(Old));
            Assert.That(AssetDatabase.AssetPathToGUID(path), Is.EqualTo(guid));
            var reloaded = new EditorLocalizationData(); reloaded.Load(_config);
            foreach (string code in new[] { "en", "ru" }) Assert.That(reloaded.GetTranslation(New, code), Is.EqualTo(code + " value"));
            Assert.That(reloaded.GetFileForKey(Old), Is.Null);
            Assert.That(reloaded.GetDescription(New), Is.EqualTo("description"));
            Assert.That(reloaded.GetPreviousKeys(New), Does.Contain(Old));
            Assert.That((int)JObject.Parse(File.ReadAllText(Disk(_base + "/_meta.json")))[New]["custom"], Is.EqualTo(42));
        }

        [Test] public void TextAndAssetKeys_WithSameName_AreIndependent()
        {
            var scene = Scene(); var go = ObjectWithKeys();
            EditorSceneManager.SaveScene(scene, _root + "/Open.unity");
            var table = ScriptableObject.CreateInstance<LocalizationAssetTable>();
            var resource = Asset("Resource", "unrelated"); table.Set(Old, resource); AssetDatabase.CreateAsset(table, _root + "/Table.asset");
            LocalizationKeyRenamer.RenameText(_data, Old, New, true, Roots);
            Assert.That(Key(go.GetComponent<LocalizedText>()), Is.EqualTo(New));
            Assert.That(Key(go.GetComponent<LocalizedAudioClip>()), Is.EqualTo(Old));
            Assert.That(go.GetComponent<RenameReferenceComponent>().key, Is.EqualTo(New));
            Assert.That(table.HasKey(Old), Is.True);
            LocalizationKeyRenamer.RenameAssets(new[] { table }, Old, "asset.new", true, Roots);
            Assert.That(table.Get("asset.new"), Is.EqualTo(resource)); Assert.That(table.HasKey(Old), Is.False);
            Assert.That(Key(go.GetComponent<LocalizedAudioClip>()), Is.EqualTo("asset.new"));
            Assert.That(Key(go.GetComponent<LocalizedText>()), Is.EqualTo(New));
        }

        [TestCase(false)] [TestCase(true)]
        public void BatchMappings_AreSimultaneousIncludingCycles(bool cycle)
        {
            var a = Asset("A", "A"); var b = Asset("B", "B");
            Assert.That(KeyReferenceUpdater.UpdateReferences(new Dictionary<string, string> { ["A"] = "B", ["B"] = cycle ? "A" : "C" }, Roots), Is.EqualTo(2));
            Assert.That(a.key, Is.EqualTo("B")); Assert.That(b.key, Is.EqualTo(cycle ? "A" : "C"));
        }

        [Test] public void ClosedScene_HandlesEscapedUnicodeKeysAndRestoresSceneSetup()
        {
            const string unusual = "ключ/\"\n";
            var scene = Scene(); ObjectWithKeys(unusual);
            string path = _root + "/Closed.unity";
            EditorSceneManager.SaveScene(scene, path); EditorSceneManager.CloseScene(scene, true);
            SceneManager.SetActiveScene(_active); int count = SceneManager.sceneCount;
            string guid = AssetDatabase.AssetPathToGUID(path);
            KeyReferenceUpdater.UpdateReferences(new Dictionary<string, string> { [unusual] = New }, Roots);
            Assert.That(SceneManager.sceneCount, Is.EqualTo(count)); Assert.That(SceneManager.GetActiveScene(), Is.EqualTo(_active));
            scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive); _scenes.Add(scene);
            Assert.That(Key(scene.GetRootGameObjects()[0].GetComponent<LocalizedText>()), Is.EqualTo(New));
            Assert.That(AssetDatabase.AssetPathToGUID(path), Is.EqualTo(guid));
        }

        [Test] public void PrefabVariantsAndNestedInstances_PreserveInheritedValuesWithoutOverrides()
        {
            var scene = Scene(); var go = ObjectWithKeys();
            string basePath = _root + "/Base.prefab", variantPath = _root + "/Variant.prefab", nestedPath = _root + "/Nested.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, basePath); Object.DestroyImmediate(go);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            var custom = instance.GetComponent<RenameReferenceComponent>(); custom.key = "override";
            PrefabUtility.RecordPrefabInstancePropertyModifications(custom);
            PrefabUtility.SaveAsPrefabAsset(instance, variantPath); Object.DestroyImmediate(instance);
            var parent = new GameObject("Parent"); instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene); instance.transform.SetParent(parent.transform);
            PrefabUtility.SaveAsPrefabAsset(parent, nestedPath); Object.DestroyImmediate(parent);
            instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            EditorSceneManager.SaveScene(scene, _root + "/Instance.unity");
            KeyReferenceUpdater.UpdateReferences(new Dictionary<string, string> { [Old] = New, ["override"] = "override.new" }, Roots);
            foreach (string path in new[] { basePath, variantPath, nestedPath })
            {
                var loaded = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var text = loaded.GetComponentInChildren<LocalizedText>(true);
                    Assert.That(Key(text), Is.EqualTo(New), path);
                    if (path != basePath)
                    { using var serialized = new SerializedObject(text); Assert.That(serialized.FindProperty("_key").prefabOverride, Is.False, path); }
                    if (path == variantPath) Assert.That(loaded.GetComponent<RenameReferenceComponent>().key, Is.EqualTo("override.new"));
                }
                finally { PrefabUtility.UnloadPrefabContents(loaded); }
            }
            Assert.That(Key(instance.GetComponent<LocalizedText>()), Is.EqualTo(New));
            using var s = new SerializedObject(instance.GetComponent<LocalizedText>());
            Assert.That(s.FindProperty("_key").prefabOverride, Is.False);
        }

        [TestCase("readonly")] [TestCase("locked")] [TestCase("dirty")]
        public void UnwritableReference_StopsBeforeChangingAnyData(string reason)
        {
            var asset = Asset(); string path = Disk(AssetDatabase.GetAssetPath(asset));
            if (reason == "readonly") File.SetAttributes(path, FileAttributes.ReadOnly);
            if (reason == "dirty") { asset.literal = "pending edit"; EditorUtility.SetDirty(asset); }
            using var locked = reason == "locked" ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read) : null;
            var before = Snapshot();
            Assert.That(() => LocalizationKeyRenamer.RenameText(_data, Old, New, true, Roots), Throws.Exception);
            AssertUnchanged(before); Assert.That(asset.key, Is.EqualTo(Old));
            if (reason == "dirty") Assert.That(asset.literal, Is.EqualTo("pending edit"));
        }

        [Test] public void DirtySavedScene_IsNotSilentlySaved()
        {
            var scene = Scene(); var go = ObjectWithKeys(); EditorSceneManager.SaveScene(scene, _root + "/Dirty.unity");
            go.name = "User pending edit"; EditorSceneManager.MarkSceneDirty(scene);
            var before = Snapshot();
            Assert.That(() => LocalizationKeyRenamer.RenameText(_data, Old, New, true, Roots), Throws.InvalidOperationException.With.Message.Contains("Save pending scene"));
            AssertUnchanged(before); Assert.That(go.name, Is.EqualTo("User pending edit")); Assert.That(scene.isDirty, Is.True);
        }

        [Test] public void ConflictAndStaleSource_DoNotChangeReferencesOrData()
        {
            var asset = Asset(); var before = Snapshot();
            Assert.Throws<InvalidOperationException>(() => LocalizationKeyRenamer.RenameText(_data, Old, "other", true, Roots));
            AssertUnchanged(before);
            string path = Disk(_base + "/ru/text/test.json"); File.AppendAllText(path, " "); before = Snapshot();
            Assert.Throws<InvalidOperationException>(() => LocalizationKeyRenamer.RenameText(_data, Old, New, true, Roots));
            AssertUnchanged(before); Assert.That(asset.key, Is.EqualTo(Old));
        }

        [Test] public void FailureAfterReferenceSaves_RollsBackFilesMemoryAndGuid()
        {
            var asset = Asset(); var scene = Scene(); var go = ObjectWithKeys();
            EditorSceneManager.SaveScene(scene, _root + "/Open.unity");
            var prefabSource = ObjectWithKeys();
            PrefabUtility.SaveAsPrefabAsset(prefabSource, _root + "/Rollback.prefab"); Object.DestroyImmediate(prefabSource);
            var closed = Scene(); ObjectWithKeys(); EditorSceneManager.SaveScene(closed, _root + "/ClosedRollback.unity");
            EditorSceneManager.CloseScene(closed, true); SceneManager.SetActiveScene(scene);
            var before = Snapshot();
            var method = typeof(LocalizationKeyRenamer).GetMethod("RenameTextCore", BindingFlags.Static | BindingFlags.NonPublic);
            var error = Assert.Throws<TargetInvocationException>(() => method.Invoke(null, new object[]
            { _data, Old, New, true, Roots, (Action)(() => throw new IOException("Injected save failure")) }));
            Assert.That(error.InnerException, Is.TypeOf<IOException>());
            AssertUnchanged(before); Assert.That(asset.key, Is.EqualTo(Old));
            Assert.That(Key(go.GetComponent<LocalizedText>()), Is.EqualTo(Old));
            Assert.That(go.GetComponent<RenameReferenceComponent>().key, Is.EqualTo(Old));
        }

        [Test] public void ReferenceOnlyRename_InUnsavedSceneSupportsUndo()
        {
            Scene(); var go = ObjectWithKeys(); Undo.IncrementCurrentGroup();
            KeyReferenceUpdater.UpdateReferences(new Dictionary<string, string> { [Old] = New }, Roots);
            Assert.That(Key(go.GetComponent<LocalizedText>()), Is.EqualTo(New));
            Undo.PerformUndo(); Assert.That(Key(go.GetComponent<LocalizedText>()), Is.EqualTo(Old));
        }

        [Test] public void RenameWithoutReferences_LeavesAssetsAlone()
        {
            var asset = Asset(); LocalizationKeyRenamer.RenameText(_data, Old, New, false, Roots);
            Assert.That(asset.key, Is.EqualTo(Old)); Assert.That(_data.GetTranslation(New, "en"), Is.EqualTo("en value"));
        }

        [Test] public void DestinationMetadataConflict_PreservesBothDescriptions()
        {
            _data.SetDescription(New, "orphan metadata"); _data.SaveMeta();
            var before = Snapshot();
            Assert.Throws<InvalidOperationException>(() => LocalizationKeyRenamer.RenameText(_data, Old, New, true, Roots));
            AssertUnchanged(before); Assert.That(_data.GetDescription(Old), Is.EqualTo("description"));
            Assert.That(_data.GetDescription(New), Is.EqualTo("orphan metadata"));
        }

        [Test] public void ReadOnlyAssetTable_StopsBeforeOtherTablesAndReferencesChange()
        {
            var a = ScriptableObject.CreateInstance<LocalizationAssetTable>(); a.Set(Old, null); AssetDatabase.CreateAsset(a, _root + "/A.asset");
            var b = ScriptableObject.CreateInstance<LocalizationAssetTable>(); b.Set(Old, null); AssetDatabase.CreateAsset(b, _root + "/B.asset");
            var scene = Scene(); var go = ObjectWithKeys(); EditorSceneManager.SaveScene(scene, _root + "/AssetReferences.unity");
            File.SetAttributes(Disk(AssetDatabase.GetAssetPath(b)), FileAttributes.ReadOnly);
            var before = Snapshot();
            Assert.That(() => LocalizationKeyRenamer.RenameAssets(new[] { a, b }, Old, New, true, Roots), Throws.Exception);
            AssertUnchanged(before); Assert.That(a.HasKey(Old), Is.True); Assert.That(b.HasKey(Old), Is.True);
            Assert.That(Key(go.GetComponent<LocalizedAudioClip>()), Is.EqualTo(Old));
        }

        [Test] public void AssetRenameFailure_RestoresTablesReferencesAndObjectBindings()
        {
            var resource = Asset("Resource", "unrelated");
            var table = ScriptableObject.CreateInstance<LocalizationAssetTable>(); table.Set(Old, resource);
            AssetDatabase.CreateAsset(table, _root + "/Table.asset");
            var scene = Scene(); var go = ObjectWithKeys(); EditorSceneManager.SaveScene(scene, _root + "/AssetReferences.unity");
            var before = Snapshot();
            var method = typeof(LocalizationKeyRenamer).GetMethod("RenameAssetsCore", BindingFlags.Static | BindingFlags.NonPublic);
            var error = Assert.Throws<TargetInvocationException>(() => method.Invoke(null, new object[]
            { new[] { table }, Old, New, true, Roots, (Action)(() => throw new IOException("Injected table save failure")) }));
            Assert.That(error.InnerException, Is.TypeOf<IOException>());
            AssertUnchanged(before); Assert.That(table.Get(Old), Is.EqualTo(resource)); Assert.That(table.HasKey(New), Is.False);
            Assert.That(Key(go.GetComponent<LocalizedAudioClip>()), Is.EqualTo(Old));
        }

        [Test] public void CyclicManagedReferences_AreVisitedOnceAndPreserveTheirIdentity()
        {
            var asset = Asset(); asset.managed.next = asset.managed;
            EditorUtility.SetDirty(asset); AssetDatabase.SaveAssetIfDirty(asset);
            Assert.That(KeyReferenceUpdater.UpdateReferences(new Dictionary<string, string> { [Old] = New }, Roots), Is.EqualTo(1));
            Assert.That(asset.managed.key, Is.EqualTo(New)); Assert.That(asset.managed.next, Is.SameAs(asset.managed));
            Assert.That(asset.managed.literal, Is.EqualTo(Old));
        }

        [Test] public void UnloadedScene_RemainsInTheSceneSetupAfterRename()
        {
            var scene = Scene(); ObjectWithKeys(); string path = _root + "/Unloaded.unity";
            EditorSceneManager.SaveScene(scene, path); EditorSceneManager.CloseScene(scene, false);
            SceneManager.SetActiveScene(_active);
            var setup = EditorSceneManager.GetSceneManagerSetup();
            KeyReferenceUpdater.UpdateReferences(new Dictionary<string, string> { [Old] = New }, Roots);
            var after = EditorSceneManager.GetSceneManagerSetup();
            Assert.That(after.Select(s => (s.path, s.isLoaded, s.isActive)), Is.EqualTo(setup.Select(s => (s.path, s.isLoaded, s.isActive))));
            scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive); _scenes.Add(scene);
            Assert.That(Key(scene.GetRootGameObjects()[0].GetComponent<LocalizedText>()), Is.EqualTo(New));
        }

        [Test] public void PendingPrefabStageReference_IsDetectedBeforeWrites()
        {
            var scene = Scene(); var go = ObjectWithKeys("unrelated");
            string path = _root + "/Editing.prefab"; PrefabUtility.SaveAsPrefabAsset(go, path); Object.DestroyImmediate(go);
            EditorSceneManager.SaveScene(scene, _root + "/Empty.unity");
            try
            {
                var stage = PrefabStageUtility.OpenPrefab(path);
                SetKey(stage.prefabContentsRoot.GetComponent<LocalizedText>(), Old);
                // Unsaved stage contents must be checked, even though the disk prefab does not contain Old.
                var before = Snapshot();
                Assert.That(() => LocalizationKeyRenamer.RenameText(_data, Old, New, true, Roots), Throws.InvalidOperationException);
                AssertUnchanged(before);
            }
            finally
            {
                var stage = PrefabStageUtility.GetCurrentPrefabStage();
                if (stage != null)
                {
                    SetKey(stage.prefabContentsRoot.GetComponent<LocalizedText>(), "unrelated");
                    // This fixture is ours; saving it avoids an interactive save prompt when leaving Prefab Mode.
                    typeof(PrefabStage).GetMethod("SavePrefab", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(stage, null);
                }
                StageUtility.GoToMainStage();
            }
        }

        [Test] public void LocalUpmPackageReference_RejectsRenameWithoutEditingPackage()
        {
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(LocalizationConfig).Assembly);
            Assert.That(package.source, Is.EqualTo(UnityEditor.PackageManager.PackageSource.Local));
            string path = package.assetPath + "/__RenameTest_" + Guid.NewGuid().ToString("N") + ".asset";
            var asset = ScriptableObject.CreateInstance<RenameReferenceAsset>(); asset.key = Old;
            try
            {
                AssetDatabase.CreateAsset(asset, path);
                var before = Snapshot();
                string physical = Path.Combine(package.resolvedPath, Path.GetFileName(path));
                byte[] packageBytes = File.ReadAllBytes(physical);
                Assert.That(() => LocalizationKeyRenamer.RenameText(_data, Old, New, true, new[] { _root, package.assetPath }),
                    Throws.InvalidOperationException.With.Message.Contains("belongs to a package"));
                AssertUnchanged(before); Assert.That(File.ReadAllBytes(physical), Is.EqualTo(packageBytes));
                Assert.That(asset.key, Is.EqualTo(Old));
            }
            finally { AssetDatabase.DeleteAsset(path); if (asset) Object.DestroyImmediate(asset); }
        }

        [Test] public void AssetTableConflict_StopsBeforeAnyTableIsRenamed()
        {
            var a = ScriptableObject.CreateInstance<LocalizationAssetTable>(); a.Set(Old, null); AssetDatabase.CreateAsset(a, _root + "/A.asset");
            var b = ScriptableObject.CreateInstance<LocalizationAssetTable>(); b.Set(New, null); AssetDatabase.CreateAsset(b, _root + "/B.asset");
            var before = Snapshot();
            Assert.Throws<InvalidOperationException>(() => LocalizationKeyRenamer.RenameAssets(new[] { a, b }, Old, New, true, Roots));
            AssertUnchanged(before); Assert.That(a.HasKey(Old), Is.True);
        }
    }
}
