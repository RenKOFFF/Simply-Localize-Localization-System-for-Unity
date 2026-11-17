using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using SimplyLocalize.Editor;
using SimplyLocalize.Editor.Data;
using SimplyLocalize.Editor.Windows;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace SimplyLocalize.Tests
{
    public class ProjectToolsViewTests
    {
        private string root, entry, source;
        private LocalizationProjectConfig config;
        private EditorWindow host;
        private ProjectToolsView view;
        private ProjectNotesTestScope notesCleanup;
        private HashSet<int> previousUndo;
        [SetUp] public void Setup()
        {
            root = "Assets/SimplyLocalizeTools_" + Guid.NewGuid().ToString("N"); Directory.CreateDirectory(root); AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            config = LocalizationProjectAuthoring.Create(new[] { "en" }, "en", "en", root + "/Project.asset", false); notesCleanup = new ProjectNotesTestScope(config);
            entry = config.Sets[0].AddEntry("hello").Id; EditorUtility.SetDirty(config); AssetDatabase.SaveAssetIfDirty(config);
            source = root + "/Resources/SimplyLocalize/Translations/en/Common.json"; File.WriteAllText(source, "{\"translations\":{\"hello\":\"Hello\"}}");
            previousUndo = new HashSet<int>(Resources.FindObjectsOfTypeAll<LocalizationProjectUndo>().Select(r => r.GetInstanceID()));
            host = ScriptableObject.CreateInstance<ToolsHost>(); host.position = new Rect(100, 100, 900, 700); host.ShowUtility();
            view = new ProjectToolsView(config); host.rootVisualElement.Add(view);
        }
        [TearDown] public void Cleanup()
        {
            view?.Dispose(); if (host != null) host.Close();
            foreach (var record in Resources.FindObjectsOfTypeAll<LocalizationProjectUndo>().Where(r => !previousUndo.Contains(r.GetInstanceID()))) record.Dispose();
            notesCleanup?.Dispose(); AssetDatabase.DeleteAsset(root);
        }
        private T Field<T>(string name) where T : VisualElement => host.rootVisualElement.Q<T>(name);
        private void Click(string name) { var button = Field<Button>(name); using var submit = NavigationSubmitEvent.GetPooled(); submit.target = button; button.SendEvent(submit); }
        [Test] public void PreviewAndApplyButtonSaveNotesTranslationsAndLanguagesInOneUndo()
        {
            byte[] before = File.ReadAllBytes(source);
            Assert.That(view.PreviewCsv("Key,Description,en,pl\nhello,Menu greeting,Welcome,Cześć\n"), Is.True);
            Assert.That(view.HasPendingChanges, Is.True); Assert.That(File.ReadAllBytes(source), Is.EqualTo(before)); Assert.That(config.Languages.Count, Is.EqualTo(1));
            Assert.That(Field<ListView>("csvChanges").itemsSource.Count, Is.GreaterThan(3)); Click("csvApply");
            Assert.That(view.HasPendingChanges, Is.False); Assert.That(config.MainChannel.Languages, Does.Contain("pl")); Assert.That(LocalizationProjectNotes.Load(config)[entry], Is.EqualTo("Menu greeting"));
            Undo.PerformUndo(); Assert.That(config.Languages.Count, Is.EqualTo(1)); Assert.That(File.ReadAllBytes(source), Is.EqualTo(before)); Assert.That(LocalizationProjectNotes.Load(config), Is.Empty);
        }
        [Test] public void StalePreviewReportsErrorAndKeepsExternalData()
        {
            Assert.That(view.PreviewCsv("Key,en\nhello,Pending\n"), Is.True); File.WriteAllText(source, "{\"translations\":{\"hello\":\"External\"}}");
            Assert.That(view.ApplyPreview(), Is.False); Assert.That(Field<Button>("csvApply").enabledSelf, Is.False);
            Assert.That(Field<HelpBox>("csvStatus").text, Does.Contain("preview")); Assert.That(File.ReadAllText(source), Does.Contain("External"));
            Assert.That(view.HasPendingChanges, Is.True);
        }
        [Test] public void PolicyChangeInvalidatesOldPreviewAndCancelDoesNotCreateNewSources()
        {
            Assert.That(view.PreviewCsv("Key,pl\nhello,Cześć\n"), Is.True); string path = view.Preview.Changes.Single(c => c.Kind == ProjectCsvChangeKind.CreateSource).After;
            Field<Toggle>("csvClearEmpty").value = true; Assert.That(view.Preview, Is.Null); Assert.That(File.Exists(path), Is.False); Assert.That(config.Languages.Count, Is.EqualTo(1));
            view.PreviewCsv("Key,pl\nhello,Cześć\n"); Click("csvCancel"); Assert.That(view.Preview, Is.Null); Assert.That(File.Exists(path), Is.False);
        }
        [Test] public void InvalidReplacementCannotApplyEarlierValidPreview()
        {
            view.PreviewCsv("Key,en\nhello,Previous\n"); Assert.That(view.PreviewCsv("Key,en\nhello,\"broken"), Is.False);
            Assert.That(view.Preview, Is.Null); Assert.That(view.ApplyPreview(), Is.False); Assert.That(File.ReadAllText(source), Does.Not.Contain("Previous"));
        }
        [Test] public void ExportContainsSavedIdentityAndNotesWithoutApplyingPreview()
        {
            using (var edit = new LocalizationProjectEdit(config, config.Sets[0].Id)) { edit.SetDescription(entry, "Saved note"); edit.Commit(); }
            view.PreviewCsv("Key,en\nhello,Unapplied\n"); string path = root + "/Export.csv";
            Assert.That(view.ExportFile(path), Is.True); string csv = File.ReadAllText(path);
            Assert.That(csv, Does.Contain(entry).And.Contain("Saved note").And.Contain("Hello").And.Not.Contain("Unapplied")); Assert.That(view.HasPendingChanges, Is.True);
        }
        [Test] public void SharedSourceImpactIsVisibleBeforeApplying()
        {
            var other = config.AddSet("Dialogue"); other.AddEntry("hello"); other.SetSources("en", config.Sets[0].Sources[0].Sources.ToArray()); EditorUtility.SetDirty(config); AssetDatabase.SaveAssetIfDirty(config);
            Assert.That(view.PreviewCsv("Key,en\nhello,Changed\n"), Is.True);
            Assert.That(Field<HelpBox>("csvSharedSources").text, Does.Contain("Common / en").And.Contain("Dialogue / en")); Assert.That(File.ReadAllText(source), Does.Contain("Hello"));
        }
        private sealed class ToolsHost : EditorWindow { }
    }
}
