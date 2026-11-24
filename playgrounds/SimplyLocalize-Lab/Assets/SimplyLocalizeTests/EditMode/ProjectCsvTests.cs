using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using SimplyLocalize.Editor;
using SimplyLocalize.Editor.Data;
using UnityEditor;

namespace SimplyLocalize.Tests
{
    public class ProjectCsvTests
    {
        private string root, entry;
        private LocalizationProjectConfig config;
        private ProjectNotesTestScope notesCleanup;
        [SetUp] public void Setup()
        {
            root = "Assets/SimplyLocalizeProjectCsv_" + Guid.NewGuid().ToString("N"); Directory.CreateDirectory(root); AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            config = LocalizationProjectAuthoring.Create(new[] { "en", "ru" }, "en", "en", root + "/Project.asset", false);
            notesCleanup = new ProjectNotesTestScope(config);
            entry = config.Sets[0].AddEntry("hello").Id; SaveConfig();
            File.WriteAllText(PathFor("en"), "{\"translations\":{\"hello\":\"Hello\"},\"custom\":true}");
            File.WriteAllText(PathFor("ru"), "{\"translations\":{\"hello\":\"Привет\"}}");
        }
        [TearDown] public void Cleanup() { notesCleanup?.Dispose(); AssetDatabase.DeleteAsset(root); }
        private string PathFor(string code) => root + "/Resources/SimplyLocalize/Translations/" + code + "/Common.json";
        private void SaveConfig() { EditorUtility.SetDirty(config); AssetDatabase.SaveAssetIfDirty(config); }
        private string Value(string id, string code, string setId = null)
        { var data = EditorProjectTextData.Load(config, setId: setId); return data.TryGetDirect(id, code, out var cell) ? cell.Value : null; }
        [Test] public void PreviewDoesNotWriteAndApplyPreservesIdAndNativeUndo()
        {
            byte[] bytes = File.ReadAllBytes(PathFor("en"));
            using var plan = ProjectCsv.Preview(config, "EntryId,Key,en,ru\n" + entry + ",welcome,Welcome,Добро пожаловать\n");
            Assert.That(plan.Changes.Any(c => c.Kind == ProjectCsvChangeKind.RenameEntry && c.EntryId == entry), Is.True);
            Assert.That(File.ReadAllBytes(PathFor("en")), Is.EqualTo(bytes)); Assert.That(config.Sets[0].Entries[0].Key, Is.EqualTo("hello"));
            using var record = LocalizationProjectUndo.Record(plan.Apply(), "Import project CSV");
            Assert.That(config.Sets[0].Entries[0].Id, Is.EqualTo(entry)); Assert.That(Value(entry, "ru"), Is.EqualTo("Добро пожаловать"));
            Undo.PerformUndo(); Assert.That(config.Sets[0].Entries[0].Key, Is.EqualTo("hello")); Assert.That(File.ReadAllBytes(PathFor("en")), Is.EqualTo(bytes));
            Undo.PerformRedo(); Assert.That(Value(entry, "en"), Is.EqualTo("Welcome")); Assert.Throws<InvalidOperationException>(() => plan.Apply());
        }
        [Test] public void ExportRoundTripPreservesCsvEscapesAndUntouchedJsonValueTypes()
        {
            using (var edit = new LocalizationProjectEdit(config, config.Sets[0].Id)) { edit.SetTranslation(entry, "en", "Quoted \"name\",\r\nnext"); edit.Commit(); }
            File.WriteAllText(PathFor("ru"), "{\"translations\":{\"hello\":42}}");
            byte[] en = File.ReadAllBytes(PathFor("en")), ru = File.ReadAllBytes(PathFor("ru"));
            string csv = ProjectCsv.Export(config, includeSources: true);
            using var plan = ProjectCsv.Preview(config, csv); Assert.That(plan.Changes, Is.Empty); Assert.That(plan.Apply().HasChanges, Is.False);
            Assert.That(File.ReadAllBytes(PathFor("en")), Is.EqualTo(en)); Assert.That(File.ReadAllBytes(PathFor("ru")), Is.EqualTo(ru));
        }
        [Test] public void CancelDuringComparisonDiscardsTheDraftAndAllowsRetry()
        {
            var csv = "Key,en\nhello,Updated\nnew,Added\n";
            byte[] before = File.ReadAllBytes(PathFor("en"));
            Assert.Throws<OperationCanceledException>(() => ProjectCsv.Preview(config, csv, null, null, CsvEmptyCellPolicy.Skip, null,
                (progress, message) => { if (progress >= .35f) throw new OperationCanceledException(); }));
            Assert.That(config.Sets[0].Entries.Count, Is.EqualTo(1));
            Assert.That(File.ReadAllBytes(PathFor("en")), Is.EqualTo(before));
            using var retry = ProjectCsv.Preview(config, csv);
            retry.Apply();
            Assert.That(Value(entry, "en"), Is.EqualTo("Updated"));
        }

