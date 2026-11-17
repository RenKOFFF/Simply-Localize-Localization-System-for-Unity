using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using SimplyLocalize.Components;
using SimplyLocalize.Editor;
using SimplyLocalize.Editor.Data;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace SimplyLocalize.Tests
{
    public class ProjectAutoLocalizeTests
    {
        private string root, source;
        private LocalizationProjectConfig config;
        private Scene scene;
        private GameObject fixture;
        private int undoGroup;
        private HashSet<int> previousUndo;
        [SetUp] public void Setup()
        {
            undoGroup = 0; previousUndo = null;
            root = "Assets/SimplyLocalizeAuto_" + Guid.NewGuid().ToString("N"); Directory.CreateDirectory(root); AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            config = LocalizationProjectAuthoring.Create(new[] { "en", "ru" }, "en", "en", root + "/Project.asset", false);
            source = root + "/Resources/SimplyLocalize/Translations/en/Common.json";
            scene = SceneManager.GetActiveScene(); fixture = new GameObject("SimplyLocalizeAutoFixture");
            previousUndo = new HashSet<int>(Resources.FindObjectsOfTypeAll<LocalizationProjectUndo>().Select(r => r.GetInstanceID()));
            Undo.IncrementCurrentGroup(); undoGroup = Undo.GetCurrentGroup();
        }
        [TearDown] public void Cleanup()
        {
            if (previousUndo != null) foreach (var record in Resources.FindObjectsOfTypeAll<LocalizationProjectUndo>().Where(r => !previousUndo.Contains(r.GetInstanceID()))) record.Dispose();
            if (undoGroup > 0) Undo.RevertAllDownToGroup(undoGroup);
            if (fixture != null) Object.DestroyImmediate(fixture);
            AssetDatabase.DeleteAsset(root);
        }
        private GameObject Label(string name, string text, Transform parent = null)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text)); SceneManager.MoveGameObjectToScene(go, scene);
            go.transform.SetParent(parent != null ? parent : fixture.transform, false); go.GetComponent<Text>().text = text; return go;
        }
        private AutoLocalizeOptions Options(AutoLocalizePrefabMode prefabs = AutoLocalizePrefabMode.Skip) => new() { Prefabs = prefabs, KeyPattern = AutoLocalizeKeyPattern.ObjectName };
        private void SaveConfig() { EditorUtility.SetDirty(config); AssetDatabase.SaveAssetIfDirty(config); }
        private string Prefab(bool duplicateNames = false)
        {
            var go = new GameObject("Panel", typeof(RectTransform)); SceneManager.MoveGameObjectToScene(go, scene);
            go.transform.SetParent(fixture.transform, false);
            Label("Label", "First", go.transform); if (duplicateNames) Label("Label", "Second", go.transform);
            string path = root + "/Panel.prefab"; PrefabUtility.SaveAsPrefabAsset(go, path); Object.DestroyImmediate(go); return path;
        }
        [Test] public void ScanIsReadOnlyDeduplicatesHierarchyAndReservesGeneratedKeys()
        {
            var parent = Label("Label", "First"); var child = Label("Label", "Second", parent.transform); Label("Numeric", "123$", parent.transform);
            config.Sets[0].AddEntry("Label"); SaveConfig(); byte[] before = File.ReadAllBytes(source);
            var scan = ProjectAutoLocalize.Scan(config, new[] { parent, child }, Options());
            Assert.That(scan.Select(c => c.Key), Is.EqualTo(new[] { "Label_1", "Label_2" })); Assert.That(scan.All(c => c.Target.GetComponent<LocalizedText>() == null), Is.True);
            Assert.That(config.Sets[0].Entries.Count, Is.EqualTo(1)); Assert.That(File.ReadAllBytes(source), Is.EqualTo(before));
        }
        [Test] public void SceneBindingsAndTranslationIdsUndoRedoTogether()
        {
            var go = Label("Greeting", "Hello"); var scan = ProjectAutoLocalize.Scan(config, new[] { go }, Options());
            using var plan = ProjectAutoLocalize.Preview(config, scan); string id = plan.Bindings.Single().EntryId;
            Assert.That(go.GetComponent<LocalizedText>(), Is.Null); Assert.That(config.Sets[0].Entries, Is.Empty); using var record = plan.Apply();
            Assert.That(go.GetComponent<LocalizedText>().Entry.EntryId, Is.EqualTo(id)); Assert.That(config.CreateEntryIndex().Resolve(go.GetComponent<LocalizedText>().Entry, out _), Is.EqualTo(LocalizationReferenceStatus.Found));
            Assert.That(File.ReadAllText(source), Does.Contain("Hello"));
            Undo.PerformUndo(); Assert.That(go.GetComponent<LocalizedText>(), Is.Null); Assert.That(config.Sets[0].Entries, Is.Empty);
            Undo.PerformRedo(); Assert.That(go.GetComponent<LocalizedText>().Entry.EntryId, Is.EqualTo(id)); Assert.That(config.Sets[0].Entries.Single().Id, Is.EqualTo(id));
        }
        [Test] public void ExistingEntryKeepsTranslationAndExistingComponentOptions()
        {
            string id = config.Sets[0].AddEntry("existing").Id; SaveConfig();
            using (var edit = new LocalizationProjectEdit(config, config.Sets[0].Id)) { edit.SetTranslation(id, "en", "Authored"); edit.Commit(); }
            var go = Label("Greeting", "Scene placeholder"); var comp = go.AddComponent<LocalizedText>(); comp.Key = "previous";
            var options = Options(); options.SkipAlreadyLocalized = false;
            var scan = ProjectAutoLocalize.Scan(config, new[] { go }, options); scan[0].Key = "existing";
            using var plan = ProjectAutoLocalize.Preview(config, scan); using var record = plan.Apply();
            Assert.That(comp.Entry.EntryId, Is.EqualTo(id)); Assert.That(File.ReadAllText(source), Does.Contain("Authored").And.Not.Contain("Scene placeholder"));
            Undo.PerformUndo(); Assert.That(comp.Key, Is.EqualTo("previous")); Assert.That(comp.Entry.IsSet, Is.False);
        }
        [UnityTest] public IEnumerator RejectedUndoRestoresSceneBindingWithoutOverwritingExternalText()
        {
            var go = Label("Greeting", "Hello"); using var plan = ProjectAutoLocalize.Preview(config, ProjectAutoLocalize.Scan(config, new[] { go }, Options()));
            using var record = plan.Apply(); string id = go.GetComponent<LocalizedText>().Entry.EntryId;
            byte[] saved = File.ReadAllBytes(source); File.AppendAllText(source, " \n"); byte[] external = File.ReadAllBytes(source);
            LogAssert.Expect(LogType.Error, new Regex("Undo/Redo stopped:")); Undo.PerformUndo(); yield return null;
            Assert.That(go.GetComponent<LocalizedText>(), Is.Not.Null); Assert.That(go.GetComponent<LocalizedText>().Entry.EntryId, Is.EqualTo(id));
            Assert.That(config.Sets[0].Entries.Single().Id, Is.EqualTo(id)); Assert.That(File.ReadAllBytes(source), Is.EqualTo(external));
            File.WriteAllBytes(source, saved); Undo.PerformUndo(); Assert.That(go.GetComponent<LocalizedText>(), Is.Null); Assert.That(config.Sets[0].Entries, Is.Empty);
        }
        [UnityTest] public IEnumerator RejectedRedoRestoresSceneWithoutDanglingBinding()
        {
            var go = Label("Greeting", "Hello"); using var plan = ProjectAutoLocalize.Preview(config, ProjectAutoLocalize.Scan(config, new[] { go }, Options()));
            using var record = plan.Apply(); Undo.PerformUndo(); byte[] saved = File.ReadAllBytes(source);
            File.AppendAllText(source, " \n"); byte[] external = File.ReadAllBytes(source);
            LogAssert.Expect(LogType.Error, new Regex("Undo/Redo stopped:")); Undo.PerformRedo(); yield return null;
            Assert.That(go.GetComponent<LocalizedText>(), Is.Null); Assert.That(config.Sets[0].Entries, Is.Empty); Assert.That(File.ReadAllBytes(source), Is.EqualTo(external));
            File.WriteAllBytes(source, saved); Undo.PerformRedo(); Assert.That(go.GetComponent<LocalizedText>(), Is.Not.Null);
        }
        [UnityTest] public IEnumerator GroupedUndoFailureRestoresEarlierFileStepsAndBothSceneBindings()
        {
            var other = config.AddSet("Other"); config.MainChannel.IncludeSet(other.Id); SaveConfig();
            var first = Label("First", "First text"); var second = Label("Second", "Second text");
            Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
            using var firstPlan = ProjectAutoLocalize.Preview(config, ProjectAutoLocalize.Scan(config, new[] { first }, Options())); using var firstRecord = firstPlan.Apply();
            using var secondPlan = ProjectAutoLocalize.Preview(config, ProjectAutoLocalize.Scan(config, new[] { second }, Options()), setId: other.Id); using var secondRecord = secondPlan.Apply();
            string secondSource = secondPlan.CreatedSourcePath; byte[] secondBytes = File.ReadAllBytes(secondSource);
            Undo.CollapseUndoOperations(group); Undo.IncrementCurrentGroup(); File.AppendAllText(source, " \n"); byte[] external = File.ReadAllBytes(source);
            LogAssert.Expect(LogType.Error, new Regex("Undo/Redo stopped:")); Undo.PerformUndo(); yield return null;
            Assert.That(first.GetComponent<LocalizedText>(), Is.Not.Null); Assert.That(second.GetComponent<LocalizedText>(), Is.Not.Null);
            Assert.That(config.Sets.All(s => s.Entries.Count == 1), Is.True); Assert.That(File.ReadAllBytes(source), Is.EqualTo(external)); Assert.That(File.ReadAllBytes(secondSource), Is.EqualTo(secondBytes));
        }
        [UnityTest] public IEnumerator GroupedRedoFailureRestoresEarlierFileStepsAndBothSceneBindings()
        {
            var other = config.AddSet("Other"); config.MainChannel.IncludeSet(other.Id); SaveConfig();
            string otherSource = root + "/Resources/Other.json";
            using (var edit = new LocalizationProjectEdit(config, other.Id)) { edit.CreateTextSource(other.Id, "en", otherSource); edit.Commit(); }
            var first = Label("First", "First text"); var second = Label("Second", "Second text");
            Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
            using var firstPlan = ProjectAutoLocalize.Preview(config, ProjectAutoLocalize.Scan(config, new[] { first }, Options())); using var firstRecord = firstPlan.Apply();
            using var secondPlan = ProjectAutoLocalize.Preview(config, ProjectAutoLocalize.Scan(config, new[] { second }, Options()), setId: other.Id); using var secondRecord = secondPlan.Apply();
            Undo.CollapseUndoOperations(group); Undo.IncrementCurrentGroup(); Undo.PerformUndo(); byte[] firstBytes = File.ReadAllBytes(source);
            File.AppendAllText(otherSource, " \n"); byte[] external = File.ReadAllBytes(otherSource);
            LogAssert.Expect(LogType.Error, new Regex("Undo/Redo stopped:")); Undo.PerformRedo(); yield return null;
            Assert.That(first.GetComponent<LocalizedText>(), Is.Null); Assert.That(second.GetComponent<LocalizedText>(), Is.Null);
            Assert.That(config.Sets.All(s => s.Entries.Count == 0), Is.True); Assert.That(File.ReadAllBytes(source), Is.EqualTo(firstBytes)); Assert.That(File.ReadAllBytes(otherSource), Is.EqualTo(external));
        }
        [Test] public void TmpUsesSelectedIndependentChannelAndKeepsMainEntryWithTheSameKey()
        {
            string mainId = config.Sets[0].AddEntry("Greeting").Id;
            var set = config.AddSet("Subtitles"); var channel = config.AddChannel("Subtitles", new[] { "ru", "en" }); channel.IncludeSet(set.Id); SaveConfig();
            var go = new GameObject("Greeting", typeof(RectTransform), typeof(TextMeshProUGUI)); go.transform.SetParent(fixture.transform); go.GetComponent<TMP_Text>().text = "Привет";
            var scan = ProjectAutoLocalize.Scan(config, new[] { go }, Options(), channel.Id);
            using var plan = ProjectAutoLocalize.Preview(config, scan, channel.Id); using var record = plan.Apply();
            var component = go.GetComponent<LocalizedText>(); Assert.That(component.Entry.ChannelId, Is.EqualTo(channel.Id)); Assert.That(component.Entry.EntryId, Is.Not.EqualTo(mainId));
            Assert.That(File.ReadAllText(plan.CreatedSourcePath), Does.Contain("Привет")); Assert.That(File.ReadAllText(source), Does.Not.Contain("Привет"));
        }
        [Test] public void ExistingCustomBackendEntryCanBeBoundWithoutAResourcesWriter()
        {
            string id = config.Sets[0].AddEntry("Greeting").Id; config.Sets[0].SetSources("en", new LocalizationSourceReference("custom", "my-table", LocalizationSourceKind.TextTable)); SaveConfig();
            var go = Label("Greeting", "Placeholder"); var scan = ProjectAutoLocalize.Scan(config, new[] { go }, Options()); scan[0].Key = "Greeting";
            using var plan = ProjectAutoLocalize.Preview(config, scan); using var record = plan.Apply(); Assert.That(go.GetComponent<LocalizedText>().Entry.EntryId, Is.EqualTo(id));
            Undo.PerformUndo(); Assert.That(go.GetComponent<LocalizedText>(), Is.Null);
        }
        [Test] public void SourceAndSceneChangesAfterPreviewAreRejected()
        {
            var go = Label("Greeting", "Hello"); var scan = ProjectAutoLocalize.Scan(config, new[] { go }, Options());
            using (var plan = ProjectAutoLocalize.Preview(config, scan))
            { go.GetComponent<Text>().text = "External"; Assert.Throws<InvalidOperationException>(() => plan.Apply()); }
            Assert.That(go.GetComponent<LocalizedText>(), Is.Null);
            scan = ProjectAutoLocalize.Scan(config, new[] { go }, Options()); using var current = ProjectAutoLocalize.Preview(config, scan);
            File.AppendAllText(source, " \n"); byte[] external = File.ReadAllBytes(source);
            Assert.Throws<InvalidOperationException>(() => current.Apply()); Assert.That(go.GetComponent<LocalizedText>(), Is.Null); Assert.That(File.ReadAllBytes(source), Is.EqualTo(external));
        }
        [Test] public void UnregisteredSourceKeyAndContradictoryGeneratedKeysAreNotOverwritten()
        {
            var first = Label("Greeting", "First"); var second = Label("Other", "Second");
            File.WriteAllText(source, "{\"translations\":{\"Greeting\":\"Existing\"}}");
            var scan = ProjectAutoLocalize.Scan(config, new[] { first }, Options());
            Assert.That(Assert.Throws<InvalidOperationException>(() => ProjectAutoLocalize.Preview(config, scan)).Message, Does.Contain("already exists in a source"));
            scan = ProjectAutoLocalize.Scan(config, new[] { first, second }, Options()); foreach (var row in scan) row.Key = "same";
            Assert.Throws<ArgumentException>(() => ProjectAutoLocalize.Preview(config, scan)); Assert.That(config.Sets[0].Entries, Is.Empty);
        }
        [Test] public void PrefabModesAreExplicitAndInstanceOverrideDoesNotChangeAsset()
        {
            string path = Prefab(); byte[] before = File.ReadAllBytes(path); var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset, scene);
            instance.transform.SetParent(fixture.transform, false);
            Assert.That(ProjectAutoLocalize.Scan(config, new[] { instance }, Options()), Is.Empty);
            var scan = ProjectAutoLocalize.Scan(config, new[] { instance }, Options(AutoLocalizePrefabMode.InstanceOverrides));
            using var plan = ProjectAutoLocalize.Preview(config, scan); using var record = plan.Apply();
            Assert.That(instance.GetComponentInChildren<LocalizedText>(), Is.Not.Null); Assert.That(File.ReadAllBytes(path), Is.EqualTo(before));
            Undo.PerformUndo(); Assert.That(instance.GetComponentInChildren<LocalizedText>(), Is.Null);
        }
        [Test] public void PrefabAssetEditUsesSiblingIdentityAndPreservesExistingFileIdsWithDuplicateNames()
        {
            string path = Prefab(true); var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path); byte[] before = File.ReadAllBytes(path);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset.transform.GetChild(0).GetComponent<Text>(), out string guid, out long firstId);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset.transform.GetChild(1).GetComponent<Text>(), out _, out long secondId);
            var scan = ProjectAutoLocalize.Scan(config, new[] { asset }, Options(AutoLocalizePrefabMode.PrefabAssets));
            using var plan = ProjectAutoLocalize.Preview(config, scan); using var record = plan.Apply();
            asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(asset.transform.GetChild(0).GetComponent<LocalizedText>().Entry.EntryId, Is.EqualTo(plan.Bindings[0].EntryId));
            Assert.That(asset.transform.GetChild(1).GetComponent<LocalizedText>().Entry.EntryId, Is.EqualTo(plan.Bindings[1].EntryId));
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset.transform.GetChild(0).GetComponent<Text>(), out string currentGuid, out long currentFirst);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset.transform.GetChild(1).GetComponent<Text>(), out _, out long currentSecond);
            Assert.That(currentGuid, Is.EqualTo(guid)); Assert.That(currentFirst, Is.EqualTo(firstId)); Assert.That(currentSecond, Is.EqualTo(secondId));
            Undo.PerformUndo(); Assert.That(File.ReadAllBytes(path), Is.EqualTo(before)); Assert.That(config.Sets[0].Entries, Is.Empty);
            Undo.PerformRedo(); Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponentsInChildren<LocalizedText>().Length, Is.EqualTo(2));
        }
        [Test] public void ReadOnlyPrefabPreflightPreventsSceneAndTranslationChanges()
        {
            var sceneTarget = Label("Scene", "Scene text"); string path = Prefab(); var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var scan = ProjectAutoLocalize.Scan(config, new[] { sceneTarget, asset }, Options(AutoLocalizePrefabMode.PrefabAssets));
            using var plan = ProjectAutoLocalize.Preview(config, scan); byte[] text = File.ReadAllBytes(source), prefab = File.ReadAllBytes(path);
            var attributes = File.GetAttributes(path); File.SetAttributes(path, attributes | FileAttributes.ReadOnly);
            try { Assert.Catch<Exception>(() => plan.Apply()); } finally { File.SetAttributes(path, attributes); }
            Assert.That(sceneTarget.GetComponent<LocalizedText>(), Is.Null); Assert.That(config.Sets[0].Entries, Is.Empty);
            Assert.That(File.ReadAllBytes(source), Is.EqualTo(text)); Assert.That(File.ReadAllBytes(path), Is.EqualTo(prefab));
        }
        [Test] public void NestedPrefabAssetEditIsAnOverrideInTheSelectedOuterAsset()
        {
            string inner = Prefab(); byte[] innerBefore = File.ReadAllBytes(inner);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(inner), scene); instance.transform.SetParent(fixture.transform);
            var outer = new GameObject("Outer", typeof(RectTransform)); outer.transform.SetParent(fixture.transform); instance.transform.SetParent(outer.transform);
            string path = root + "/Outer.prefab"; PrefabUtility.SaveAsPrefabAsset(outer, path); Object.DestroyImmediate(outer);
            var scan = ProjectAutoLocalize.Scan(config, new[] { AssetDatabase.LoadAssetAtPath<GameObject>(path) }, Options(AutoLocalizePrefabMode.PrefabAssets));
            Assert.That(scan.Single().PrefabPath, Is.EqualTo(path)); using var plan = ProjectAutoLocalize.Preview(config, scan); using var record = plan.Apply();
            Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponentInChildren<LocalizedText>(), Is.Not.Null); Assert.That(File.ReadAllBytes(inner), Is.EqualTo(innerBefore));
        }
        [Test] public void VariantChangesKeepBasePrefabUntouchedAndAddedInstanceObjectsRequireOverrides()
        {
            string basePath = Prefab(); byte[] original = File.ReadAllBytes(basePath);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(basePath), scene); instance.transform.SetParent(fixture.transform);
            string variantPath = root + "/Variant.prefab"; PrefabUtility.SaveAsPrefabAsset(instance, variantPath); Object.DestroyImmediate(instance);
            var variant = AssetDatabase.LoadAssetAtPath<GameObject>(variantPath);
            using var plan = ProjectAutoLocalize.Preview(config, ProjectAutoLocalize.Scan(config, new[] { variant }, Options(AutoLocalizePrefabMode.PrefabAssets))); using var record = plan.Apply();
            Assert.That(File.ReadAllBytes(basePath), Is.EqualTo(original)); Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>(variantPath).GetComponentInChildren<LocalizedText>(), Is.Not.Null);
            instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(basePath), scene); instance.transform.SetParent(fixture.transform); var added = Label("Added", "Added text", instance.transform);
            Assert.Throws<InvalidOperationException>(() => ProjectAutoLocalize.Scan(config, new[] { added }, Options(AutoLocalizePrefabMode.PrefabAssets)));
            Assert.That(ProjectAutoLocalize.Scan(config, new[] { added }, Options(AutoLocalizePrefabMode.InstanceOverrides)).Single().Target, Is.EqualTo(added));
        }
        [Test] public void NativeSaveFailureRollsBackPrefabConfigAndNewJsonWithMetadata()
        {
            string path = Prefab(); byte[] prefab = File.ReadAllBytes(path); var set = config.AddSet("New"); config.MainChannel.IncludeSet(set.Id); SaveConfig();
            using var edit = new LocalizationProjectEdit(config, set.Id); string id = edit.Draft.Sets.Single(s => s.Id == set.Id).AddEntry("new").Id;
            string json = root + "/Resources/" + Path.GetFileName(root) + ".json"; edit.CreateTextSource(set.Id, "en", json); edit.SetTranslation(id, "en", "Pending");
            var method = typeof(LocalizationProjectEdit).GetMethod("CommitWithNativeFiles", BindingFlags.NonPublic | BindingFlags.Instance);
            var contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                Action<object> write = transaction =>
                {
                    transaction.GetType().GetMethod("TouchExternal", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(transaction, new object[] { path });
                    contents.GetComponentInChildren<Text>().text = "Pending native save"; PrefabUtility.SaveAsPrefabAsset(contents, path); AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                    throw new IOException("Injected failure after native save");
                };
                var callback = Delegate.CreateDelegate(method.GetParameters()[1].ParameterType, write.Target, write.Method);
                var failure = Assert.Throws<TargetInvocationException>(() => method.Invoke(edit, new object[] { new Dictionary<string, byte[]> { [path] = prefab }, callback }));
                Assert.That(failure.InnerException, Is.TypeOf<IOException>());
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(prefab)); Assert.That(File.Exists(json) || File.Exists(json + ".meta"), Is.False);
            Assert.That(config.Sets.Single(s => s.Id == set.Id).Entries, Is.Empty); Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponentInChildren<Text>().text, Is.EqualTo("First"));
        }
        [Test] public void MissingSourceCreationIsPreviewedAndCancelledWithoutFiles()
        {
            var set = config.AddSet("New"); config.MainChannel.IncludeSet(set.Id); SaveConfig(); var go = Label("Greeting", "Hello");
            var scan = ProjectAutoLocalize.Scan(config, new[] { go }, Options(), setId: set.Id); string path;
            using (var plan = ProjectAutoLocalize.Preview(config, scan, setId: set.Id)) { path = plan.CreatedSourcePath; Assert.That(path, Is.Not.Null); Assert.That(File.Exists(path), Is.False); }
            Assert.That(File.Exists(path), Is.False); Assert.That(go.GetComponent<LocalizedText>(), Is.Null); Assert.That(config.Sets.Single(s => s.Id == set.Id).Entries, Is.Empty);
        }
    }
}
