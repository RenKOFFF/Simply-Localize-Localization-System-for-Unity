using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using SimplyLocalize.Editor;
using SimplyLocalize.Editor.Data;
using UnityEditor;

namespace SimplyLocalize.Tests
{
    // Persistence guarantees from the former config/profile CSV importer, exercised through channels.
    public class CsvPersistenceTests
    {
        private string root, entry, enPath, ruPath, notesPath;
        private LocalizationProjectConfig config;
        private ProjectNotesTestScope cleanup;
        [SetUp] public void Setup()
        {
            root = "Assets/SimplyLocalizeCsvPersistence_" + Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(root); AssetDatabase.Refresh();
            config = LocalizationProjectAuthoring.Create(new[] { "en", "ru" }, "en", "en", root + "/Project.asset", false);
            entry = config.Sets[0].AddEntry("key").Id; SaveConfig();
            cleanup = new ProjectNotesTestScope(config); notesPath = LocalizationProjectNotes.GetAssetPath(config);
            enPath = PathFor("en"); ruPath = PathFor("ru");
            foreach (string code in new[] { "en", "ru" })
                File.WriteAllText(PathFor(code), "{\"translations\":{\"key\":\"original " + code + "\"},\"note\":\"preserve\"}");
            using var edit = Edit(); edit.SetDescription(entry, "original description"); edit.Commit();
        }
        [TearDown] public void Cleanup() { cleanup?.Dispose(); AssetDatabase.DeleteAsset(root); }
        private string PathFor(string code) => root + "/Resources/SimplyLocalize/Translations/" + code + "/Common.json";
        private LocalizationProjectEdit Edit() => new(config, config.Sets[0].Id);
        private void SaveConfig() { EditorUtility.SetDirty(config); AssetDatabase.SaveAssetIfDirty(config); }
        private string Value(string code) { Assert.That(EditorProjectTextData.Load(config).TryGetDirect(entry, code, out var cell), Is.True); return cell.Value; }
        private void Import(string csv, CsvEmptyCellPolicy policy = CsvEmptyCellPolicy.Skip)
        { using var plan = ProjectCsv.Preview(config, csv, emptyCells: policy); plan.Apply(); }