        [Test] public void ConflictingBatchPreservesAllPreviouslyRegisteredEntries()
        {
            var set = config.Sets[0];
            var added = new LocalizationEntryDefinition("new", LocalizationEntryKind.Text);
            Assert.Throws<ArgumentException>(() => set.AddEntries(new[] { added, added }));
            Assert.That(set.Entries.Select(e => e.Id), Is.EqualTo(new[] { entry }));
        }

        [Test] public void WithoutIdsMatchesSetKeyAndAllocatesNewIdentityBeforeApply()
        {
            using var plan = ProjectCsv.Preview(config, "Key,en\nhello,Updated\nnew,New text\n");
            string id = plan.Changes.Single(c => c.Kind == ProjectCsvChangeKind.AddEntry).EntryId;
            Assert.That(Guid.TryParseExact(id, "N", out _), Is.True); plan.Apply();
            Assert.That(config.Sets[0].Entries.Single(e => e.Key == "hello").Id, Is.EqualTo(entry)); Assert.That(config.Sets[0].Entries.Single(e => e.Key == "new").Id, Is.EqualTo(id));
            Assert.That(Value(id, "en"), Is.EqualTo("New text"));
        }
        [Test] public void NewLanguageAndSourceAreCreatedTogetherAndUndoRedoKeepGuid()
        {
            var independent = config.AddChannel("Voice", new[] { "en" }); independent.IncludeSet(config.Sets[0].Id); string voiceId = independent.Id; SaveConfig();
            using var plan = ProjectCsv.Preview(config, "Key,pl\nhello,Cześć\n");
            Assert.That(plan.NewLanguages, Is.EqualTo(new[] { "pl" })); string path = plan.Changes.Single(c => c.Kind == ProjectCsvChangeKind.CreateSource).After;
            Assert.That(File.Exists(path), Is.False); using var record = LocalizationProjectUndo.Record(plan.Apply(), "Import Polish");
            string guid = AssetDatabase.AssetPathToGUID(path); Assert.That(guid, Is.Not.Empty); Assert.That(Value(entry, "pl"), Is.EqualTo("Cześć"));
            Assert.That(config.Channels.Single(c => c.Id == voiceId).Languages, Is.EqualTo(new[] { "en" }));
            Undo.PerformUndo(); Assert.That(config.Languages.Any(l => l.Code == "pl"), Is.False); Assert.That(File.Exists(path), Is.False);
            Undo.PerformRedo(); Assert.That(AssetDatabase.AssetPathToGUID(path), Is.EqualTo(guid)); Assert.That(Value(entry, "pl"), Is.EqualTo("Cześć"));
        }
        [Test] public void ClearAndSkipAreExplicitAndWhitespaceIsPreserved()
        {
            using (var plan = ProjectCsv.Preview(config, "Key,en,ru\nhello,,  \n"))
            { Assert.That(plan.SkippedEmptyCells, Is.EqualTo(1)); plan.Apply(); }
            Assert.That(Value(entry, "en"), Is.EqualTo("Hello")); Assert.That(Value(entry, "ru"), Is.EqualTo("  "));
            using (var plan = ProjectCsv.Preview(config, "Key,en\nhello,\n", emptyCells: CsvEmptyCellPolicy.Clear)) plan.Apply();
            Assert.That(Value(entry, "en"), Is.Empty);
        }
        [Test] public void MoveAndRenameRetainAllLanguagesIncludingThoseNotInCsv()
        {
            var target = config.AddSet("Dialogue"); config.MainChannel.IncludeSet(target.Id); string targetId = target.Id; SaveConfig();
            using var plan = ProjectCsv.Preview(config, "SetId,EntryId,Key,en\n" + targetId + "," + entry + ",welcome,Welcome\n");
            Assert.That(plan.Changes.Count(c => c.Kind == ProjectCsvChangeKind.CreateSource), Is.EqualTo(2)); plan.Apply();
            Assert.That(config.Sets[0].Entries, Is.Empty); Assert.That(config.Sets.Single(s => s.Id == targetId).Entries.Single().Id, Is.EqualTo(entry));
            Assert.That(Value(entry, "ru", targetId), Is.EqualTo("Привет")); Assert.That(Value(entry, "en", targetId), Is.EqualTo("Welcome"));
        }
        [Test] public void StalePreviewPreservesExternalChangesAndCreatesNoNewLanguage()
        {
            using var plan = ProjectCsv.Preview(config, "Key,en,pl\nhello,Updated,Cześć\n");
            string newPath = plan.Changes.Single(c => c.Kind == ProjectCsvChangeKind.CreateSource).After;
            File.AppendAllText(PathFor("en"), " \n"); byte[] external = File.ReadAllBytes(PathFor("en"));
            Assert.Throws<InvalidOperationException>(() => plan.Apply()); Assert.That(File.ReadAllBytes(PathFor("en")), Is.EqualTo(external));
            Assert.That(File.Exists(newPath), Is.False); Assert.That(config.Languages.Any(l => l.Code == "pl"), Is.False);
        }
        [Test] public void ReadOnlyFailureLeavesConfigAndOtherTranslationsUnchanged()
        {
            using var plan = ProjectCsv.Preview(config, "Key,en,ru\nhello,Updated,Изменено\n"); byte[] en = File.ReadAllBytes(PathFor("en"));
            var attributes = File.GetAttributes(PathFor("ru")); File.SetAttributes(PathFor("ru"), attributes | FileAttributes.ReadOnly);
            try { Assert.Catch<Exception>(() => plan.Apply()); } finally { File.SetAttributes(PathFor("ru"), attributes); }
            Assert.That(File.ReadAllBytes(PathFor("en")), Is.EqualTo(en)); Assert.That(Value(entry, "ru"), Is.EqualTo("Привет"));
        }
        [Test] public void MultipleSourcesRequireExplicitDestinationForNewKey()
        {
            string address = Path.GetFileName(root) + "Extra";
            using (var edit = new LocalizationProjectEdit(config, config.Sets[0].Id)) { edit.CreateTextSource(config.Sets[0].Id, "en", root + "/Resources/" + address + ".json"); edit.Commit(); }
            Assert.Throws<InvalidOperationException>(() => ProjectCsv.Preview(config, "Key,en\nnew,New text\n"));
            using var plan = ProjectCsv.Preview(config, "Key,en,Source:en\nnew,New text," + address + "\n"); plan.Apply();
            Assert.That(File.ReadAllText(root + "/Resources/" + address + ".json"), Does.Contain("New text")); Assert.That(File.ReadAllText(PathFor("en")), Does.Not.Contain("new"));
        }
        [Test] public void SharedSourceCannotReceiveContradictoryValuesFromDifferentEntries()
        {
            var other = config.AddSet("Other"); other.AddEntry("hello");
            other.SetSources("en", config.Sets[0].Sources.Single(s => s.Language == "en").Sources.ToArray()); config.MainChannel.IncludeSet(other.Id); SaveConfig();
            string csv = "SetId,Key,en\n" + config.Sets[0].Id + ",hello,Changed\n" + other.Id + ",hello,Hello\n";
            Assert.That(Assert.Throws<FormatException>(() => ProjectCsv.Preview(config, csv)).Message, Does.Contain("shared source")); Assert.That(Value(entry, "en"), Is.EqualTo("Hello"));
        }
        [Test] public void ConflictingIdentityAndMalformedRowsNeverChangeFiles()
        {
            byte[] original = File.ReadAllBytes(PathFor("en")); string foreign = Guid.NewGuid().ToString("N");
            Assert.Throws<ArgumentException>(() => ProjectCsv.Preview(config, "EntryId,Key,en\n" + foreign + ",hello,Foreign\n"));
            Assert.Throws<FormatException>(() => ProjectCsv.Preview(config, "Key,en,EN\nhello,One,Two\n"));
            Assert.Throws<FormatException>(() => ProjectCsv.Preview(config, "Key,en\nhello,\"broken\n"));
            Assert.Throws<FormatException>(() => ProjectCsv.Preview(config, "EntryId,Key,en\n" + entry + ",hello,One\n" + entry + ",other,Two\n"));
            Assert.That(File.ReadAllBytes(PathFor("en")), Is.EqualTo(original));
        }
        [Test] public void CancelAndHeaderOnlyPreviewLeaveNoFilesOrDefinitions()
        {
            string path;
            using (var plan = ProjectCsv.Preview(config, "Key,pl\nnew,Nowy\n")) path = plan.Changes.Single(c => c.Kind == ProjectCsvChangeKind.CreateSource).After;
            Assert.That(File.Exists(path), Is.False); Assert.That(config.Sets[0].Entries.Count, Is.EqualTo(1)); Assert.That(config.Languages.Count, Is.EqualTo(2));
            using var empty = ProjectCsv.Preview(config, "Key,pl\n"); Assert.That(empty.Changes, Is.Empty); Assert.That(empty.Apply().HasChanges, Is.False);
        }
        [Test] public void DescriptionRoundTripsAndEmptyPolicyNeverSilentlyDropsNotes()
        {
            using (var plan = ProjectCsv.Preview(config, "Key,Description,en\nhello,Title screen,Hello\n"))
            { Assert.That(plan.Changes.Single().Kind, Is.EqualTo(ProjectCsvChangeKind.Description)); plan.Apply(); }
            string csv = ProjectCsv.Export(config); Assert.That(csv, Does.Contain("Description").And.Contain("Title screen"));
            using (var plan = ProjectCsv.Preview(config, csv)) Assert.That(plan.Apply().HasChanges, Is.False);
            using (var plan = ProjectCsv.Preview(config, "Key,Description,en\nhello,,Hello\n")) plan.Apply();
            Assert.That(LocalizationProjectNotes.Load(config)[entry], Is.EqualTo("Title screen"));
            using (var plan = ProjectCsv.Preview(config, "Key,Description,en\nhello,,Hello\n", emptyCells: CsvEmptyCellPolicy.Clear)) plan.Apply();
            Assert.That(LocalizationProjectNotes.Load(config).ContainsKey(entry), Is.False);
        }
    }
}
