using System;
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
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace SimplyLocalize.Tests
{
    public class ProjectDraftRecoveryTests
    {
        private string root, textId, assetId;
        private LocalizationProjectConfig config;
        private Texture2D texture;
        private EditorWindow host;
        private ProjectTranslationsView text;
        private ProjectAssetsView assets;
        private ProjectAppearanceView appearance;
        private readonly List<ProjectViewDraft> snapshots = new();
        private HashSet<int> priorUndo;
        private ProjectNotesTestScope notes;
        [SetUp] public void Setup()
        {
            root = "Assets/SimplyLocalizeDraft_" + Guid.NewGuid().ToString("N"); Directory.CreateDirectory(root); AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            config = LocalizationProjectAuthoring.Create(new[] { "en", "ru" }, "en", "en", root + "/Project.asset", false); notes = new ProjectNotesTestScope(config);
            textId = config.Sets[0].AddEntry("greeting").Id; assetId = config.Sets[0].AddEntry("picture", LocalizationEntryKind.Asset).Id; Save(config);
            File.WriteAllText(JsonPath("en"), "{\"translations\":{\"greeting\":\"Hello\"}}");
            File.WriteAllText(JsonPath("ru"), "{\"translations\":{\"greeting\":\"Привет\"}}");
            LocalizationProjectAuthoring.CreateAssetTable(config, config.Sets[0].Id, "en"); LocalizationProjectAuthoring.CreateAssetTable(config, config.Sets[0].Id, "ru");
            LocalizationProjectAuthoring.CreateAppearance(config, "en"); texture = new Texture2D(1, 1); AssetDatabase.CreateAsset(texture, root + "/Picture.asset");
            Table("en").Set("picture", texture); Save(Table("en"));
            priorUndo = Resources.FindObjectsOfTypeAll<LocalizationProjectUndo>().Select(x => x.GetInstanceID()).ToHashSet();
            host = ScriptableObject.CreateInstance<DraftHost>(); host.position = new Rect(120, 120, 900, 700); host.ShowUtility();
        }
        [TearDown] public void Cleanup()
        {
            CloseViews(); if (host != null) host.Close(); foreach (var snapshot in snapshots) snapshot?.Dispose(); snapshots.Clear();
            foreach (var record in Resources.FindObjectsOfTypeAll<LocalizationProjectUndo>().Where(r => !priorUndo.Contains(r.GetInstanceID()))) record.Dispose();
            notes?.Dispose(); AssetDatabase.DeleteAsset(root);
        }
        private void CloseViews() { text?.Dispose(); text = null; assets?.Dispose(); assets = null; appearance?.Dispose(); appearance = null; host?.rootVisualElement.Clear(); }
        private string JsonPath(string language) => root + "/Resources/SimplyLocalize/Translations/" + language + "/Common.json";
        private static void Save(Object asset) { EditorUtility.SetDirty(asset); AssetDatabase.SaveAssetIfDirty(asset); }
        private LocalizationAssetTable Table(string language) => AssetDatabase.LoadAssetAtPath<LocalizationAssetTable>(new ResourcesEditorTextReader().ResolveSourceAssetPath(config.Sets[0].Sources.Single(s => s.Language == language).Sources.First(s => s.Kind == LocalizationSourceKind.AssetTable)));
        private LocalizationAppearance Appearance() => AssetDatabase.LoadAssetAtPath<LocalizationAppearance>(new ResourcesEditorTextReader().ResolveSourceAssetPath(config.Languages[0].Appearance));
        private void OpenText() { CloseViews(); text = new ProjectTranslationsView(config); host.rootVisualElement.Add(text); text.SelectEntry(textId); }
        private void OpenAssets() { CloseViews(); assets = new ProjectAssetsView(config); host.rootVisualElement.Add(assets); assets.SelectEntry(assetId); }
        private void OpenAppearance() { CloseViews(); appearance = new ProjectAppearanceView(config); host.rootVisualElement.Add(appearance); }
        private ProjectViewDraft Keep(ProjectViewDraft draft) { snapshots.Add(draft); return draft; }
        private T Field<T>(string name) where T : VisualElement => host.rootVisualElement.Q<T>(name);
        private void Click(string name) { var button = Field<Button>(name); using var submit = NavigationSubmitEvent.GetPooled(); submit.target = button; button.SendEvent(submit); }
        [Test] public void TextDraftRestoresKeyValuesNotesAndRemovalWithoutWritingUntilSave()
        {
            OpenText(); Field<TextField>("entryKey").value = "welcome"; Field<TextField>("translation_en").value = "My draft"; Field<TextField>("entryDescription").value = "Keep this note"; Click("removeTranslation_ru");
            var state = Keep(text.CaptureDraft()); byte[] before = File.ReadAllBytes(JsonPath("en")); OpenText(); text.RestoreDraft(state);
            Assert.That(Field<TextField>("entryKey").value, Is.EqualTo("welcome")); Assert.That(Field<TextField>("translation_en").value, Is.EqualTo("My draft")); Assert.That(Field<TextField>("entryDescription").value, Is.EqualTo("Keep this note"));
            Assert.That(File.ReadAllBytes(JsonPath("en")), Is.EqualTo(before)); Assert.That(text.HasPendingChanges, Is.True); Assert.That(text.SaveChanges(), Is.True);
            Assert.That(File.ReadAllText(JsonPath("ru")), Does.Not.Contain("greeting").And.Not.Contain("welcome")); Assert.That(LocalizationProjectNotes.Load(config)[textId], Is.EqualTo("Keep this note"));
        }
        [Test] public void TextExternalEditBeforeCaptureCannotBecomeANewBaselineDuringRestore()
        {
            OpenText(); Field<TextField>("translation_en").value = "My draft";
            File.WriteAllText(JsonPath("en"), "{\"translations\":{\"greeting\":\"External\"}}"); var state = Keep(text.CaptureDraft());
            OpenText(); text.RestoreDraft(state); Assert.That(text.SaveChanges(), Is.False); Assert.That(Field<TextField>("translation_en").value, Is.EqualTo("My draft"));
            Assert.That(File.ReadAllText(JsonPath("en")), Does.Contain("External")); Assert.That(text.Reload(), Is.False); Assert.That(text.Reload(true), Is.True);
        }
        [Test] public void TextNotesCreatedExternallyAfterCaptureBlockSave()
        {
            OpenText(); Field<TextField>("entryDescription").value = "Mine"; var state = Keep(text.CaptureDraft());
            string path = LocalizationProjectNotes.GetAssetPath(config); Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, "{\"descriptions\":{\"" + textId + "\":\"External note\"}}");
            OpenText(); text.RestoreDraft(state); Assert.That(text.SaveChanges(), Is.False); Assert.That(File.ReadAllText(path), Does.Contain("External note"));
        }
        [Test] public void MissingOrMalformedSourceKeepsReadableTextDraftAndRequiresExplicitDiscard()
        {
            OpenText(); Field<TextField>("translation_en").value = "Do not lose"; var state = Keep(text.CaptureDraft()); File.WriteAllText(JsonPath("en"), "broken json");
            OpenText(); text.RestoreDraft(state); Assert.That(Field<VisualElement>("draftRecovery"), Is.Not.Null); Assert.That(Field<TextField>("recovered_en").value, Is.EqualTo("Do not lose"));
            Assert.That(text.HasPendingChanges, Is.True); Assert.That(text.SaveChanges(), Is.False); Assert.That(text.Reload(), Is.False);
            File.WriteAllText(JsonPath("en"), "{\"translations\":{\"greeting\":\"Hello\"}}"); Click("retryRecoveredDraft"); Assert.That(Field<VisualElement>("draftRecovery"), Is.Null); Assert.That(text.SaveChanges(), Is.True);
        }
        [Test] public void RestoreRefusesWrongScopeAndDoesNotReplaceAnotherPendingDraft()
        {
            OpenText(); Field<TextField>("translation_en").value = "First"; var state = Keep(text.CaptureDraft());
            Assert.Throws<InvalidOperationException>(() => text.RestoreDraft(state)); Assert.That(Field<TextField>("translation_en").value, Is.EqualTo("First"));
            OpenAssets(); Assert.Throws<InvalidOperationException>(() => assets.RestoreDraft(state)); Assert.That(assets.HasPendingChanges, Is.False);
        }
        [Test] public void RepeatedRestoreAndCaptureDoesNotAdvanceConfigBaseline()
        {
            OpenText(); Field<TextField>("translation_en").value = "Mine"; var state = Keep(text.CaptureDraft());
            OpenText(); text.RestoreDraft(state); state = Keep(text.CaptureDraft()); config.Sets[0].AddEntry("external"); Save(config);
            OpenText(); text.RestoreDraft(state); Assert.That(text.SaveChanges(), Is.False); Assert.That(config.Sets[0].Entries.Any(e => e.Key == "external"), Is.True);
        }
        [Test] public void AssetDraftRestoresReferenceRemovalAndKeyAndPreservesUndo()
        {
            OpenAssets(); Field<TextField>("assetKey").value = "icon"; Field<ObjectField>("asset_ru").value = texture; Click("removeAsset_en"); var state = Keep(assets.CaptureDraft());
            OpenAssets(); assets.RestoreDraft(state); Assert.That(Field<ObjectField>("asset_ru").value, Is.SameAs(texture)); Assert.That(assets.SaveChanges(), Is.True);
            Assert.That(Table("ru").Get("icon") == texture, Is.True); Assert.That(Table("en").HasKey("icon"), Is.False);
            Undo.PerformUndo(); Assert.That(Table("en").Get("picture") == texture, Is.True); Assert.That(Table("ru").HasKey("icon"), Is.False);
        }
        [Test] public void UnsavedNativeAssetChangeBlocksRestoredAssetSave()
        {
            OpenAssets(); Field<ObjectField>("asset_ru").value = texture; var state = Keep(assets.CaptureDraft());
            Table("en").Set("outside", texture); // Even without SetDirty / disk save, the read baseline changed.
            OpenAssets(); assets.RestoreDraft(state); Assert.That(assets.SaveChanges(), Is.False); Assert.That(Table("en").Get("outside"), Is.SameAs(texture));
        }
        [Test] public void DeletedAssignedAssetIsNotRestoredAsIntentionalEmptyValue()
        {
            OpenAssets(); Field<ObjectField>("asset_ru").value = texture; var state = Keep(assets.CaptureDraft()); AssetDatabase.DeleteAsset(root + "/Picture.asset");
            OpenAssets(); assets.RestoreDraft(state); Assert.That(Field<VisualElement>("draftRecovery"), Is.Not.Null); Assert.That(assets.HasPendingChanges, Is.True); Assert.That(assets.SaveChanges(), Is.False); Assert.That(Table("ru").HasKey("picture"), Is.False);
        }
        [Test] public void AppearanceDraftOwnsSeparateCloneAndSavesOnlyAfterExplicitAction()
        {
            var external = ScriptableObject.CreateInstance<ExtendedAppearance>(); AssetDatabase.CreateAsset(external, root + "/Resources/Extended.asset");
            config.Languages[0].Appearance = new LocalizationSourceReference("resources", "Extended", LocalizationSourceKind.Appearance); Save(config);
            OpenAppearance(); Field<Toggle>("overrideTypography").value = true; Field<Slider>("fontSizeMultiplier").value = 1.6f; var state = Keep(appearance.CaptureDraft());
            OpenAppearance(); appearance.RestoreDraft(state); state.Dispose(); Assert.That(Field<Slider>("fontSizeMultiplier").value, Is.EqualTo(1.6f)); Assert.That(Appearance().fontSizeMultiplier, Is.EqualTo(1));
            Assert.That(appearance.SaveChanges(), Is.True); Assert.That(Appearance().fontSizeMultiplier, Is.EqualTo(1.6f));
            Assert.That(Appearance(), Is.TypeOf<ExtendedAppearance>()); Assert.That(((ExtendedAppearance)Appearance()).AdditionalSetting, Is.EqualTo("Keep external settings"));
        }
        [Test] public void AppearanceConflictRetainsValuesAndCannotOverwriteExternalSettings()
        {
            OpenAppearance(); Field<Toggle>("overrideTypography").value = true; Field<Slider>("fontSizeMultiplier").value = 1.6f; var state = Keep(appearance.CaptureDraft());
            Appearance().fontSizeMultiplier = 1.2f; Save(Appearance()); OpenAppearance(); appearance.RestoreDraft(state);
            Assert.That(Field<Slider>("fontSizeMultiplier").value, Is.EqualTo(1.6f)); Assert.That(appearance.SaveChanges(), Is.False); Assert.That(Appearance().fontSizeMultiplier, Is.EqualTo(1.2f));
        }
        [Test] public void RemovedAppearanceSourcePreservesSnapshotForRecovery()
        {
            OpenAppearance(); Field<Toggle>("overrideTypography").value = true; Field<Slider>("fontSizeMultiplier").value = 1.6f; var state = Keep(appearance.CaptureDraft());
            AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(Appearance())); OpenAppearance(); appearance.RestoreDraft(state);
            Assert.That(Field<VisualElement>("draftRecovery"), Is.Not.Null); Assert.That(appearance.HasPendingChanges, Is.True); Assert.That(appearance.SaveChanges(), Is.False);
            Assert.That(appearance.Reload(), Is.False); appearance.Reload(true); Assert.That(appearance.HasPendingChanges, Is.False);
        }
        [Test] public void EmptyDraftAndUneditedViewsDoNotBecomeDirty()
        {
            Assert.That(new ProjectViewDraft().IsPending, Is.False); OpenText(); Assert.That(text.CaptureDraft(), Is.Null);
            OpenAssets(); Assert.That(assets.CaptureDraft(), Is.Null); OpenAppearance(); Assert.That(appearance.CaptureDraft(), Is.Null);
        }
        private sealed class DraftHost : EditorWindow { }
    }
}
