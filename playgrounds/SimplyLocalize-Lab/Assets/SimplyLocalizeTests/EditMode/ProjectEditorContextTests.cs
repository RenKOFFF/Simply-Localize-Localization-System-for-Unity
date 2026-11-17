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
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace SimplyLocalize.Tests
{
    public class ProjectEditorContextTests
    {
        private const string Prefix = "SimplyLocalize.ProjectEditor.";
        private string root, entry, source;
        private string[] previousSelection;
        private LocalizationProjectConfig config;
        private GameObject fixture;
        private int undoGroup;
        [SetUp] public void Setup()
        {
            undoGroup = 0; previousSelection = new[] { "Config", "Channel", "Set" }.Select(k => SessionState.GetString(Prefix + k, "")).ToArray();
            root = "Assets/SimplyLocalizeEditorContext_" + Guid.NewGuid().ToString("N"); Directory.CreateDirectory(root); AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            config = LocalizationProjectAuthoring.Create(new[] { "en", "ru" }, "en", "en", root + "/Project.asset", false);
            entry = config.Sets[0].AddEntry("greeting").Id; Save(); source = root + "/Resources/SimplyLocalize/Translations/en/Common.json";
            File.WriteAllText(source, "{\"translations\":{\"greeting\":\"Hello {name}\"}}");
            EditorProjectSelection.Select(config); fixture = new GameObject("EditorContextFixture"); Undo.IncrementCurrentGroup(); undoGroup = Undo.GetCurrentGroup();
        }
        [TearDown] public void Cleanup()
        {
            if (undoGroup > 0) Undo.RevertAllDownToGroup(undoGroup);
            if (fixture != null) Object.DestroyImmediate(fixture); AssetDatabase.DeleteAsset(root);
            string[] keys = { "Config", "Channel", "Set" }; for (int i = 0; i < keys.Length; i++) SessionState.SetString(Prefix + keys[i], previousSelection[i]);
            EditorProjectSelection.Invalidate();
        }
        private void Save() { EditorUtility.SetDirty(config); AssetDatabase.SaveAssetIfDirty(config); EditorProjectSelection.Invalidate(); }
        private T Component<T>() where T : LocalizedComponentBase
        { var go = new GameObject(typeof(T).Name, typeof(RectTransform), typeof(Text)); go.transform.SetParent(fixture.transform); return go.AddComponent<T>(); }
        [Test] public void MetadataSelectionDoesNotReadContentsOrInitializeRuntime()
        {
            config.Sets[0].SetSources("en", new LocalizationSourceReference("unregistered", "table", LocalizationSourceKind.TextTable)); Save();
            var runtime = Localization.Runtime; var context = EditorProjectSelection.Current;
            Assert.That(EditorProjectSelection.GetContext(setId: "").SetId, Is.EqualTo(config.MainChannel.DefaultSetId));
            Assert.That(context.Entries(LocalizationEntryKind.Text).Single().Id, Is.EqualTo(entry)); Assert.That(Localization.Runtime, Is.SameAs(runtime));
            Assert.Throws<InvalidOperationException>(() => { _ = context.Text; });
        }
        [Test] public void SharedReaderRegistrationSupportsMixedProvidersAndDoesNotOwnAssets()
        {
            string provider = "test-" + Guid.NewGuid().ToString("N"); var reader = new Reader();
            config.Sets[0].SetSources("ru", new LocalizationSourceReference(provider, "table", LocalizationSourceKind.TextTable)); Save();
            var context = EditorProjectSelection.Current;
            using (LocalizationEditorReaders.Register(provider, reader))
            {
                Assert.That(context.Text.TryResolve(entry, "ru", out var ru), Is.True); Assert.That(ru.Value, Is.EqualTo("Custom"));
                Assert.That(context.Text.TryResolve(entry, "en", out var en), Is.True); Assert.That(en.Value, Does.StartWith("Hello"));
                Assert.Throws<InvalidOperationException>(() => LocalizationEditorReaders.Register(provider, reader));
            }
            Assert.That(reader.Disposed, Is.False); Assert.Throws<InvalidOperationException>(() => { _ = EditorProjectSelection.Current.Text; });
        }
        [Test] public void AssetAndAppearanceReadersRemainBorrowedAndWrongKindsAreRejected()
        {
            string provider = "test-" + Guid.NewGuid().ToString("N"); var asset = ScriptableObject.CreateInstance<LocalizationAppearance>(); var reader = new Reader { Asset = asset };
            try
            {
                using (LocalizationEditorReaders.Register(provider, assets: reader, appearance: _ => asset))
                {
                    Assert.That(LocalizationEditorReaders.ReadAssets(new(provider, "table", LocalizationSourceKind.AssetTable)).Values["greeting"], Is.SameAs(asset));
                    Assert.That(LocalizationEditorReaders.ReadAppearance(new(provider, "look", LocalizationSourceKind.Appearance)), Is.SameAs(asset));
                    Assert.Throws<ArgumentException>(() => LocalizationEditorReaders.ReadText(new(provider, "look", LocalizationSourceKind.Appearance)));
                }
                Assert.That(asset != null, Is.True); Assert.That(reader.Disposed, Is.False);
            }
            finally { Object.DestroyImmediate(asset); }
        }
        [Test] public void InvalidReaderRegistrationCannotReplaceResources()
        {
            foreach (string id in new[] { "resources", "Resources", "bad provider", "" }) Assert.Throws<ArgumentException>(() => LocalizationEditorReaders.Register(id, new Reader()));
            Assert.Throws<ArgumentException>(() => LocalizationEditorReaders.Register("empty"));
        }
        [Test] public void PreviewReloadReadsExternalJsonAndSelectionFollowsConfigGuid()
        {
            var context = EditorProjectSelection.Current; Assert.That(context.Text.TryResolve(entry, "en", out _), Is.True);
            File.WriteAllText(source, "{\"translations\":{\"greeting\":\"External\"}}"); EditorProjectSelection.Invalidate();
            Assert.That(context.Text.TryResolve(entry, "en", out var cell), Is.True); Assert.That(cell.Value, Is.EqualTo("External"));
            File.WriteAllText(source, "{\"translations\":{\"greeting\":\"Second external edit\"}}"); EditorProjectSelection.Invalidate();
            Assert.That(context.Text.TryResolve(entry, "en", out cell), Is.True); Assert.That(cell.Value, Is.EqualTo("Second external edit"));
            Assert.That(AssetDatabase.MoveAsset(root + "/Project.asset", root + "/Renamed.asset"), Is.Empty); EditorProjectSelection.Invalidate();
            Assert.That(EditorProjectSelection.Config, Is.SameAs(config));
        }
        [Test] public void DeletedExplicitConfigDoesNotSilentlySelectAnotherConfig()
        {
            AssetDatabase.DeleteAsset(root + "/Project.asset"); EditorProjectSelection.Invalidate();
            Assert.That(EditorProjectSelection.Config, Is.Null); Assert.That(EditorProjectSelection.Message, Does.Contain("no longer available"));
        }
        [Test] public void ComponentReferenceKeepsItsChannelAndFollowsRenameAndMove()
        {
            var second = config.AddSet("Second"); var voice = config.AddChannel("Voice", new[] { "ru", "en" }); voice.IncludeSet(second.Id); voice.IncludeSet(config.Sets[0].Id); Save();
            var component = Component<LocalizedText>(); using var serialized = new SerializedObject(component);
            LocalizationEditorBindings.SetEntry(serialized, config, voice.Id, entry);
            Assert.That(LocalizationEditorBindings.ContextFor(component, out var original).SetId, Is.EqualTo(config.Sets[0].Id)); Assert.That(original.Key, Is.EqualTo("greeting"));
            config.MoveEntry(entry, second.Id); second.RenameEntry(entry, "renamed"); Save();
            var context = LocalizationEditorBindings.ContextFor(component, out var resolved);
            Assert.That(context.ChannelId, Is.EqualTo(voice.Id)); Assert.That(context.SetId, Is.EqualTo(second.Id)); Assert.That(resolved.Key, Is.EqualTo("renamed"));
            Assert.That(component.Key, Is.EqualTo("greeting")); Assert.That(EditorProjectSelection.Current.ChannelId, Is.EqualTo(config.MainChannelId));
        }
        [Test] public void MultiObjectBindingUndoAndClearRestoreIdentityAndStringFallback()
        {
            var a = Component<LocalizedText>(); var b = Component<LocalizedText>(); a.Key = "before";
            using var serialized = new SerializedObject(new Object[] { a, b }); LocalizationEditorBindings.SetEntry(serialized, config, config.MainChannelId, entry);
            Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup(); Assert.That(a.Entry.EntryId, Is.EqualTo(entry)); Assert.That(b.Entry.EntryId, Is.EqualTo(entry));
            Undo.PerformUndo(); Assert.That(a.Key, Is.EqualTo("before")); Assert.That(a.Entry.IsSet || b.Entry.IsSet, Is.False); Undo.PerformRedo();
            LocalizationEditorBindings.ClearEntry(serialized); Assert.That(a.Key, Is.Empty); Assert.That(a.ChannelId, Is.Empty); Assert.That(a.SetId, Is.Empty); Assert.That(a.Entry.IsSet, Is.False);
        }
        [Test] public void WrongKindAndUnavailableSetFailBeforeChangingAnyTarget()
        {
            var component = Component<LocalizedText>(); using var serialized = new SerializedObject(component);
            string asset = config.Sets[0].AddEntry("asset", LocalizationEntryKind.Asset).Id; var other = config.AddSet("Other"); string foreign = other.AddEntry("foreign").Id; Save();
            Assert.Throws<ArgumentException>(() => LocalizationEditorBindings.SetEntry(serialized, config, config.MainChannelId, asset));
            Assert.Throws<ArgumentException>(() => LocalizationEditorBindings.SetEntry(serialized, config, config.MainChannelId, foreign)); Assert.That(component.Entry.IsSet, Is.False);
        }
        [Test] public void AssetInspectorUsesAssetIdentityAndRejectsTextWithoutLoadingContent()
        {
            string asset = config.Sets[0].AddEntry("sprite", LocalizationEntryKind.Asset).Id; Save(); var component = Component<LocalizedSprite>();
            using var serialized = new SerializedObject(component); LocalizationEditorBindings.SetEntry(serialized, config, config.MainChannelId, asset);
            Assert.That(LocalizationEditorBindings.AssetType(component.GetType()), Is.EqualTo(typeof(Sprite)));
            Assert.That(LocalizationEditorBindings.ContextFor(component, out var resolved).SetId, Is.EqualTo(config.Sets[0].Id)); Assert.That(resolved.Id, Is.EqualTo(asset));
            Assert.Throws<ArgumentException>(() => LocalizationEditorBindings.SetEntry(serialized, config, config.MainChannelId, entry)); Assert.That(component.Entry.EntryId, Is.EqualTo(asset));
            var editor = UnityEditor.Editor.CreateEditor(component); try { Assert.That(editor, Is.TypeOf<LocalizedAssetEditor>()); } finally { Object.DestroyImmediate(editor); }
        }
        [Test] public void IdenticalKeysInDifferentSetsStaySeparateAndMalformedIdentityIsNotHidden()
        {
            var other = config.AddSet("Other"); config.MainChannel.IncludeSet(other.Id); string second = other.AddEntry("greeting").Id; Save();
            Assert.That(EditorProjectSelection.GetContext(setId: other.Id).Entries(LocalizationEntryKind.Text).Single().Id, Is.EqualTo(second));
            Assert.That(EditorProjectSelection.Current.Entries(LocalizationEntryKind.Text).Single().Id, Is.EqualTo(entry));
            var component = Component<LocalizedText>(); using var serialized = new SerializedObject(component);
            LocalizationEditorBindings.SetEntry(serialized, config, config.MainChannelId, second);
            serialized.FindProperty("_entry").FindPropertyRelative("_entryId").stringValue = "broken"; serialized.ApplyModifiedProperties();
            Assert.Throws<InvalidOperationException>(() => LocalizationEditorBindings.ContextFor(component, out _));
            LocalizationEditorBindings.ClearEntry(serialized); Assert.That(LocalizationEditorBindings.ContextFor(component, out var resolved), Is.Not.Null); Assert.That(resolved, Is.Null);
        }
        [Test] public void ParametersUseParserAndExplicitAddPreservesExistingValuesAndExtras()
        {
            var component = Component<FormattableLocalizedText>(); using var serialized = new SerializedObject(component);
            SetParameters(serialized, ("name", "Player"), ("extra", "Keep")); Undo.IncrementCurrentGroup();
            LocalizationEditorParameters.AddMissing(serialized, "{{escaped}} {name} {count|one|many} {0} {name}");
            var list = serialized.FindProperty("_defaultNamedParams"); Assert.That(list.arraySize, Is.EqualTo(4)); Assert.That(list.GetArrayElementAtIndex(0).FindPropertyRelative("value").stringValue, Is.EqualTo("Player"));
            Assert.That(LocalizationEditorParameters.Names("{{escaped}} {name} {0}"), Is.EqualTo(new[] { "name", "0" }));
            Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup(); Undo.PerformUndo(); serialized.Update(); Assert.That(serialized.FindProperty("_defaultNamedParams").arraySize, Is.EqualTo(2));
        }
        [Test] public void PreviewUsesRuntimeValueParsingAndRejectsHugeIndicesWithoutAllocating()
        {
            var component = Component<FormattableLocalizedText>(); using var serialized = new SerializedObject(component); SetParameters(serialized, ("count", "2.5"));
            var list = serialized.FindProperty("_defaultNamedParams"); string formatted = LocalizationEditorParameters.Format("{count|один|мало|много|дробь}", "ru", list);
            Assert.That(formatted, Is.EqualTo("дробь")); SetParameters(serialized, ("2147483647", "1"));
            Assert.Throws<InvalidOperationException>(() => LocalizationEditorParameters.Format("{0}", "en", serialized.FindProperty("_defaultNamedParams")));
        }
        [Test] public void AddingUnsafeIndexedDefaultsFailsBeforeTouchingExistingParameters()
        {
            var component = Component<FormattableLocalizedText>(); using var serialized = new SerializedObject(component); SetParameters(serialized, ("name", "Keep"));
            string before = EditorJsonUtility.ToJson(component);
            Assert.Throws<InvalidOperationException>(() => LocalizationEditorParameters.AddMissing(serialized, "{new} {1000000000}"));
            Assert.That(EditorJsonUtility.ToJson(component), Is.EqualTo(before));
        }
        [UnityTest] public IEnumerator InspectorRepaintSupportsDerivedComponentsWithoutMutatingParametersOrFiles()
        {
            var component = Component<CustomFormattableText>(); using var serialized = new SerializedObject(component); SetParameters(serialized, ("extra", "Keep"));
            LocalizationEditorBindings.SetEntry(serialized, config, config.MainChannelId, entry); string before = EditorJsonUtility.ToJson(component); byte[] bytes = File.ReadAllBytes(source);
            var editor = UnityEditor.Editor.CreateEditor(component); Assert.That(editor, Is.TypeOf<FormattableLocalizedTextEditor>());
            typeof(LocalizedComponentEditor).GetField("showPreview", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(editor, true);
            var host = ScriptableObject.CreateInstance<InspectorHost>(); host.Inspector = editor; host.position = new Rect(150, 150, 600, 700); host.ShowUtility();
            try { for (int i = 0; i < 8; i++) { host.Repaint(); yield return null; } Assert.That(host.Draws, Is.GreaterThan(0)); Assert.That(EditorJsonUtility.ToJson(component), Is.EqualTo(before)); Assert.That(File.ReadAllBytes(source), Is.EqualTo(bytes)); }
            finally { host.Close(); Object.DestroyImmediate(editor); }
        }
        private static void SetParameters(SerializedObject target, params (string name, string value)[] values)
        {
            target.Update(); var list = target.FindProperty("_defaultNamedParams"); list.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) { var item = list.GetArrayElementAtIndex(i); item.FindPropertyRelative("name").stringValue = values[i].name; item.FindPropertyRelative("value").stringValue = values[i].value; }
            target.ApplyModifiedProperties();
        }
        private sealed class Reader : ILocalizationEditorTextReader, ILocalizationEditorAssetReader, IDisposable
        {
            internal bool Disposed; internal Object Asset;
            LocalizationEditorTextSource ILocalizationEditorTextReader.Read(LocalizationSourceReference source) => new(new Dictionary<string, string> { ["greeting"] = "Custom" });
            LocalizationEditorAssetSource ILocalizationEditorAssetReader.Read(LocalizationSourceReference source) => new(new Dictionary<string, Object> { ["greeting"] = Asset });
            public void Dispose() => Disposed = true;
        }
        private sealed class InspectorHost : EditorWindow
        { internal UnityEditor.Editor Inspector; internal int Draws; private void OnGUI() { if (Inspector != null) { Inspector.OnInspectorGUI(); Draws++; } } }
    }
}
