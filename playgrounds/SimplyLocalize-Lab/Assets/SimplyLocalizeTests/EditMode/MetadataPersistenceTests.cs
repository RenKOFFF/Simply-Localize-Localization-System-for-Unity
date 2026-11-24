using System;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using SimplyLocalize.Editor;
using SimplyLocalize.Editor.Data;
using UnityEditor;

namespace SimplyLocalize.Tests
{
    // Ported from key/history metadata: stable entry IDs replace historical-name lookup.
    // The same corruption, external-change and write-failure guarantees remain required.
    public class MetadataPersistenceTests
    {
        private string root, path, source, entry;
        private LocalizationProjectConfig config;
        private ProjectNotesTestScope cleanup;
        [SetUp] public void Setup()
        {
            root = "Assets/SimplyLocalizeMetadata_" + Guid.NewGuid().ToString("N"); Directory.CreateDirectory(root); AssetDatabase.Refresh();
            config = LocalizationProjectAuthoring.Create(new[] { "en" }, "en", "en", root + "/Project.asset", false);
            entry = config.Sets[0].AddEntry("key").Id; EditorUtility.SetDirty(config); AssetDatabase.SaveAssetIfDirty(config);
            cleanup = new ProjectNotesTestScope(config); path = LocalizationProjectNotes.GetAssetPath(config);
            source = root + "/Resources/SimplyLocalize/Translations/en/Common.json";
            Directory.CreateDirectory(Path.GetDirectoryName(path));
        }
        [TearDown] public void Cleanup() { cleanup?.Dispose(); AssetDatabase.DeleteAsset(root); }
        private LocalizationProjectEdit Edit() => new(config, config.Sets[0].Id);
        private string Json(string value = "original") => new JObject { ["descriptions"] = new JObject { [entry] = value } }.ToString();
        private void Write(string json) => File.WriteAllText(path, json.Replace("$ID", entry));
        private void RejectUnchanged()
        {
            byte[] original = File.ReadAllBytes(path), text = File.ReadAllBytes(source);
            Assert.Catch<Exception>(() => LocalizationProjectNotes.Load(config));
            using var edit = Edit(); Assert.Catch<Exception>(() => edit.SetDescription(entry, "Replacement"));
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(original)); Assert.That(File.ReadAllBytes(source), Is.EqualTo(text));
        }
        [Test] public void EscapedMetadataRoundTripsThroughEntryRenameWithoutLosingCharacters()
        {
            const string value = "П\b\f\u0001\n\t\r\\\"/";
            Write(Json(value)); using (var edit = Edit())
            {
                Assert.That(edit.GetDescription(entry), Is.EqualTo(value));
                Assert.Throws<ArgumentException>(() => edit.RenameTextEntry(entry, "Ключ/\"\\\u0002"));
                edit.RenameTextEntry(entry, "Ключ/\"\\"); edit.SetDescription(entry, value + " changed"); edit.Commit();
            }
            Assert.That(LocalizationProjectNotes.Load(config)[entry], Is.EqualTo(value + " changed")); Assert.That(config.Sets[0].Entries[0].Id, Is.EqualTo(entry));
        }
        [TestCase("")][TestCase("{")][TestCase("[]")][TestCase("null")]
        [TestCase("{}")][TestCase("{\"descriptions\":null}")]
        [TestCase("{\"descriptions\":{\"$ID\":\"unterminated}}")]
        [TestCase("{\"descriptions\":{}} trailing")]
        [TestCase("{\"descriptions\":{},\"descriptions\":{}}")]
        [TestCase("{\"descriptions\":{\"$ID\":\"a\",\"$ID\":\"b\"}}")]
        [TestCase("{\"descriptions\":{\"not-an-id\":\"a\"}}")]
        [TestCase("{\"descriptions\":{\"00000000000000000000000000000000\":\"a\"}}")]
        public void InvalidFileIsRejectedWithoutPublishingOrOverwriting(string json) { Write(json); RejectUnchanged(); }
        [TestCase("null")][TestCase("42")][TestCase("true")][TestCase("{}")][TestCase("[]")]
        [Timeout(3000)] public void NonStringDescriptionRejectsWholeDocument(string token)
        { Write("{\"descriptions\":{\"" + Guid.NewGuid().ToString("N") + "\":\"good\",\"$ID\":" + token + "}}"); RejectUnchanged(); }
        [TestCase(false)][TestCase(true)] public void InvalidUtf8IsNeverReplacedAndSaved(bool bom)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(Json("x")); bytes[Array.IndexOf(bytes, (byte)'x')] = 0xff;
            File.WriteAllBytes(path, bom ? new byte[] { 0xef, 0xbb, 0xbf }.Concat(bytes).ToArray() : bytes); RejectUnchanged();
        }
        [Test] public void InvalidUtf16SurrogateIsRejected()
        {
            byte[] bytes = Encoding.Unicode.GetBytes(Json("x")); int index = Array.IndexOf(bytes, (byte)'x'); bytes[index] = 0; bytes[index + 1] = 0xd8;
            File.WriteAllBytes(path, Encoding.Unicode.GetPreamble().Concat(bytes).ToArray()); RejectUnchanged();
        }
        [TestCase(false)][TestCase(true)] public void DamagedTranslationBytesCannotBecomeWritableReplacementCharacters(bool bom)
        {
            byte[] bytes = Encoding.UTF8.GetBytes("{\"translations\":{\"key\":\"x\"}}"); bytes[Array.IndexOf(bytes, (byte)'x')] = 0xff;
            if (bom) bytes = new byte[] { 0xef, 0xbb, 0xbf }.Concat(bytes).ToArray(); File.WriteAllBytes(source, bytes);
            Assert.Catch<Exception>(() => EditorProjectTextData.Load(config)); Assert.Throws<InvalidDataException>(() => Edit());
            Assert.That(File.ReadAllBytes(source), Is.EqualTo(bytes)); Assert.That(File.Exists(path), Is.False);
        }
        [Test] public void TranslationEditingPreservesUnicodeEncodingAndReadsTheSavedValue()
        {
            var encoding = new UnicodeEncoding(true, true, true);
            File.WriteAllBytes(source, encoding.GetPreamble().Concat(encoding.GetBytes("{\"translations\":{\"key\":\"Привет\"},\"custom\":42}")).ToArray());
            Assert.That(EditorProjectTextData.Load(config).TryGetDirect(entry, "en", out var before), Is.True); Assert.That(before.Value, Is.EqualTo("Привет"));
            using (var edit = Edit()) { edit.SetTranslation(entry, "en", "Добрый день"); edit.Commit(); }
            byte[] saved = File.ReadAllBytes(source); Assert.That(saved.Take(2), Is.EqualTo(encoding.GetPreamble()));
            Assert.That(encoding.GetString(saved), Does.Contain("custom").And.Contain("Добрый день"));
            Assert.That(EditorProjectTextData.Load(config).TryGetDirect(entry, "en", out var after), Is.True); Assert.That(after.Value, Is.EqualTo("Добрый день"));
        }
        [Test, Timeout(3000)] public void ExcessiveNestingRejectsUnknownFieldsToo()
        { Write("{\"descriptions\":{},\"extra\":" + new string('[', 100) + "0" + new string(']', 100) + "}"); RejectUnchanged(); }
        [Test] public void RepairRequiresAnExplicitSuccessfulReadBeforeStagingNewNotes()
        {
            Write("{"); using var edit = Edit(); Assert.Catch<Exception>(() => edit.SetDescription(entry, "lost"));
            Write(Json("repaired")); Assert.That(edit.GetDescription(entry), Is.EqualTo("repaired")); edit.SetDescription(entry, "edited"); edit.Commit();
            Assert.That(LocalizationProjectNotes.Load(config)[entry], Is.EqualTo("edited"));
        }
        [Test, Platform("Win")] public void ReadFailureDoesNotPublishAnEmptyWritableDocument()
        {
            Write(Json()); using var edit = Edit();
            using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None)) Assert.Catch<IOException>(() => edit.SetDescription(entry, "lost"));
            Assert.That(edit.GetDescription(entry), Is.EqualTo("original")); edit.Commit(); Assert.That(LocalizationProjectNotes.Load(config)[entry], Is.EqualTo("original"));
        }
        [TestCase(false)][TestCase(true)] public void ExternalChangeOrDeletionRequiresFreshEdit(bool delete)
        {
            Write(Json()); using var edit = Edit(); edit.SetDescription(entry, "local");
            if (delete) File.Delete(path); else Write(Json("external"));
            Assert.Throws<InvalidOperationException>(() => edit.Commit());
            if (delete) Assert.That(File.Exists(path), Is.False); else Assert.That(LocalizationProjectNotes.Load(config)[entry], Is.EqualTo("external"));
        }
        [Test] public void FileCreatedAfterReadingMissingNotesCannotBeOverwritten()
        {
            using var edit = Edit(); Assert.That(edit.GetDescription(entry), Is.Empty); Write(Json("external")); edit.SetDescription(entry, "local");
            Assert.Throws<InvalidOperationException>(() => edit.Commit()); Assert.That(LocalizationProjectNotes.Load(config)[entry], Is.EqualTo("external"));
        }
        [Test] public void UnknownFieldsAndForeignNotesSurviveRemovingTheLastKnownNote()
        {
            var data = JObject.Parse(Json()); string foreign = Guid.NewGuid().ToString("N"); data["descriptions"][foreign] = "Keep";
            data["custom"] = JObject.Parse("{\"text\":\"} ] \\\"\",\"date\":\"2026-01-02T00:00:00Z\",\"flags\":[true,null,7]}");
            Write(data.ToString()); using (var edit = Edit()) { edit.SetDescription(entry, null); edit.Commit(); }
            var written = JObject.Parse(File.ReadAllText(path)); Assert.That(JToken.DeepEquals(written["custom"], data["custom"]), Is.True);
            Assert.That(LocalizationProjectNotes.Load(config)[foreign], Is.EqualTo("Keep")); Assert.That(LocalizationProjectNotes.Load(config).ContainsKey(entry), Is.False);
        }
        [Test] public void MissingFileAndNullDescriptionDoNotCreateEmptyMetadata()
        { using var edit = Edit(); Assert.That(edit.GetDescription(entry), Is.Empty); edit.SetDescription(entry, null); edit.Commit(); Assert.That(File.Exists(path), Is.False); }
        [Test] public void UnsavedConfigCannotChooseAHiddenNotesDestination()
        {
            var unsaved = LocalizationProjectConfig.Create(new[] { "en" });
            try { Assert.Throws<InvalidOperationException>(() => LocalizationProjectNotes.GetAssetPath(unsaved)); }
            finally { UnityEngine.Object.DestroyImmediate(unsaved); }
        }
        [Test, Platform("Win")] public void ReplacementFailureKeepsNotesTranslationsAndPendingEditsForRetry()
        {
            Write(Json()); byte[] original = File.ReadAllBytes(path), beforeText = File.ReadAllBytes(source); using var edit = Edit();
            edit.SetDescription(entry, "changed"); edit.SetTranslation(entry, "en", "changed");
            // Managed file replacement fails before the transaction invokes any Unity import.
            using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)) Assert.Catch<IOException>(() => edit.Commit());
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(original)); Assert.That(File.ReadAllBytes(source), Is.EqualTo(beforeText));
            Assert.That(edit.GetDescription(entry), Is.EqualTo("changed")); edit.Commit(); Assert.That(LocalizationProjectNotes.Load(config)[entry], Is.EqualTo("changed"));
            Assert.That(Directory.GetFiles(Path.GetDirectoryName(path)).Where(p => p.Contains(".tmp") || p.Contains(".bak")), Is.Empty);
        }
        [TestCase("utf8")][TestCase("utf8bom")][TestCase("utf16le")][TestCase("utf16be")][TestCase("utf32le")][TestCase("utf32be")]
        public void RepeatedSavePreservesEncodingBomUnknownFieldsAndNoOpBytes(string kind)
        {
            Encoding encoding = kind switch { "utf8" => new UTF8Encoding(false, true), "utf8bom" => new UTF8Encoding(true, true), "utf16le" => new UnicodeEncoding(false, true, true), "utf16be" => new UnicodeEncoding(true, true, true), "utf32le" => new UTF32Encoding(false, true, true), _ => new UTF32Encoding(true, true, true) };
            byte[] bytes = encoding.GetPreamble().Concat(encoding.GetBytes(Json("Привет"))).ToArray(); File.WriteAllBytes(path, bytes);
            using (var edit = Edit()) { edit.SetDescription(entry, "Привет"); edit.Commit(); } Assert.That(File.ReadAllBytes(path), Is.EqualTo(bytes));
            using (var edit = Edit()) { edit.SetDescription(entry, "До свидания"); edit.Commit(); }
            byte[] saved = File.ReadAllBytes(path); Assert.That(saved.Take(encoding.GetPreamble().Length), Is.EqualTo(encoding.GetPreamble()));
            Assert.That(encoding.GetString(saved), Does.Contain("До свидания")); Assert.That(LocalizationProjectNotes.Load(config)[entry], Is.EqualTo("До свидания"));
            using (var edit = Edit()) { edit.GetDescription(entry); Assert.That(edit.Commit().HasChanges, Is.False); } Assert.That(File.ReadAllBytes(path), Is.EqualTo(saved));
        }
    }
}