        [Test] public void DefaultImportSkipsEmptyTranslationsAndDescriptions()
        {
            Import("Key,Description,en,ru\nkey,,,new ru");
            Assert.That(Value("en"), Is.EqualTo("original en")); Assert.That(Value("ru"), Is.EqualTo("new ru"));
            Assert.That(LocalizationProjectNotes.Load(config)[entry], Is.EqualTo("original description"));
        }
        [Test] public void ExplicitClearChangesOnlyPresentColumnsAndPreservesIdentity()
        {
            Import("Key,Description,en\nkey,,", CsvEmptyCellPolicy.Clear);
            Assert.That(Value("en"), Is.Empty); Assert.That(Value("ru"), Is.EqualTo("original ru"));
            Assert.That(LocalizationProjectNotes.Load(config).ContainsKey(entry), Is.False);
            Assert.That(config.Sets[0].Entries.Single().Id, Is.EqualTo(entry));
        }
        [Test] public void ExportImportRoundTripsWhitespaceQuotesNewlinesAndUnicode()
        {
            const string value = "  Привет, \"мир\"\r\nline\rnext\n😀  "; const string key = "key,\"quoted\"";
            using (var edit = Edit()) { edit.RenameTextEntry(entry, key); edit.SetTranslation(entry, "en", value); edit.SetDescription(entry, value); edit.Commit(); }
            string csv = ProjectCsv.Export(config, includeSources: true); var disk = Snapshot();
            Import(csv); AssertSnapshot(disk); Assert.That(Value("en"), Is.EqualTo(value));
            Assert.That(LocalizationProjectNotes.Load(config)[entry], Is.EqualTo(value));
            Import("Key,en\n\"key,\"\"quoted\"\"\",   "); Assert.That(Value("en"), Is.EqualTo("   "));
        }
        [TestCase("\n")][TestCase("\r\n")][TestCase("\r")]
        public void BomQuotedHeaderAndRecordSeparatorsRemainSupported(string newline)
        { Import("\uFEFF\"Key\",\"en\"" + newline + "key,updated" + newline); Assert.That(Value("en"), Is.EqualTo("updated")); }
        [Test] public void LanguageColumnsNormalizeStandardCodes()
        {
            Import("Key,EN,pt-br\nkey,updated,Olá");
            Assert.That(Value("en"), Is.EqualTo("updated")); Assert.That(Value("pt-BR"), Is.EqualTo("Olá"));
            Assert.That(config.Languages.Select(l => l.Code), Is.EqualTo(new[] { "en", "ru", "pt-BR" }));
        }
        [TestCase("")][TestCase("Other,en\nkey,new")][TestCase("Key,Description\nkey,new")]
        [TestCase("Key,en,en\nkey,new,new")][TestCase("Key,en,EN\nkey,new,new")]
        [TestCase("Key,pt-BR,pt-br\nkey,new,new")][TestCase("Key,\nkey,new")]
        [TestCase("Key,en\nkey,new\nkey,duplicate")][TestCase("Key,en\nkey,new\nnext")]
        [TestCase("Key,en\nkey,new\nnext,x,extra")][TestCase("Key,en\nkey,new\n,x")]
        [TestCase("Key,en\nkey,new\nnext,\"unterminated")][TestCase("Key,en\nkey,new\nnext,\"x\"junk")]
        [TestCase("Key,en\nkey,new\nnext,x\"y")][TestCase("Key,SetId,en\nkey,unknown,new")]
        [TestCase("Key,en,Source:ru\nkey,new,unknown")]
        public void InvalidCsvDoesNotApplyEarlierRowsOrWriteAnything(string csv)
        {
            var disk = Snapshot(); string before = ProjectCsv.Export(config);
            Assert.Catch<Exception>(() => ProjectCsv.Preview(config, csv));
            Assert.That(ProjectCsv.Export(config), Is.EqualTo(before)); AssertSnapshot(disk);
        }
        [TestCase("../escape")][TestCase("a/b")][TestCase("a\\b")][TestCase("C:escape")]
        [TestCase("CON")][TestCase("aux.json")][TestCase("LPT1")][TestCase("menu.")]
        public void UnregisteredSourceCannotSelectOrCreateAnArbitraryFile(string address)
        {
            var disk = Snapshot();
            Assert.Throws<ArgumentException>(() => ProjectCsv.Preview(config, "Key,en,Source:en\nnext,value," + address));
            AssertSnapshot(disk); Assert.That(config.Sets[0].Entries.Count, Is.EqualTo(1));
        }
        [TestCase("Assets/../escape.json")][TestCase("Assets/Outside.json")]
        [TestCase("Packages/other/Resources/x.json")][TestCase("Assets/Resources/CON.json")]
        [TestCase("Assets/Resources/aux.json")][TestCase("Assets/Resources/LPT1.json")]
        [TestCase("Assets/Resources/menu./text.json")][TestCase("Assets/Resources/menu /text.json")]
        [TestCase("Assets/Resources/../text.json")][TestCase("Assets/Resources/a\\b.json")]
        public void CustomSourceFactoryCannotEscapeEditableResources(string path)
        {
            var disk = Snapshot(); Assert.Catch<Exception>(() => ProjectCsv.Preview(config, "Key,pl\nkey,Cześć", newSourcePath: (_, _) => path));
            AssertSnapshot(disk); Assert.That(config.Languages.Any(l => l.Code == "pl"), Is.False);
        }
        [Test] public void PreviewAndCancelAreReadOnlyAndConfigChangeInvalidatesPreview()
        {
            var disk = Snapshot(); using var plan = ProjectCsv.Preview(config, "Key,Description,en,fr\nnext,new note,hello,bonjour");
            Assert.That(plan.NewLanguages, Is.EqualTo(new[] { "fr" })); Assert.That(plan.Changes, Is.Not.Empty); AssertSnapshot(disk);
            Assert.That(config.Sets[0].Entries.Count, Is.EqualTo(1)); config.name = "External config edit"; EditorUtility.SetDirty(config);
            Assert.Throws<InvalidOperationException>(() => plan.Apply()); Assert.That(config.name, Is.EqualTo("External config edit")); AssertSnapshot(disk);
        }
        [Test] public void RepeatedImportIsIdempotentAndUsesSelectedDefaultSet()
        {
            const string csv = "Key,en\nnew,value"; Import(csv); var disk = Snapshot();
            using var plan = ProjectCsv.Preview(config, csv); Assert.That(plan.Changes, Is.Empty); Assert.That(plan.Apply().HasChanges, Is.False); AssertSnapshot(disk);
            Assert.That(config.Sets[0].Entries.Any(e => e.Key == "new"), Is.True);
        }
        [Test] public void AdditionalJsonFieldsAndNewLanguageSurviveReload()
        {
            Import("Key,Description,en,fr\nkey,changed,edited,bonjour");
            Assert.That(JObject.Parse(File.ReadAllText(enPath))["note"].Value<string>(), Is.EqualTo("preserve"));
            Assert.That(Value("fr"), Is.EqualTo("bonjour")); Assert.That(LocalizationProjectNotes.Load(config)[entry], Is.EqualTo("changed"));
            Import("Key,en,fr\nkey,edited,bonjour"); Assert.That(config.Languages.Count(l => l.Code == "fr"), Is.EqualTo(1));
        }
        [Test] public void MetadataErrorPreventsAnyTranslationWrite()
        {
            File.WriteAllText(notesPath, "{"); var disk = Snapshot();
            Assert.Catch<Exception>(() => ProjectCsv.Preview(config, "Key,Description,en\nkey,changed,edited")); AssertSnapshot(disk);
        }
        [Test] public void ExternalTranslationEditIsNeverOverwrittenEvenInAnOmittedColumn()
        {
            using var plan = ProjectCsv.Preview(config, "Key,en\nkey,edited");
            File.WriteAllText(ruPath, "{\"translations\":{\"key\":\"external\"}}"); var disk = Snapshot();
            Assert.Throws<InvalidOperationException>(() => plan.Apply()); AssertSnapshot(disk); Assert.That(Value("en"), Is.EqualTo("original en"));
        }
        [TestCase(false)][TestCase(true)] public void UnconnectedLanguageFileIsNeverOverwritten(bool appearsAfterPreview)
        {
            string path = PathFor("fr"); ProjectCsvImportPlan plan = null;
            try
            {
                if (appearsAfterPreview) plan = ProjectCsv.Preview(config, "Key,fr\nkey,bonjour", newSourcePath: (_, _) => path);
                Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, "{\"translations\":{\"other\":\"keep\"}}"); var disk = Snapshot();
                if (appearsAfterPreview) Assert.Catch<Exception>(() => plan.Apply());
                else Assert.Catch<Exception>(() => ProjectCsv.Preview(config, "Key,fr\nkey,bonjour", newSourcePath: (_, _) => path));
                AssertSnapshot(disk); Assert.That(config.Languages.Any(l => l.Code == "fr"), Is.False);
            }
            finally { plan?.Dispose(); }
        }
        [Test, Platform("Win")] public void LateWriteFailureRollsBackFilesNotesAndNewLanguageAndCanRetry()
        {
            var disk = Snapshot(); using var plan = ProjectCsv.Preview(config, "Key,Description,en,ru,fr\nkey,changed,new en,new ru,bonjour");
            string newPath = plan.Changes.Single(c => c.Kind == ProjectCsvChangeKind.CreateSource).After;
            using (new FileStream(notesPath, FileMode.Open, FileAccess.Read, FileShare.Read)) Assert.Throws<IOException>(() => plan.Apply());
            AssertSnapshot(disk); Assert.That(config.Languages.Any(l => l.Code == "fr"), Is.False); Assert.That(File.Exists(newPath + ".meta"), Is.False);
            Assert.That(Directory.Exists(Path.GetDirectoryName(newPath)), Is.False); Assert.That(plan.IsApplied, Is.False);
            plan.Apply(); Assert.That(Value("fr"), Is.EqualTo("bonjour")); Assert.That(Value("ru"), Is.EqualTo("new ru"));
        }
        [Test, Platform("Win")] public void SingleFileFailureKeepsPendingEditAndDoesNotTruncate()
        {
            byte[] before = File.ReadAllBytes(enPath); using var edit = Edit(); edit.SetTranslation(entry, "en", "pending");
            using (new FileStream(enPath, FileMode.Open, FileAccess.Read, FileShare.Read)) Assert.Throws<IOException>(() => edit.Commit());
            Assert.That(File.ReadAllBytes(enPath), Is.EqualTo(before)); edit.Commit(); Assert.That(Value("en"), Is.EqualTo("pending"));
        }
        [Test, Platform("Win")] public void LaterLanguageWriteFailureRollsBackEarlierLanguage()
        {
            var disk = Snapshot(); using var edit = Edit(); edit.SetTranslation(entry, "en", "new en"); edit.SetTranslation(entry, "ru", "new ru");
            using (new FileStream(ruPath, FileMode.Open, FileAccess.Read, FileShare.Read)) Assert.Throws<IOException>(() => edit.Commit()); AssertSnapshot(disk);
        }
        [Test] public void ReadOnlyDestinationRejectsEntireBatchBeforeAnyWrite()
        {
            var disk = Snapshot(); using var plan = ProjectCsv.Preview(config, "Key,en,ru\nkey,edited,edited"); var attributes = File.GetAttributes(ruPath);
            try { File.SetAttributes(ruPath, attributes | FileAttributes.ReadOnly); Assert.Catch<Exception>(() => plan.Apply()); AssertSnapshot(disk); }
            finally { File.SetAttributes(ruPath, attributes); }
        }
        [Test] public void HeaderOnlyImportDoesNotSaveSeparatePendingEditsOrAddLanguages()
        {
            var disk = Snapshot(); using var edit = Edit(); edit.SetTranslation(entry, "en", "pending");
            using var plan = ProjectCsv.Preview(config, "Key,fr\n"); Assert.That(plan.Apply().HasChanges, Is.False); AssertSnapshot(disk);
            Assert.That(config.Languages.Any(l => l.Code == "fr"), Is.False); edit.Commit(); Assert.That(Value("en"), Is.EqualTo("pending"));
        }
        [Test] public void ExtraJsonFieldsPreserveDecimalPrecisionAndDateStrings()
        {
            File.WriteAllText(enPath, "{\"translations\":{\"key\":\"original en\"},\"amount\":0.1234567890123456789012345678,\"date\":\"2026-11-16T00:00:00Z\"}");
            Import("Key,en\nkey,edited"); string result = File.ReadAllText(enPath);
            Assert.That(result, Does.Contain("0.1234567890123456789012345678").And.Contain("2026-11-16T00:00:00Z"));
        }
        [Test, Platform("Win")] public void LockedConfigRejectsImportBeforeNativeSerializationAndAllowsRetry()
        {
            var disk = Snapshot(); using var plan = ProjectCsv.Preview(config, "Key,en,fr\nkey,hello,bonjour");
            using (new FileStream(AssetDatabase.GetAssetPath(config), FileMode.Open, FileAccess.Read, FileShare.Read)) Assert.Throws<IOException>(() => plan.Apply());
            Assert.That(config.Languages.Any(l => l.Code == "fr"), Is.False); Assert.That(EditorUtility.IsDirty(config), Is.False); AssertSnapshot(disk);
            plan.Apply(); Assert.That(Value("fr"), Is.EqualTo("bonjour"));
        }
        [Test] public void UnsavedConfigEditsRejectPreviewWithoutSavingOrDiscardingThem()
        {
            var disk = Snapshot(); config.name = "PendingConfig"; EditorUtility.SetDirty(config);
            Assert.Throws<InvalidOperationException>(() => ProjectCsv.Preview(config, "Key,en,fr\nkey,hello,bonjour"));
            Assert.That(config.name, Is.EqualTo("PendingConfig")); Assert.That(EditorUtility.IsDirty(config), Is.True); AssertSnapshot(disk);
            SaveConfig(); Import("Key,en,fr\nkey,hello,bonjour"); Assert.That(Value("fr"), Is.EqualTo("bonjour"));
        }
        [TestCase(false)][TestCase(true)] public void FailureAfterConfigSerializationRestoresDiskMemoryDirtyStateAndAllowsRetry(bool dirtyAfterPreview)
        {
            string before = EditorJsonUtility.ToJson(config); var disk = Snapshot();
            using var plan = ProjectCsv.Preview(config, "Key,en,fr\nkey,hello,bonjour");
            if (dirtyAfterPreview) EditorUtility.SetDirty(config);
            var edit = (LocalizationProjectEdit)typeof(ProjectCsvImportPlan).GetField("edit", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(plan);
            var commit = typeof(LocalizationProjectEdit).GetMethod("CommitWithNativeFiles", BindingFlags.Instance | BindingFlags.NonPublic);
            bool reached = false;
            Action fail = () =>
            {
                reached = config.Languages.Any(l => l.Code == "fr") && File.ReadAllText(AssetDatabase.GetAssetPath(config)).Contains("fr");
                throw new IOException("Simulated failure after native config serialization");
            };
            Type callbackType = commit.GetParameters()[1].ParameterType;
            var parameter = Expression.Parameter(callbackType.GenericTypeArguments[0]);
            var callback = Expression.Lambda(callbackType, Expression.Call(Expression.Constant(fail), typeof(Action).GetMethod("Invoke")), parameter).Compile();
            var error = Assert.Throws<TargetInvocationException>(() => commit.Invoke(edit, new object[] { null, callback }));
            Assert.That(error.InnerException, Is.TypeOf<IOException>()); Assert.That(reached, Is.True);
            Assert.That(EditorJsonUtility.ToJson(config), Is.EqualTo(before)); Assert.That(EditorUtility.IsDirty(config), Is.EqualTo(dirtyAfterPreview)); AssertSnapshot(disk);
            plan.Apply(); Assert.That(Value("fr"), Is.EqualTo("bonjour"));
        }
        [Test] public void TransactionFailureRestoresExternalFilesAndEarlierJsonWrites()
        {
            var disk = Snapshot(); string configPath = AssetDatabase.GetAssetPath(config), newPath = root + "/new.asset";
            WithTransaction(call =>
            {
                call("Add", new object[] { enPath, File.ReadAllBytes(enPath), Encoding.UTF8.GetBytes("updated") });
                call("TrackExternal", new object[] { configPath }); call("TrackExternal", new object[] { newPath });
                var error = Assert.Throws<TargetInvocationException>(() => call("Execute", new object[] { (Action)(() =>
                {
                    call("TouchExternal", new object[] { configPath }); File.WriteAllText(configPath, "changed config");
                    call("TouchExternal", new object[] { newPath }); File.WriteAllText(newPath, "new native asset"); throw new IOException("Simulated failure");
                }) })); Assert.That(error.InnerException, Is.TypeOf<IOException>());
            }); AssertSnapshot(disk);
        }
        [Test] public void ExternalFileAppearingBeforeNativeWriteIsNotDeletedByRollback()
        {
            string path = root + "/external.asset";
            WithTransaction(call =>
            {
                call("TrackExternal", new object[] { path });
                Assert.Throws<TargetInvocationException>(() => call("Execute", new object[] { (Action)(() =>
                { File.WriteAllText(path, "someone else's file"); call("TouchExternal", new object[] { path }); }) }));
            }); Assert.That(File.ReadAllText(path), Is.EqualTo("someone else's file"));
        }
        private static void WithTransaction(Action<Action<string, object[]>> action)
        {
            var type = typeof(LocalizationProjectEdit).Assembly.GetType("SimplyLocalize.Editor.Data.EditorFileTransaction");
            using var tx = (IDisposable)Activator.CreateInstance(type, true);
            action((method, args) => type.GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(tx, args));
        }
        private Dictionary<string, byte[]> Snapshot() => Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .Where(p => !p.EndsWith(".meta")).Concat(new[] { notesPath }).ToDictionary(Path.GetFullPath, File.ReadAllBytes);
        private void AssertSnapshot(Dictionary<string, byte[]> expected)
        { var actual = Snapshot(); Assert.That(actual.Keys, Is.EquivalentTo(expected.Keys)); foreach (var pair in expected) Assert.That(actual[pair.Key], Is.EqualTo(pair.Value), pair.Key); }
    }
}
