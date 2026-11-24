using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using SimplyLocalize.Components;
using SimplyLocalize.Editor;
using SimplyLocalize.Editor.Data;
using SimplyLocalize.Editor.Inspectors;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace SimplyLocalize.Tests
{
    public class ProjectEditorFieldTests
    {
        private const string Prefix = "SimplyLocalize.ProjectEditor.";
        private string root, entry, source;
        private string[] previous;
        private LocalizationProjectConfig config;
        private FieldFixture first, second;
        private int undoGroup;
        private GameObject go;
        [SetUp] public void Setup()
        {
            previous = new[] { "Config", "Channel", "Set" }.Select(k => SessionState.GetString(Prefix + k, "")).ToArray();
            root = "Assets/SimplyLocalizeEditorFields_" + Guid.NewGuid().ToString("N"); Directory.CreateDirectory(root); AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            config = LocalizationProjectAuthoring.Create(new[] { "en", "ru" }, "en", "en", root + "/Project.asset", false);
            entry = config.Sets[0].AddEntry("menu").Id; Save(); source = root + "/Resources/SimplyLocalize/Translations/en/Common.json";
            File.WriteAllText(source, "{\"translations\":{\"menu\":\"Play\"}}");
            EditorProjectSelection.Select(config); first = ScriptableObject.CreateInstance<FieldFixture>(); second = ScriptableObject.CreateInstance<FieldFixture>();
            Undo.IncrementCurrentGroup(); undoGroup = Undo.GetCurrentGroup();
        }
        [TearDown] public void Cleanup()
        {
            if (undoGroup > 0) Undo.RevertAllDownToGroup(undoGroup);
            if (go != null) Object.DestroyImmediate(go); Object.DestroyImmediate(first); Object.DestroyImmediate(second); AssetDatabase.DeleteAsset(root);
            string[] names = { "Config", "Channel", "Set" }; for (int i = 0; i < names.Length; i++) SessionState.SetString(Prefix + names[i], previous[i]); EditorProjectSelection.Invalidate();
        }
        private void Save() { EditorUtility.SetDirty(config); AssetDatabase.SaveAssetIfDirty(config); EditorProjectSelection.Invalidate(); }
        private static Action<string, string> Capture(SerializedProperty property, bool reference) =>
            (Action<string, string>)typeof(LocalizationEditorFields).GetMethod("CaptureAssignment", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { property, reference });

        [Test] public void StringKeysRemainInMainDefaultSetWhenWindowSelectsIndependentChannel()
        {
            var other = config.AddSet("Other"); other.AddEntry("voice"); other.AddEntry("menu"); var voice = config.AddChannel("Voice", new[] { "en" }); voice.IncludeSet(other.Id); Save();
            EditorProjectSelection.Select(config, voice.Id, other.Id);
            Assert.That(LocalizationEditorFields.StringContext.ChannelId, Is.EqualTo(config.MainChannelId));
            Assert.That(LocalizationEditorFields.StringEntries.Select(e => e.Id), Is.EqualTo(new[] { entry }));
            using var serialized = new SerializedObject(first);
            Assert.Throws<ArgumentException>(() => LocalizationEditorFields.SetStringKey(serialized.FindProperty("Key"), "voice")); Assert.That(first.Key, Is.Null.Or.Empty);
            LocalizationEditorFields.SetStringKey(serialized.FindProperty("Key"), "menu"); Assert.That(first.Key, Is.EqualTo("menu"));
        }
        [Test] public void UnknownKeyIsRejectedWithoutCreatingDataAndClearUsesOrdinaryUndo()
        {
            byte[] before = File.ReadAllBytes(source); string definition = EditorJsonUtility.ToJson(config);
            using var serialized = new SerializedObject(new Object[] { first, second });
            Assert.Throws<ArgumentException>(() => LocalizationEditorFields.SetStringKey(serialized.FindProperty("Key"), "unregistered"));
            Assert.That(File.ReadAllBytes(source), Is.EqualTo(before)); Assert.That(EditorJsonUtility.ToJson(config), Is.EqualTo(definition));
            LocalizationEditorFields.SetStringKey(serialized.FindProperty("Key"), "menu"); Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup();
            Assert.That(first.Key, Is.EqualTo("menu")); Assert.That(second.Key, Is.EqualTo("menu"));
            Undo.PerformUndo(); Assert.That(first.Key, Is.Null.Or.Empty); Assert.That(second.Key, Is.Null.Or.Empty); Undo.PerformRedo(); serialized.Update();
            LocalizationEditorFields.SetStringKey(serialized.FindProperty("Key"), ""); Assert.That(first.Key, Is.Empty); Assert.That(second.Key, Is.Empty);
        }
        [Test] public void GenericReferencesKeepStableIdsAcrossRenameAndMoveWithMultiObjectUndo()
        {
            using var serialized = new SerializedObject(new Object[] { first, second });
            LocalizationEditorFields.SetReference(serialized.FindProperty("Reference"), config.MainChannelId, entry); Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup();
            Assert.That(first.Reference.EntryId, Is.EqualTo(entry)); Assert.That(second.Reference.EntryId, Is.EqualTo(entry));
            Undo.PerformUndo(); Assert.That(first.Reference.IsSet || second.Reference.IsSet, Is.False); Undo.PerformRedo();
            var other = config.AddSet("Other"); config.MainChannel.IncludeSet(other.Id); config.MoveEntry(entry, other.Id); other.RenameEntry(entry, "renamed"); Save();
            Assert.That(config.CreateEntryIndex().Resolve(first.Reference, out var location), Is.EqualTo(LocalizationReferenceStatus.Found)); Assert.That(location.SetId, Is.EqualTo(other.Id)); Assert.That(location.Key, Is.EqualTo("renamed"));
        }
        [Test] public void ComponentReferenceDrawerChecksKindAndClearAlsoClearsStringFallback()
        {
            go = new GameObject("FieldBinding", typeof(RectTransform), typeof(UnityEngine.UI.Image)); var component = go.AddComponent<LocalizedSprite>();
            string asset = config.Sets[0].AddEntry("icon", LocalizationEntryKind.Asset).Id; Save();
            using var serialized = new SerializedObject(component); var property = serialized.FindProperty("_entry");
            Assert.That(LocalizationEditorFields.RequiredKind(property), Is.EqualTo(LocalizationEntryKind.Asset));
            Assert.Throws<ArgumentException>(() => LocalizationEditorFields.SetReference(property, config.MainChannelId, entry)); Assert.That(component.Entry.IsSet, Is.False);
            LocalizationEditorFields.SetReference(property, config.MainChannelId, asset); Assert.That(component.Key, Is.EqualTo("icon"));
            LocalizationEditorFields.SetReference(serialized.FindProperty("_entry"), null, null); Assert.That(component.Entry.IsSet, Is.False); Assert.That(component.Key, Is.Empty); Assert.That(component.ChannelId, Is.Empty); Assert.That(component.SetId, Is.Empty);
        }
        [Test] public void PartialOrUnavailableReferenceCannotOverwriteExistingField()
        {
            var other = config.AddSet("Other"); string foreign = other.AddEntry("foreign").Id; Save();
            using var serialized = new SerializedObject(first); var property = serialized.FindProperty("Reference"); LocalizationEditorFields.SetReference(property, config.MainChannelId, entry);
            foreach (var invalid in new[] { (config.MainChannelId, ""), ("bad", entry), (config.MainChannelId, foreign) })
                Assert.Throws<ArgumentException>(() => LocalizationEditorFields.SetReference(property, invalid.Item1, invalid.Item2));
            Assert.That(first.Reference.EntryId, Is.EqualTo(entry));
        }
        [Test] public void PopupUsesFreshSerializedObjectAndRejectsChangedFieldInAnySelectedTarget()
        {
            Action<string, string> assign;
            using (var serialized = new SerializedObject(new Object[] { first, second })) assign = Capture(serialized.FindProperty("Reference"), true);
            second.Reference = new LocalizationEntryReference(config.MainChannelId, entry);
            Assert.Throws<InvalidOperationException>(() => assign(config.MainChannelId, entry)); Assert.That(first.Reference.IsSet, Is.False);
            second.Reference = default;
            assign(config.MainChannelId, entry); Assert.That(first.Reference.EntryId, Is.EqualTo(entry)); Assert.That(second.Reference.EntryId, Is.EqualTo(entry));
        }
        [Test] public void PopupRejectsRetargetedArrayAndConfigSwitch()
        {
            first.References = new[] { default(LocalizationEntryReference), new LocalizationEntryReference(config.MainChannelId, entry) };
            using var serialized = new SerializedObject(first); var assign = Capture(serialized.FindProperty("References").GetArrayElementAtIndex(0), true);
            Array.Reverse(first.References); Assert.Throws<InvalidOperationException>(() => assign(null, null)); Assert.That(first.References[0].IsSet, Is.True);
            var key = Capture(serialized.FindProperty("Key"), false);
            SessionState.SetString(Prefix + "Config", "missing"); EditorProjectSelection.Invalidate();
            Assert.Throws<InvalidOperationException>(() => key("menu", null)); Assert.That(first.Key, Is.Null.Or.Empty);
        }
        [Test] public void PopupRejectsRemovedEntryButFollowsRenamedIdentity()
        {
            using var serialized = new SerializedObject(first); var assign = Capture(serialized.FindProperty("Reference"), true);
            config.Sets[0].RenameEntry(entry, "newName"); Save(); assign(config.MainChannelId, entry); Assert.That(first.Reference.EntryId, Is.EqualTo(entry));
            assign = Capture(serialized.FindProperty("Reference"), true); config.MainChannel.RemoveSet(config.Sets[0].Id); Save();
            Assert.Throws<ArgumentException>(() => assign(config.MainChannelId, entry)); Assert.That(first.Reference.EntryId, Is.EqualTo(entry));
        }
        [Test] public void ReferenceSearchDistinguishesIdenticalKeysAndKindsWithoutReadingContent()
        {
            var set = config.AddSet("Other"); string other = set.AddEntry("menu").Id; string asset = set.AddEntry("icon", LocalizationEntryKind.Asset).Id;
            var voice = config.AddChannel("Voice", new[] { "en" }); voice.IncludeSet(set.Id);
            set.SetSources("en", new LocalizationSourceReference("unregistered", "absent", LocalizationSourceKind.TextTable)); Save();
            var search = ScriptableObject.CreateInstance<LocalizationReferenceSearchWindow>();
            try
            {
                string chosen = null; search.Init(config, LocalizationEntryKind.Text, (c, e) => chosen = c + ":" + e);
                var tree = search.CreateSearchTree(new SearchWindowContext(Vector2.zero)); var refs = tree.Where(t => t.userData is LocalizationEntryReference r && r.IsSet).ToArray();
                Assert.That(refs.Select(t => ((LocalizationEntryReference)t.userData).EntryId), Is.EquivalentTo(new[] { entry, other }));
                search.OnSelectEntry(refs.Single(t => ((LocalizationEntryReference)t.userData).EntryId == other), default); Assert.That(chosen, Is.EqualTo(voice.Id + ":" + other));
                Assert.That(search.OnSelectEntry(tree[0], default), Is.False);
                search.Init(config, null, (_, _) => { }); Assert.That(search.CreateSearchTree(default).Any(t => t.userData is LocalizationEntryReference r && r.EntryId == asset), Is.True);
            }
            finally { Object.DestroyImmediate(search); }
        }
        [Test] public void KeySearchPreservesPrefixKeysAndLiteralAddPrefixAndIgnoresGroups()
        {
            var search = ScriptableObject.CreateInstance<KeySearchWindow>();
            try
            {
                string chosen = null; var keys = new List<string> { "menu", "menu/play", "+ Add: literal", "menu/settings/video" };
                search.Init(keys, key => chosen = key); var tree = search.CreateSearchTree(default);
                Assert.That(tree.Where(t => t.userData is string s && s.Length > 0).Select(t => (string)t.userData), Is.EquivalentTo(keys));
                search.OnSelectEntry(tree.Single(t => Equals(t.userData, "+ Add: literal")), default); Assert.That(chosen, Is.EqualTo("+ Add: literal"));
                Assert.That(search.OnSelectEntry(tree[0], default), Is.False); Assert.That(chosen, Is.EqualTo("+ Add: literal"));
                search.Init(null, key => chosen = key, "new"); tree = search.CreateSearchTree(default); search.OnSelectEntry(tree[2], default); Assert.That(chosen, Is.EqualTo("new"));
            }
            finally { Object.DestroyImmediate(search); }
        }
        [Test] public void NoneClearsReferencesEvenWithoutASelectedConfig()
        {
            first.Reference = new LocalizationEntryReference(config.MainChannelId, entry);
            SessionState.SetString(Prefix + "Config", "missing"); EditorProjectSelection.Invalidate();
            using var serialized = new SerializedObject(first); LocalizationEditorFields.SetReference(serialized.FindProperty("Reference"), null, null);
            Assert.That(first.Reference.IsSet, Is.False);
            var picker = ScriptableObject.CreateInstance<LocalizationReferenceSearchWindow>();
            try { picker.Init(null, null, (_, _) => { }); Assert.That(picker.CreateSearchTree(default).Count, Is.EqualTo(2)); }
            finally { Object.DestroyImmediate(picker); }
        }
        [UnityTest] public IEnumerator ActualDrawersRepaintNestedFieldsAndMixedSelectionWithoutWritingData()
        {
            first.Key = "menu"; first.Preview = "menu"; first.Reversed = "menu"; first.Reference = new LocalizationEntryReference(config.MainChannelId, entry);
            first.References = new[] { first.Reference, default }; string before = EditorJsonUtility.ToJson(first); byte[] data = File.ReadAllBytes(source);
            var editor = UnityEditor.Editor.CreateEditor(first); editor.serializedObject.FindProperty("Reference").isExpanded = true;
            var list = editor.serializedObject.FindProperty("References"); list.isExpanded = true; list.GetArrayElementAtIndex(0).isExpanded = true;
            var host = ScriptableObject.CreateInstance<FieldHost>(); host.Inspector = editor; host.position = new Rect(200, 200, 600, 700); host.ShowUtility();
            try
            {
                for (int i = 0; i < 8; i++) { host.Repaint(); yield return null; }
                Assert.That(host.Draws, Is.GreaterThan(0)); Assert.That(EditorJsonUtility.ToJson(first), Is.EqualTo(before)); Assert.That(File.ReadAllBytes(source), Is.EqualTo(data));
                Object.DestroyImmediate(editor); editor = UnityEditor.Editor.CreateEditor(new Object[] { first, second }); host.Inspector = editor;
                for (int i = 0; i < 4; i++) { host.Repaint(); yield return null; }
                Assert.That(first.Key, Is.EqualTo("menu")); Assert.That(second.Key, Is.Null.Or.Empty);
            }
            finally { host.Close(); Object.DestroyImmediate(editor); }
        }
        public sealed class FieldFixture : ScriptableObject
        {
            [LocalizationKey, LocalizationPreview(true)] public string Key;
            [LocalizationPreview] public string Preview;
            [LocalizationPreview, LocalizationKey] public string Reversed;
            public LocalizationEntryReference Reference;
            public LocalizationEntryReference[] References;
        }
        private sealed class FieldHost : EditorWindow
        { internal UnityEditor.Editor Inspector; internal int Draws; private void OnGUI() { if (Inspector != null) { Inspector.OnInspectorGUI(); Draws++; } } }
    }
}
