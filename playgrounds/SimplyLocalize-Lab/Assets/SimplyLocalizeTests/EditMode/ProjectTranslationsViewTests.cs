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
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace SimplyLocalize.Tests
{
    public class ProjectTranslationsViewTests
    {
        private string root, entry;
        private LocalizationProjectConfig config;
        private EditorWindow host;
        private ProjectTranslationsView view;
        private HashSet<int> previousUndo;
        private ProjectNotesTestScope notesCleanup;
        [SetUp] public void Setup()
        {
            root = "Assets/SimplyLocalizeTextView_" + Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(root); AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            config = LocalizationProjectAuthoring.Create(new[] { "en", "ru" }, "en", "en", root + "/Project.asset", false);
            notesCleanup = new ProjectNotesTestScope(config);
            entry = config.Sets[0].AddEntry("hello").Id; SaveConfig();
            File.WriteAllText(PathFor("en"), "{\"translations\":{\"hello\":\"Hello\",\"external\":\"Register me\"}}");
            File.WriteAllText(PathFor("ru"), "{\"translations\":{\"hello\":\"\"}}");
            previousUndo = new HashSet<int>(Resources.FindObjectsOfTypeAll<LocalizationProjectUndo>().Select(r => r.GetInstanceID()));
            host = ScriptableObject.CreateInstance<TextHost>(); host.position = new Rect(100, 100, 900, 600); host.ShowUtility();
        }
        [TearDown] public void Cleanup()
        {
            view?.Dispose(); if (host != null) host.Close();
            if (previousUndo != null) foreach (var record in Resources.FindObjectsOfTypeAll<LocalizationProjectUndo>().Where(r => !previousUndo.Contains(r.GetInstanceID()))) record.Dispose();
            notesCleanup?.Dispose(); AssetDatabase.DeleteAsset(root);
        }
        private string PathFor(string code) => root + "/Resources/SimplyLocalize/Translations/" + code + "/Common.json";
        private void SaveConfig() { EditorUtility.SetDirty(config); AssetDatabase.SaveAssetIfDirty(config); }
        private void Open(string set = null, ILocalizationEditorTextReader reader = null)
        { view?.Dispose(); host.rootVisualElement.Clear(); view = new ProjectTranslationsView(config, setId: set, reader: reader); host.rootVisualElement.Add(view); }
        private T Field<T>(string name) where T : VisualElement => host.rootVisualElement.Q<T>(name);

        [Test] public void AddingAnotherKeyClosesThePreviousSelectionBeforeReload()
        {
            Open();
            view.SelectEntry(entry);
            Assert.That(view.AddEntry("second"), Is.True);
            Assert.That(view.SelectedEntryId, Is.EqualTo(config.Sets[0].Entries.Single(item => item.Key == "second").Id));
            Assert.That(view.HasPendingChanges, Is.False);
            Assert.That(view.AddEntry("third"), Is.True);
            Assert.That(view.SelectedEntryId, Is.EqualTo(config.Sets[0].Entries.Single(item => item.Key == "third").Id));
        }

        [Test] public void RenamingAndSavingTranslationsFromUiKeepsIdentityAndNativeUndoRestoresBoth()
        {
            Open(); view.SelectEntry(entry);
            Field<TextField>("entryKey").value = "welcome"; Field<TextField>("translation_ru").value = "Привет";
            Assert.That(view.HasPendingChanges, Is.True); Assert.That(config.Sets[0].Entries[0].Key, Is.EqualTo("hello"));
            Assert.That(view.SaveChanges(), Is.True); Assert.That(view.HasPendingChanges, Is.False);
            Assert.That(config.Sets[0].Entries[0].Id, Is.EqualTo(entry)); Assert.That(config.Sets[0].Entries[0].Key, Is.EqualTo("welcome"));
            Assert.That(File.ReadAllText(PathFor("ru")), Does.Contain("Привет")); Assert.That(Field<TextField>("entryKey").value, Is.EqualTo("welcome"));
            Undo.PerformUndo(); Assert.That(Field<TextField>("entryKey").value, Is.EqualTo("hello")); Assert.That(Field<TextField>("translation_ru").value, Is.Empty);
            Undo.PerformRedo(); Assert.That(Field<TextField>("translation_ru").value, Is.EqualTo("Привет"));
        }
        [Test] public void FallbackPreviewDoesNotFillTheMissingDirectTranslationOrWriteOnSelection()
        {
            byte[] before = File.ReadAllBytes(PathFor("ru")); Open(); view.SelectEntry(entry);
            Assert.That(Field<TextField>("translation_ru").value, Is.Empty);
            Assert.That(Field<Label>("translationFallback_ru").text, Does.Contain("en: Hello"));
            Assert.That(view.HasPendingChanges, Is.False); Assert.That(Field<Button>("saveText").enabledSelf, Is.False);
            Assert.That(File.ReadAllBytes(PathFor("ru")), Is.EqualTo(before));
        }
        [Test] public void ExternalEditBlocksSaveKeepsTypedTextAndNeedsExplicitDiscardToReload()
        {
            Open(); view.SelectEntry(entry); Field<TextField>("translation_en").value = "Typed";
            File.WriteAllText(PathFor("en"), "{\"translations\":{\"hello\":\"External\"}}");
            Assert.That(view.SaveChanges(), Is.False); Assert.That(Field<TextField>("translation_en").value, Is.EqualTo("Typed"));
            Assert.That(view.HasPendingChanges, Is.True); Assert.That(view.Reload(), Is.False);
            Assert.That(File.ReadAllText(PathFor("en")), Does.Contain("External"));
            Assert.That(view.Reload(true), Is.True); Assert.That(Field<TextField>("translation_en").value, Is.EqualTo("External"));
        }
        [Test] public void MissingDestinationDoesNotWriteIntoFallbackOrLoseOtherLanguages()
        {
            config.Sets[0].RemoveSources("ru"); SaveConfig(); Open(); view.SelectEntry(entry);
            byte[] before = File.ReadAllBytes(PathFor("en")); Field<TextField>("translation_ru").value = "Привет";
            Assert.That(view.SaveChanges(), Is.False); Assert.That(Field<HelpBox>("textMessage").text, Does.Contain("source"));
            Assert.That(File.ReadAllBytes(PathFor("en")), Is.EqualTo(before));
        }
        [Test] public void RegisteringExternalKeyKeepsSourceBytesAndGeneratesStableIdentity()
        {
            byte[] bytes = File.ReadAllBytes(PathFor("en")); Open(); Assert.That(Field<ListView>("unregisteredText").itemsSource.Count, Is.EqualTo(1));
            Assert.That(view.AddEntry("external"), Is.True); string id = view.SelectedEntryId;
            Assert.That(config.Sets[0].Entries.Single(e => e.Key == "external").Id, Is.EqualTo(id));
            Assert.That(Field<TextField>("translation_en").value, Is.EqualTo("Register me")); Assert.That(File.ReadAllBytes(PathFor("en")), Is.EqualTo(bytes));
            Undo.PerformUndo(); Assert.That(config.Sets[0].Entries.Any(e => e.Key == "external"), Is.False);
            Undo.PerformRedo(); Assert.That(config.Sets[0].Entries.Single(e => e.Key == "external").Id, Is.EqualTo(id));
        }
        [Test] public void RegisteringAmbiguousExternalKeyDoesNotPublishBrokenDefinition()
        {
            string address = Path.GetFileName(root) + "Duplicate";
            File.WriteAllText(root + "/Resources/" + address + ".json", "{\"translations\":{\"external\":\"Duplicate\"}}");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var set = config.Sets[0]; set.SetSources("en", set.Sources.Single(s => s.Language == "en").Sources.Concat(new[] { new LocalizationSourceReference("resources", address, LocalizationSourceKind.TextTable) }).ToArray()); SaveConfig();
            Open(); Assert.That(view.AddEntry("external"), Is.False); Assert.That(config.Sets[0].Entries.Any(e => e.Key == "external"), Is.False);
            Assert.That(Field<HelpBox>("textMessage").text, Does.Contain("Duplicate key"));
        }
        [Test] public void MoveRetainsIdentityAndTranslationsAcrossSets()
        {
            var destination = config.AddSet("Dialogue"); string setId = destination.Id;
            config.MainChannel.IncludeSet(setId); SaveConfig();
            using (var create = new LocalizationProjectEdit(config, setId))
            {
                foreach (string language in new[] { "en", "ru" }) create.CreateTextSource(setId, language, root + "/Resources/" + Path.GetFileName(root) + language + ".json");
                create.Commit();
            }
            Open(); view.SelectEntry(entry); Assert.That(view.MoveEntry(setId), Is.True);
            Assert.That(config.Sets[0].Entries.Any(e => e.Id == entry), Is.False); Assert.That(config.Sets.Single(s => s.Id == setId).Entries.Single().Id, Is.EqualTo(entry));
            Open(setId); view.SelectEntry(entry); Assert.That(Field<TextField>("translation_en").value, Is.EqualTo("Hello"));
            Assert.That(File.ReadAllText(PathFor("en")), Does.Not.Contain("hello"));
            Undo.PerformUndo(); Assert.That(config.Sets[0].Entries.Single().Id, Is.EqualTo(entry));
        }
        [Test] public void PendingEntryCannotBeReplacedByAnotherSelectionOrRegistration()
        {
            string other = config.Sets[0].AddEntry("other").Id; SaveConfig(); Open(); view.SelectEntry(entry);
            Field<TextField>("translation_en").value = "Pending";
            Assert.Throws<InvalidOperationException>(() => view.SelectEntry(other)); Assert.That(view.AddEntry("new"), Is.False);
            Assert.That(view.SelectedEntryId, Is.EqualTo(entry)); Assert.That(Field<TextField>("translation_en").value, Is.EqualTo("Pending"));
        }
        [Test] public void MovePickerLetsUserChooseBetweenMultipleDestinationSources()
        {
            var destination = config.AddSet("Dialogue"); string setId = destination.Id;
            config.MainChannel.IncludeSet(setId); SaveConfig();
            string prefix = Path.GetFileName(root);
            using (var create = new LocalizationProjectEdit(config, setId))
            {
                create.CreateTextSource(setId, "en", root + "/Resources/" + prefix + "First.json");
                create.CreateTextSource(setId, "en", root + "/Resources/" + prefix + "Second.json");
                create.CreateTextSource(setId, "ru", root + "/Resources/" + prefix + "Ru.json");
                create.Commit();
            }
            Open(); view.SelectEntry(entry);
            Field<DropdownField>("moveTextDestination_en").value = prefix + "Second";
            var button = Field<Button>("moveText"); using var submit = NavigationSubmitEvent.GetPooled(); submit.target = button; button.SendEvent(submit);
            Assert.That(config.Sets.Single(s => s.Id == setId).Entries.Single().Id, Is.EqualTo(entry));
            Assert.That(File.ReadAllText(root + "/Resources/" + prefix + "Second.json"), Does.Contain("Hello"));
            Assert.That(File.ReadAllText(root + "/Resources/" + prefix + "First.json"), Does.Not.Contain("hello"));
        }
        [Test] public void CorrectedRetryDoesNotCommitAValueRevertedAfterTheFailedAttempt()
        {
            string extra = Path.GetFileName(root) + "Extra";
            File.WriteAllText(PathFor("ru"), "{\"translations\":{}}");
            File.WriteAllText(root + "/Resources/" + extra + ".json", "{\"translations\":{}}");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var set = config.Sets[0]; set.SetSources("ru", set.Sources.Single(s => s.Language == "ru").Sources.Concat(new[] { new LocalizationSourceReference("resources", extra, LocalizationSourceKind.TextTable) }).ToArray()); SaveConfig();
            Open(); view.SelectEntry(entry); Field<TextField>("translation_en").value = "Unwanted"; Field<TextField>("translation_ru").value = "Привет";
            Assert.That(view.SaveChanges(), Is.False);
            Field<TextField>("translation_en").value = "Hello";
            Field<DropdownField>("translationDestination_ru").value = "SimplyLocalize/Translations/ru/Common";
            Assert.That(view.SaveChanges(), Is.True); Assert.That(Field<TextField>("translation_en").value, Is.EqualTo("Hello"));
            Assert.That(Field<TextField>("translation_ru").value, Is.EqualTo("Привет"));
        }
        [UnityTest] public IEnumerator ThousandsOfEntriesUseVirtualizedRowsAndSearchOnlyChangesEditorSelection()
        {
            for (int i = 0; i < 2500; i++) config.Sets[0].AddEntry("key_" + i.ToString("D4")); SaveConfig(); Open();
            yield return null; yield return null;
            var list = Field<ListView>("textEntries"); Assert.That(list.itemsSource.Count, Is.EqualTo(2501));
            Assert.That(list.virtualizationMethod, Is.EqualTo(CollectionVirtualizationMethod.FixedHeight));
            Assert.That(list.Query<Label>().ToList().Count, Is.LessThan(100));
            Field<UnityEditor.UIElements.ToolbarSearchField>("textSearch").value = "key_2499";
            Assert.That(list.itemsSource.Count, Is.EqualTo(1)); Assert.That(config.MainChannel.DefaultLanguage, Is.EqualTo("en"));
        }
        [Test] public void CustomReaderAllowsPreviewAndMetadataRegistrationWithoutAnImplicitWriter()
        {
            foreach (string code in new[] { "en", "ru" }) config.Sets[0].SetSources(code, new LocalizationSourceReference("external", code, LocalizationSourceKind.TextTable)); SaveConfig();
            Open(reader: new Reader()); view.SelectEntry(entry);
            Assert.That(Field<TextField>("translation_en").value, Is.EqualTo("External reader")); Assert.That(Field<TextField>("translation_en").enabledSelf, Is.False);
            Assert.That(view.AddEntry("new"), Is.True); Assert.That(config.Sets[0].Entries.Any(e => e.Key == "new"), Is.True);
        }
        [Test] public void TranslatorNoteIsAnExplicitPendingEditAndUndoRestoresIt()
        {
            Open(); view.SelectEntry(entry); Field<TextField>("entryDescription").value = "Menu greeting";
            Assert.That(view.HasPendingChanges, Is.True); Assert.That(LocalizationProjectNotes.Load(config), Is.Empty);
            Assert.That(view.SaveChanges(), Is.True); Assert.That(LocalizationProjectNotes.Load(config)[entry], Is.EqualTo("Menu greeting"));
            Undo.PerformUndo(); Assert.That(Field<TextField>("entryDescription").value, Is.Empty);
            Undo.PerformRedo(); Assert.That(Field<TextField>("entryDescription").value, Is.EqualTo("Menu greeting"));
        }
        private sealed class Reader : ILocalizationEditorTextReader
        { public LocalizationEditorTextSource Read(LocalizationSourceReference source) => new(new Dictionary<string, string> { ["hello"] = "External reader" }); }
        private sealed class TextHost : EditorWindow { }
    }
}
