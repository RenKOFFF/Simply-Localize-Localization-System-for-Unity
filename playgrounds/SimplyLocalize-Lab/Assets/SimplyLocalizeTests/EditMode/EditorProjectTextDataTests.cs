using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using SimplyLocalize.Editor.Data;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SimplyLocalize.Tests
{
    public class EditorProjectTextDataTests
    {
        private LocalizationProjectConfig config;
        private string entry, root;
        private MemoryReader reader;
        [SetUp] public void Setup()
        {
            config = LocalizationProjectConfig.Create(new[] { "en", "ru", "pl" });
            entry = config.Sets[0].AddEntry("hello").Id;
            reader = new MemoryReader();
            foreach (string code in new[] { "en", "ru", "pl" })
            {
                config.Sets[0].SetSources(code, Source(code));
                reader.Tables[code] = new Dictionary<string, string> { ["hello"] = code == "en" ? "Hello" : "" };
            }
        }
        [TearDown] public void Cleanup()
        {
            if (root != null) AssetDatabase.DeleteAsset(root);
            Object.DestroyImmediate(config);
        }
        private static LocalizationSourceReference Source(string address, string provider = "memory") =>
            new(provider, address, LocalizationSourceKind.TextTable);
        private EditorProjectTextData Load() => EditorProjectTextData.Load(config, reader: reader);

        [Test] public void EqualKeysInDifferentSetsStaySeparateAndOnlySelectedSourcesAreRead()
        {
            var second = config.AddSet("Dialogue"); var other = second.AddEntry("hello");
            second.SetSources("en", Source("dialogue")); reader.Tables["dialogue"] = new() { ["hello"] = "Different" };
            config.MainChannel.IncludeSet(second.Id);
            var first = Load();
            Assert.That(reader.Reads, Is.EquivalentTo(new[] { "en", "ru", "pl" }));
            Assert.That(first.TryResolve(entry, "en", out var value), Is.True); Assert.That(value.Value, Is.EqualTo("Hello"));
            var secondData = EditorProjectTextData.Load(config, setId: second.Id, reader: reader);
            Assert.That(secondData.TryResolve(other.Id, "ru", out value), Is.True); Assert.That(value.Value, Is.EqualTo("Different"));
            Assert.Throws<ArgumentException>(() => secondData.TryResolve(entry, "en", out _));
        }
        [Test] public void DirectEmptyTranslationAndResolvedFallbackRemainDistinguishable()
        {
            var data = Load();
            Assert.That(data.TryGetDirect(entry, "ru", out var direct), Is.True); Assert.That(direct.Value, Is.Empty);
            Assert.That(data.TryResolve(entry, "RU", out var resolved), Is.True);
            Assert.That(resolved.Value, Is.EqualTo("Hello")); Assert.That(resolved.Language, Is.EqualTo("en"));
            Assert.That(resolved.Source.Address, Is.EqualTo("en"));
            config.MainChannel.FallbackLanguage = null;
            Assert.That(Load().TryResolve(entry, "ru", out _), Is.False);
        }
        [Test] public void SnapshotFreezesKeysAndFallbackUntilExplicitReload()
        {
            var before = Load(); config.Sets[0].RenameEntry(entry, "welcome"); config.MainChannel.FallbackLanguage = null;
            reader.Tables["en"]["hello"] = "Changed";
            Assert.That(before.Entries[entry], Is.EqualTo("hello"));
            Assert.That(before.TryResolve(entry, "ru", out var value), Is.True); Assert.That(value.Value, Is.EqualTo("Hello"));
            var after = Load(); Assert.That(after.Entries[entry], Is.EqualTo("welcome"));
            Assert.That(after.TryResolve(entry, "en", out _), Is.False);
            Assert.That(after.GetUnregistered("en").Single().Value, Is.EqualTo("Changed"));
        }
        [Test] public void UnregisteredFileEntriesAreVisibleButNeverBecomeRuntimeEntriesImplicitly()
        {
            reader.Tables["en"]["outside"] = "Extra";
            var before = Load(); Assert.That(before.GetUnregistered("en").Single().Key, Is.EqualTo("outside"));
            Assert.That(config.Sets[0].Entries.Count, Is.EqualTo(1));
            var added = config.Sets[0].AddEntry("outside"); var after = Load();
            Assert.That(after.GetUnregistered("en"), Is.Empty);
            Assert.That(after.TryGetDirect(added.Id, "en", out var cell), Is.True); Assert.That(cell.Value, Is.EqualTo("Extra"));
        }
        [Test] public void DuplicateRegisteredKeysFailWithBothAddressesAndLeavePreviousSnapshotUsable()
        {
            var before = Load(); reader.Tables["duplicate"] = new() { ["hello"] = "Other" };
            config.Sets[0].SetSources("en", Source("en"), Source("duplicate"));
            var error = Assert.Throws<InvalidOperationException>(() => Load());
            Assert.That(error.Message, Does.Contain("hello").And.Contain("en").And.Contain("duplicate"));
            Assert.That(before.TryResolve(entry, "ru", out var cell), Is.True); Assert.That(cell.Value, Is.EqualTo("Hello"));
        }
        [Test] public void UnregisteredDuplicateKeysPreserveEachOriginInsteadOfLosingOne()
        {
            reader.Tables["en"]["outside"] = "One"; reader.Tables["extra"] = new() { ["outside"] = "Two" };
            config.Sets[0].SetSources("en", Source("en"), Source("extra"));
            Assert.That(Load().GetUnregistered("en").Select(c => c.Source.Address), Is.EquivalentTo(new[] { "en", "extra" }));
        }
        [Test] public void TextViewDoesNotReadAssetOrAppearanceSources()
        {
            config.Sets[0].AddEntry("picture", LocalizationEntryKind.Asset);
            config.Sets[0].SetSources("en", Source("en"), new("memory", "images", LocalizationSourceKind.AssetTable));
            config.Languages[0].Appearance = new("memory", "font", LocalizationSourceKind.Appearance);
            var data = Load(); Assert.That(data.Entries.Count, Is.EqualTo(1));
            Assert.That(reader.Reads, Is.EquivalentTo(new[] { "en", "ru", "pl" }));
        }
        [Test] public void UnknownProviderFailsExplicitlyInsteadOfPublishingAnEmptyTable()
        {
            var error = Assert.Throws<InvalidOperationException>(() => EditorProjectTextData.Load(config));
            Assert.That(error.Message, Does.Contain("memory:en").And.Contain("Editor text reader"));
        }
        [Test] public void UnknownSelectionIsRejectedAndSnapshotCollectionsCannotBeMutated()
        {
            var data = Load(); var unavailable = config.AddSet("Unavailable");
            Assert.Throws<ArgumentException>(() => EditorProjectTextData.Load(config, setId: unavailable.Id, reader: reader));
            Assert.Throws<ArgumentException>(() => data.TryResolve(entry, "de", out _));
            Assert.Throws<NotSupportedException>(() => ((IDictionary<string, string>)data.Entries).Clear());
            Assert.Throws<NotSupportedException>(() => ((IList<string>)data.Languages).Clear());
        }
        [Test] public void EditorFallbackMatchesRuntimeWithoutChangingItsActiveLanguageOrSnapshot()
        {
            config.MainChannel.SetFallback("ru", "pl"); reader.Tables["pl"]["hello"] = "Czesc";
            var providers = new LocalizationSourceRegistry(false); providers.Register("memory", reader);
            using var runtime = new LocalizationRuntime(config, providers);
            Complete(runtime.InitializeAsync("ru")); var active = runtime.GetChannel();
            int runtimeReads = reader.RuntimeReads;
            var data = Load();
            Assert.That(data.TryResolve(entry, "ru", out var editorValue), Is.True);
            Assert.That(active.TryGetText("hello", out var runtimeValue), Is.True);
            Assert.That(editorValue.Value, Is.EqualTo(runtimeValue.Text)); Assert.That(editorValue.Language, Is.EqualTo(runtimeValue.Language));
            data.TryResolve(entry, "en", out _);
            Assert.That(runtime.GetChannel(), Is.SameAs(active)); Assert.That(active.RequestedLanguage, Is.EqualTo("ru"));
            Assert.That(reader.RuntimeReads, Is.EqualTo(runtimeReads)); Complete(runtime.ReleaseAsync());
        }
        [Test] public void DiskReloadSeesExternalChangesBeforeReimportAndDoesNotReadNeighborFiles()
        {
            string path = CreateDiskSource(); File.WriteAllText(root + "/Resources/unused.json", "not json");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var before = EditorProjectTextData.Load(config); File.WriteAllText(path, "{\"translations\":{\"hello\":\"External\"}}");
            var after = EditorProjectTextData.Load(config);
            before.TryGetDirect(entry, "en", out var old); after.TryGetDirect(entry, "en", out var changed);
            Assert.That(old.Value, Is.EqualTo("Disk")); Assert.That(changed.Value, Is.EqualTo("External"));
            Assert.That(changed.AssetPath, Is.EqualTo(path));
        }
        [Test] public void DuplicateResourcesAddressesAreRejectedInsteadOfChoosingAnArbitraryFile()
        {
            string path = CreateDiskSource();
            string duplicate = root + "/Other/Resources/" + Path.GetFileName(path);
            Directory.CreateDirectory(Path.GetDirectoryName(duplicate)); File.Copy(path, duplicate);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var error = Assert.Throws<InvalidOperationException>(() => EditorProjectTextData.Load(config));
            Assert.That(error.Message, Does.Contain("Ambiguous").And.Contain(path).And.Contain(duplicate));
        }
        [Test] public void BrokenOrMissingSourceReportsExactAddressAndNoPartialSnapshot()
        {
            string path = CreateDiskSource(); File.WriteAllText(path, "not json");
            var error = Assert.Throws<InvalidOperationException>(() => EditorProjectTextData.Load(config));
            Assert.That(error.Message, Does.Contain(Path.GetFileNameWithoutExtension(path)).And.Contain("en"));
            File.Delete(path);
            Assert.Throws<InvalidOperationException>(() => EditorProjectTextData.Load(config));
        }
        [Test] public void ResourcesAddressExcludesEditorOnlyContent()
        {
            Assert.That(ResourcesEditorTextReader.ResourceAddress("Assets/Editor/Resources/menu.json"), Is.Null);
            Assert.That(ResourcesEditorTextReader.ResourceAddress("Assets/Resources/Editor/menu.json"), Is.Null);
            Assert.That(ResourcesEditorTextReader.ResourceAddress("Packages/example/Resources/Text/menu.json"), Is.EqualTo("Text/menu"));
        }
        [Test] public void LocalUpmSourceUsesResolvedDiskPathAndCannotBeWrittenByProjectEdits()
        {
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(LocalizationRuntime).Assembly);
            Assert.That(package.source, Is.EqualTo(UnityEditor.PackageManager.PackageSource.Local), "This repository test runs against its local UPM package.");
            string name = "EditorSourceRegression_" + Guid.NewGuid().ToString("N");
            string directory = Path.GetFullPath(Path.Combine(package.resolvedPath, name));
            Assert.That(directory.StartsWith(Path.GetFullPath(package.resolvedPath) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), Is.True);
            string logical = package.assetPath + "/" + name + "/Resources/" + name + ".json";
            string physical = Path.Combine(directory, "Resources", name + ".json");
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes("{\"translations\":{\"hello\":\"Package\"}}");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(physical)); File.WriteAllBytes(physical, bytes);
                root = "Assets/" + name; Directory.CreateDirectory(root);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                foreach (var code in new[] { "en", "ru", "pl" }) config.Sets[0].SetSources(code, Source(name, "resources"));
                AssetDatabase.CreateAsset(config, root + "/Config.asset"); AssetDatabase.SaveAssetIfDirty(config);
                Assert.That(Path.GetFullPath(ResourcesEditorTextReader.ResolveFilePath(logical)), Is.EqualTo(physical));
                var data = EditorProjectTextData.Load(config);
                Assert.That(data.TryResolve(entry, "ru", out var cell), Is.True); Assert.That(cell.Value, Is.EqualTo("Package"));
                Assert.That(cell.AssetPath, Is.EqualTo(logical));
                using (var metadata = new LocalizationProjectEdit(config, config.Sets[0].Id))
                { metadata.Draft.Languages[0].DisplayName = "English metadata"; metadata.Commit(); }
                using (var text = new LocalizationProjectEdit(config, config.Sets[0].Id))
                { text.SetTranslation(entry, "en", "Changed"); Assert.Throws<InvalidOperationException>(() => text.Commit()); }
                Assert.That(File.ReadAllBytes(physical), Is.EqualTo(bytes));
            }
            finally
            {
                // Only the unique fixture created above belongs to this test, never a package/cache root.
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
                if (File.Exists(directory + ".meta")) File.Delete(directory + ".meta");
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            }
        }
        private string CreateDiskSource()
        {
            root = "Assets/SimplyLocalizeEditorProjectTests_" + Guid.NewGuid().ToString("N");
            string address = Path.GetFileName(root); string path = root + "/Resources/" + address + ".json";
            Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, "{\"translations\":{\"hello\":\"Disk\"}}");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            foreach (string code in new[] { "en", "ru", "pl" }) config.Sets[0].SetSources(code, Source(address, "resources"));
            return path;
        }
        private static void Complete(Task task)
        { Assert.That(task.IsCompleted, Is.True, "In-memory provider must not suspend."); task.GetAwaiter().GetResult(); }
        private sealed class MemoryReader : ILocalizationEditorTextReader, ILocalizationSourceProvider
        {
            internal readonly Dictionary<string, Dictionary<string, string>> Tables = new();
            internal readonly List<string> Reads = new();
            internal int RuntimeReads;
            public LocalizationEditorTextSource Read(LocalizationSourceReference source)
            { Reads.Add(source.Address); return new LocalizationEditorTextSource(Tables[source.Address]); }
            public LocalizationProviderCapabilities GetCapabilities(LocalizationSourceKind kind) => new(LocalizationLoadUnit.Table, false, true);
            public Task<LocalizationSourceLease> LoadAsync(LocalizationSourceRequest request, CancellationToken token)
            {
                token.ThrowIfCancellationRequested(); RuntimeReads++;
                var values = Tables[request.Source.Address].Where(p => request.RequestedKeys == null || request.RequestedKeys.Contains(p.Key))
                    .ToDictionary(p => p.Key, p => p.Value);
                return Task.FromResult(new LocalizationSourceLease(LocalizationSourceData.FromText(values)));
            }
        }
    }
}
