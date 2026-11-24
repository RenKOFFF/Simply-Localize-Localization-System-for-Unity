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
    public class EditorProjectAssetDataTests
    {
        private LocalizationProjectConfig config;
        private Texture2D texture;
        private AudioClip clip;
        private string entry, root;
        private Reader reader;
        [SetUp] public void Setup()
        {
            config = LocalizationProjectConfig.Create(new[] { "en", "ru", "pl" }); entry = config.Sets[0].AddEntry("asset", LocalizationEntryKind.Asset).Id;
            texture = new Texture2D(1, 1); clip = AudioClip.Create("Clip", 8, 1, 8000, false); reader = new Reader();
            foreach (var code in new[] { "en", "ru", "pl" })
            { config.Sets[0].SetSources(code, Source(code)); reader.Tables[code] = new() { ["asset"] = code == "en" ? clip : code == "ru" ? texture : null }; }
        }
        [TearDown] public void Cleanup()
        { if (root != null) AssetDatabase.DeleteAsset(root); Object.DestroyImmediate(config); if (texture != null) Object.DestroyImmediate(texture); if (clip != null) Object.DestroyImmediate(clip); }
        private static LocalizationSourceReference Source(string address, string provider = "memory") => new(provider, address, LocalizationSourceKind.AssetTable);
        private EditorProjectAssetData Load() => EditorProjectAssetData.Load(config, reader: reader);

        [Test] public void TypedFallbackSkipsWrongTypeWhileUntypedPreviewKeepsDirectValueAndNullEntryExists()
        {
            var data = Load();
            Assert.That(data.TryResolve(entry, "ru", out var direct), Is.True); Assert.That(direct.Asset, Is.SameAs(texture));
            Assert.That(data.TryResolve(entry, "ru", out var fallback, typeof(AudioClip)), Is.True); Assert.That(fallback.Asset, Is.SameAs(clip)); Assert.That(fallback.Language, Is.EqualTo("en"));
            Assert.That(data.TryGetDirect(entry, "pl", out var empty), Is.True); Assert.That(empty.Asset, Is.Null);
            Assert.That(data.TryResolve(entry, "pl", out fallback, typeof(Texture2D)), Is.False);
            Assert.Throws<ArgumentException>(() => data.TryResolve(entry, "en", out _, typeof(string)));
        }
        [Test] public void ReadsOnlyAssetSourcesInSelectedSetAndEqualKeysRemainSeparate()
        {
            var second = config.AddSet("Images"); string other = second.AddEntry("asset", LocalizationEntryKind.Asset).Id;
            second.SetSources("en", Source("images")); config.MainChannel.IncludeSet(second.Id); reader.Tables["images"] = new() { ["asset"] = texture };
            config.Sets[0].AddEntry("text"); config.Sets[0].SetSources("en", Source("en"), new("missing", "DoNotReadText", LocalizationSourceKind.TextTable));
            config.Languages[0].Appearance = new("missing", "DoNotReadAppearance", LocalizationSourceKind.Appearance);
            var first = Load(); Assert.That(reader.Reads, Is.EquivalentTo(new[] { "en", "ru", "pl" })); Assert.That(first.Entries.Count, Is.EqualTo(1));
            var next = EditorProjectAssetData.Load(config, setId: second.Id, reader: reader);
            Assert.That(next.TryResolve(other, "ru", out var value), Is.True); Assert.That(value.Asset, Is.SameAs(texture));
            Assert.That(first.TryGetDirect(entry, "en", out value), Is.True); Assert.That(value.Asset, Is.SameAs(clip));
        }
        [Test] public void SnapshotCopiesMappingsAndPolicyButDoesNotOwnUnityAssets()
        {
            var snapshot = Load(); reader.Tables["en"]["asset"] = texture; config.MainChannel.FallbackLanguage = null; config.Sets[0].RenameEntry(entry, "renamed");
            Assert.That(snapshot.TryResolve(entry, "pl", out var value), Is.True); Assert.That(value.Asset, Is.SameAs(clip));
            Assert.That(snapshot.Entries[entry], Is.EqualTo("asset")); Assert.That(Load().TryResolve(entry, "pl", out _), Is.False);
            Object.DestroyImmediate(clip); Assert.That(snapshot.TryResolve(entry, "pl", out _), Is.False);
            Assert.Throws<NotSupportedException>(() => ((IDictionary<string, string>)snapshot.Entries).Clear());
        }
        [Test] public void DuplicateRegisteredKeysFailAndUnregisteredOriginsAreKept()
        {
            reader.Tables["ru"]["extra"] = texture; reader.Tables["other"] = new() { ["extra"] = clip };
            config.Sets[0].SetSources("ru", Source("ru"), Source("other"));
            Assert.That(Load().GetUnregistered("ru").Select(c => c.Source.Address), Is.EquivalentTo(new[] { "ru", "other" }));
            reader.Tables["other"]["asset"] = clip;
            Assert.That(Assert.Throws<InvalidOperationException>(() => Load()).Message, Does.Contain("Duplicate asset key").And.Contain("other"));
        }
        [Test] public void TypedPreviewMatchesRuntimeWithoutChangingChannelOrProviderReadCount()
        {
            var providers = new LocalizationSourceRegistry(false); providers.Register("memory", reader);
            using var runtime = new LocalizationRuntime(config, providers); var initialization = runtime.InitializeAsync("ru");
            Assert.That(initialization.IsCompletedSuccessfully, Is.True); var active = runtime.GetChannel(); int reads = reader.RuntimeReads;
            Assert.That(active.TryGetAsset<AudioClip>("asset", out var runtimeValue), Is.True);
            Assert.That(Load().TryResolve(entry, "ru", out var editorValue, typeof(AudioClip)), Is.True);
            Assert.That(editorValue.Asset, Is.SameAs(runtimeValue.Asset)); Assert.That(editorValue.Language, Is.EqualTo(runtimeValue.Language));
            Assert.That(runtime.GetChannel(), Is.SameAs(active)); Assert.That(reader.RuntimeReads, Is.EqualTo(reads)); Assert.That(runtime.ReleaseAsync().IsCompletedSuccessfully, Is.True);
        }
        [Test] public void ExactResourcesTablePreviewValidatesMissingAmbiguousAndWrongTypeSources()
        {
            root = "Assets/SimplyLocalizeAssetPreview_" + Guid.NewGuid().ToString("N"); string address = Path.GetFileName(root);
            Directory.CreateDirectory(root + "/Resources"); AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var table = ScriptableObject.CreateInstance<LocalizationAssetTable>(); table.Set("asset", null);
            string path = root + "/Resources/" + address + ".asset"; AssetDatabase.CreateAsset(table, path); AssetDatabase.SaveAssetIfDirty(table);
            foreach (var code in new[] { "en", "ru", "pl" }) config.Sets[0].SetSources(code, Source(address, "resources"));
            var data = EditorProjectAssetData.Load(config); Assert.That(data.TryGetDirect(entry, "en", out var direct), Is.True); Assert.That(direct.AssetPath, Is.EqualTo(path));
            config.Sets[0].SetSources("en", Source("Missing", "resources"));
            Assert.That(Assert.Throws<InvalidOperationException>(() => EditorProjectAssetData.Load(config)).Message, Does.Contain("Missing"));
            string text = root + "/Resources/NotAnAssetTable.json"; File.WriteAllText(text, "{}"); AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            config.Sets[0].SetSources("en", Source("NotAnAssetTable", "resources"));
            Assert.That(Assert.Throws<InvalidOperationException>(() => EditorProjectAssetData.Load(config)).Message, Does.Contain("LocalizationAssetTable"));
            Directory.CreateDirectory(root + "/Duplicate/Resources"); File.Copy(path, root + "/Duplicate/Resources/" + address + ".asset");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport); config.Sets[0].SetSources("en", Source(address, "resources"));
            Assert.That(Assert.Throws<InvalidOperationException>(() => EditorProjectAssetData.Load(config)).Message, Does.Contain("Ambiguous"));
        }
        [Test] public void UnknownReaderAndUnavailableSelectionFailExplicitly()
        {
            Assert.That(Assert.Throws<InvalidOperationException>(() => EditorProjectAssetData.Load(config)).Message, Does.Contain("memory:en"));
            var other = config.AddSet("Unavailable"); Assert.Throws<ArgumentException>(() => EditorProjectAssetData.Load(config, setId: other.Id, reader: reader));
            Assert.Throws<ArgumentException>(() => Load().TryGetDirect(entry, "de", out _));
        }
        private sealed class Reader : ILocalizationEditorAssetReader, ILocalizationSourceProvider
        {
            internal readonly Dictionary<string, Dictionary<string, Object>> Tables = new();
            internal readonly List<string> Reads = new(); internal int RuntimeReads;
            public LocalizationEditorAssetSource Read(LocalizationSourceReference source) { Reads.Add(source.Address); return new(Tables[source.Address]); }
            public LocalizationProviderCapabilities GetCapabilities(LocalizationSourceKind kind) => new(LocalizationLoadUnit.Table, false, true);
            public Task<LocalizationSourceLease> LoadAsync(LocalizationSourceRequest request, CancellationToken token)
            { token.ThrowIfCancellationRequested(); RuntimeReads++; return Task.FromResult(new LocalizationSourceLease(LocalizationSourceData.FromAssets(Tables[request.Source.Address]))); }
        }
    }
}
