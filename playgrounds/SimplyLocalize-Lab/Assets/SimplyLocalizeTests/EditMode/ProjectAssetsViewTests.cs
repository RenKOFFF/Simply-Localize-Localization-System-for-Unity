using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using SimplyLocalize.Editor;
using SimplyLocalize.Editor.Data;
using SimplyLocalize.Editor.Windows;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace SimplyLocalize.Tests
{
    public class ProjectAssetsViewTests
    {
        private string root, entry;
        private LocalizationProjectConfig config;
        private Texture2D texture;
        private EditorWindow host;
        private ProjectAssetsView view;
        private HashSet<int> priorUndo;
        [SetUp] public void Setup()
        {
            root = "Assets/SimplyLocalizeAssetView_" + Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(root); AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            config = LocalizationProjectAuthoring.Create(new[] { "en", "ru" }, "en", "en", root + "/Project.asset", false);
            entry = config.Sets[0].AddEntry("image", LocalizationEntryKind.Asset).Id; SaveConfig();
            LocalizationProjectAuthoring.CreateAssetTable(config, config.Sets[0].Id, "en");
            LocalizationProjectAuthoring.CreateAssetTable(config, config.Sets[0].Id, "ru");
            texture = new Texture2D(1, 1) { name = "Picture" }; AssetDatabase.CreateAsset(texture, root + "/Picture.asset");
            Table("en").Set("image", texture); Save(Table("en"));
            priorUndo = new HashSet<int>(Resources.FindObjectsOfTypeAll<LocalizationProjectUndo>().Select(r => r.GetInstanceID()));
            host = ScriptableObject.CreateInstance<AssetHost>(); host.position = new Rect(100, 100, 900, 700); host.ShowUtility(); Open();
        }
        [TearDown] public void Cleanup()
        {
            view?.Dispose(); if (host != null) host.Close();
            foreach (var record in Resources.FindObjectsOfTypeAll<LocalizationProjectUndo>().Where(r => !priorUndo.Contains(r.GetInstanceID()))) record.Dispose();
            AssetDatabase.DeleteAsset(root);
        }
        private void Open(ILocalizationEditorAssetReader reader = null)
        { view?.Dispose(); host.rootVisualElement.Clear(); view = new ProjectAssetsView(config, reader: reader); host.rootVisualElement.Add(view); }
        private static void Save(Object asset) { EditorUtility.SetDirty(asset); AssetDatabase.SaveAssetIfDirty(asset); }
        private void SaveConfig() => Save(config);
        private LocalizationAssetTable Table(string code) => AssetDatabase.LoadAssetAtPath<LocalizationAssetTable>(new ResourcesEditorTextReader().ResolveSourceAssetPath(
            config.Sets[0].Sources.Single(s => s.Language == code).Sources.First(s => s.Kind == LocalizationSourceKind.AssetTable)));
        private T Field<T>(string name) where T : VisualElement => host.rootVisualElement.Q<T>(name);
        private void Click(string name) { var button = Field<Button>(name); Assert.That(button, Is.Not.Null); using var submit = NavigationSubmitEvent.GetPooled(); submit.target = button; button.SendEvent(submit); }
        [Test] public void UiSavesNativeReferencesAndRenamesWithStableIdentityAndUndo()
        {
            view.SelectEntry(entry); Field<TextField>("assetKey").value = "picture"; Field<ObjectField>("asset_ru").value = texture;
            Assert.That(Table("ru").HasKey("picture"), Is.False); Assert.That(view.SaveChanges(), Is.True);
            Assert.That(Table("ru").Get("picture") == texture, Is.True); Assert.That(config.Sets[0].Entries.Single().Id, Is.EqualTo(entry));
            Undo.PerformUndo(); Assert.That(Table("ru").HasKey("picture"), Is.False); Assert.That(Field<TextField>("assetKey").value, Is.EqualTo("image"));
            Undo.PerformRedo(); Assert.That(Field<ObjectField>("asset_ru").value == texture, Is.True);
        }
        [Test] public void TypedPreviewUsesFallbackWithoutWritingDirectBindings()
        {
            var wrongType = ScriptableObject.CreateInstance<LocalizationAppearance>(); AssetDatabase.CreateAsset(wrongType, root + "/WrongType.asset");
            Table("ru").Set("image", wrongType); Save(Table("ru")); byte[] before = File.ReadAllBytes(AssetDatabase.GetAssetPath(Table("ru")));
            view.SelectEntry(entry); Field<DropdownField>("assetPreviewType").value = "Texture";
            Assert.That(Field<Label>("assetResolved_ru").text, Does.Contain("Fallback · en").And.Contain("Texture2D"));
            Assert.That(Field<ObjectField>("asset_ru").value == wrongType, Is.True); Assert.That(view.HasPendingChanges, Is.False);
            Assert.That(File.ReadAllBytes(AssetDatabase.GetAssetPath(Table("ru"))), Is.EqualTo(before));
        }
        [Test] public void ExternalChangeBlocksSaveAndRetainsDraftUntilExplicitDiscard()
        {
            view.SelectEntry(entry); Field<ObjectField>("asset_ru").value = texture;
            Table("ru").Set("external", texture); Save(Table("ru"));
            Assert.That(view.SaveChanges(), Is.False); Assert.That(Field<ObjectField>("asset_ru").value == texture, Is.True);
            Assert.That(view.Reload(), Is.False); Assert.That(Table("ru").HasKey("image"), Is.False);
            Assert.That(view.Reload(true), Is.True); Assert.That(Table("ru").Get("external") == texture, Is.True);
        }
        [Test] public void EmptyValueAndRemovingDirectEntryAreDifferentAndBothUndoable()
        {
            view.SelectEntry(entry); Click("emptyAsset_ru"); Assert.That(view.SaveChanges(), Is.True);
            Assert.That(Table("ru").HasKey("image"), Is.True); Assert.That(Table("ru").Get("image"), Is.Null);
            Assert.That(Field<Label>("assetResolved_ru").text, Does.Contain("Fallback · en"));
            Click("removeAsset_ru"); Assert.That(view.SaveChanges(), Is.True); Assert.That(Table("ru").HasKey("image"), Is.False);
            Undo.PerformUndo(); Assert.That(Table("ru").HasKey("image"), Is.True); Assert.That(Table("ru").Get("image"), Is.Null);
        }
        [Test] public void CorrectedAmbiguousSaveDoesNotPublishTheRevertedFirstValue()
        {
            LocalizationProjectAuthoring.CreateAssetTable(config, config.Sets[0].Id, "ru"); view.Reload(); view.SelectEntry(entry);
            Field<ObjectField>("asset_en").value = null; Field<ObjectField>("asset_ru").value = texture;
            Assert.That(view.SaveChanges(), Is.False); Field<ObjectField>("asset_en").value = texture;
            string address = config.Sets[0].Sources.Single(s => s.Language == "ru").Sources.First(s => s.Kind == LocalizationSourceKind.AssetTable).Address;
            Field<DropdownField>("assetDestination_ru").value = address; Assert.That(view.SaveChanges(), Is.True);
            Assert.That(Table("en").Get("image") == texture, Is.True); Assert.That(Table("ru").Get("image") == texture, Is.True);
        }
        [Test] public void RegisterExternalKeyAndDeleteDefinitionKeepAssetFile()
        {
            Table("en").Set("external", texture); Save(Table("en")); view.Reload();
            Assert.That(Field<ListView>("unregisteredAssets").itemsSource.Count, Is.EqualTo(1)); Assert.That(view.AddEntry("external"), Is.True);
            Assert.That(Field<ObjectField>("asset_en").value == texture, Is.True); Assert.That(view.RemoveEntry(), Is.True);
            Assert.That(Table("en").HasKey("external"), Is.False); Assert.That(File.Exists(root + "/Picture.asset"), Is.True);
            Undo.PerformUndo(); Assert.That(Table("en").Get("external") == texture, Is.True);
        }
        [Test] public void CreateTableFromMissingLanguagePreservesExistingSourcesAndPendingInput()
        {
            config.Sets[0].SetSources("ru", config.Sets[0].Sources.Single(s => s.Language == "ru").Sources.Where(s => s.Kind != LocalizationSourceKind.AssetTable).ToArray()); SaveConfig();
            view.Reload(); view.SelectEntry(entry); Click("createAssetTable_ru");
            Assert.That(Table("ru"), Is.Not.Null); Assert.That(Table("ru").Count, Is.Zero);
            Assert.That(config.Sets[0].Sources.Single(s => s.Language == "ru").Sources.Any(s => s.Kind == LocalizationSourceKind.TextTable), Is.True);
            Field<ObjectField>("asset_ru").value = texture; Assert.That(view.CreateTable("en"), Is.False);
            Assert.That(view.AddEntry("other"), Is.False); Assert.Throws<InvalidOperationException>(() => view.SelectEntry(entry));
            Assert.That(Field<ObjectField>("asset_ru").value == texture, Is.True);
        }
        [Test] public void CustomReaderIsPreviewOnlyAndKeepsBorrowedObjectsAlive()
        {
            foreach (string code in new[] { "en", "ru" }) config.Sets[0].SetSources(code, new LocalizationSourceReference("game", code, LocalizationSourceKind.AssetTable)); SaveConfig();
            Open(new Reader(texture)); view.SelectEntry(entry);
            Assert.That(Field<ObjectField>("asset_en").value == texture, Is.True); Assert.That(Field<ObjectField>("asset_en").enabledSelf, Is.False);
            Field<ObjectField>("asset_en").value = null; Assert.That(view.HasPendingChanges, Is.False);
            Assert.That(view.AddEntry("new"), Is.True); view.Dispose(); Assert.That(texture != null, Is.True);
        }
        [Test] public void MovePickerKeepsEntryIdentityAndOnlyWritesChosenDestination()
        {
            var target = config.AddSet("Dialogue"); string targetId = target.Id; SaveConfig();
            LocalizationProjectAuthoring.CreateAssetTable(config, targetId, "en");
            LocalizationProjectAuthoring.CreateAssetTable(config, targetId, "en");
            target = config.Sets.Single(s => s.Id == targetId);
            var sources = target.Sources.Single(s => s.Language == "en").Sources.Where(s => s.Kind == LocalizationSourceKind.AssetTable).ToArray();
            var index = new ResourcesEditorTextReader();
            var first = AssetDatabase.LoadAssetAtPath<LocalizationAssetTable>(index.ResolveSourceAssetPath(sources[0]));
            var second = AssetDatabase.LoadAssetAtPath<LocalizationAssetTable>(index.ResolveSourceAssetPath(sources[1]));
            view.Reload(); view.SelectEntry(entry); Field<DropdownField>("moveAssetDestination_en").value = sources[1].Address; Click("moveAsset");
            Assert.That(config.Sets[0].Entries, Is.Empty); Assert.That(config.Sets.Single(s => s.Id == targetId).Entries.Single().Id, Is.EqualTo(entry));
            Assert.That(first.HasKey("image"), Is.False); Assert.That(second.Get("image") == texture, Is.True);
            Assert.That(config.MainChannel.SetIds, Does.Not.Contain(targetId)); Assert.That(Table("en").HasKey("image"), Is.False);
            Undo.PerformUndo(); Assert.That(config.Sets[0].Entries.Single().Id, Is.EqualTo(entry)); Assert.That(Table("en").Get("image") == texture, Is.True);
        }
        [Test] public void AmbiguousExternalKeyCannotBecomeABrokenRegisteredEntry()
        {
            Table("en").Set("external", texture); Save(Table("en"));
            LocalizationProjectAuthoring.CreateAssetTable(config, config.Sets[0].Id, "en");
            var source = config.Sets[0].Sources.Single(s => s.Language == "en").Sources.Last(s => s.Kind == LocalizationSourceKind.AssetTable);
            var other = AssetDatabase.LoadAssetAtPath<LocalizationAssetTable>(new ResourcesEditorTextReader().ResolveSourceAssetPath(source));
            other.Set("external", texture); Save(other); view.Reload();
            Assert.That(view.AddEntry("external"), Is.False); Assert.That(config.Sets[0].Entries.Any(e => e.Key == "external"), Is.False);
            Assert.That(Field<HelpBox>("assetMessage").text, Does.Contain("Duplicate asset key"));
        }
        [UnityTest] public IEnumerator ThousandsOfKeysUseVirtualizedRowsAndSearchDoesNotChangeRuntimeDefaults()
        {
            for (int i = 0; i < 2500; i++) config.Sets[0].AddEntry("asset_" + i.ToString("D4"), LocalizationEntryKind.Asset); SaveConfig(); view.Reload();
            yield return null; yield return null;
            var list = Field<ListView>("assetEntries"); Assert.That(list.itemsSource.Count, Is.EqualTo(2501)); Assert.That(list.Query<Label>().ToList().Count, Is.LessThan(100));
            Field<ToolbarSearchField>("assetSearch").value = "asset_2499"; Assert.That(list.itemsSource.Count, Is.EqualTo(1)); Assert.That(config.MainChannel.DefaultLanguage, Is.EqualTo("en"));
        }
        private sealed class Reader : ILocalizationEditorAssetReader
        {
            private readonly Object asset;
            internal Reader(Object asset) { this.asset = asset; }
            public LocalizationEditorAssetSource Read(LocalizationSourceReference source) => new(new Dictionary<string, Object> { ["image"] = asset });
        }
        private sealed class AssetHost : EditorWindow { }
    }
}
