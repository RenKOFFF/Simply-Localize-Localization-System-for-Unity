using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace SimplyLocalize.Tests
{
    public class ChannelSnapshotTests
    {
        private LocalizationProjectConfig config;
        private readonly List<LocalizationRuntime> runtimes = new();
        private readonly List<LocalizationChannelLease> snapshots = new();
        private Source source;
        private Texture2D texture;
        private LocalizationRuntime current;
        [SetUp] public void Setup()
        {
            config = LocalizationProjectConfig.Create(new[] { "en", "ru", "pl" });
            config.MainChannel.FallbackLanguage = "en"; config.MainChannel.SetFallback("ru", "pl");
            var set = config.Sets[0];
            foreach (string key in new[] { "caption", "global", "count", "direct" }) set.AddEntry(key);
            set.AddEntry("picture", LocalizationEntryKind.Asset);
            Sources(true, true);
            texture = new Texture2D(1, 1); source = new Source();
            source.Text["en"] = new() { ["caption"] = "English", ["global"] = "Global", ["count"] = "{0} items" };
            source.Text["pl"] = new() { ["caption"] = "Polish" };
            source.Text["ru"] = new() { ["direct"] = "Russian", ["caption"] = "" };
            source.Assets["pl"] = new() { ["picture"] = texture };
            current = Runtime(); Completed(current.InitializeAsync()); source.Reads = 0;
        }
        private void Sources(bool text, bool assets)
        {
            foreach (string code in new[] { "en", "ru", "pl" })
            {
                var descriptors = new List<LocalizationSourceReference>();
                if (text) descriptors.Add(new("test", "text", LocalizationSourceKind.TextTable));
                if (assets) descriptors.Add(new("test", "assets", LocalizationSourceKind.AssetTable));
                config.Sets[0].SetSources(code, descriptors.ToArray());
            }
        }
        private LocalizationRuntime Runtime()
        {
            var registry = new LocalizationSourceRegistry(false); registry.Register("test", source);
            var runtime = new LocalizationRuntime(config, registry); runtimes.Add(runtime); return runtime;
        }
        private static void Completed(Task task)
        {
            Assert.That(task.IsCompleted, Is.True, "In-memory operation unexpectedly suspended.");
            task.GetAwaiter().GetResult();
        }
        private static IEnumerator Done(Task task, bool success = true)
        {
            float deadline = Time.realtimeSinceStartup + 10;
            while (!task.IsCompleted && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(task.IsCompleted, Is.True); if (success) task.GetAwaiter().GetResult();
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            source.Pending?.TrySetCanceled();
            foreach (var runtime in runtimes) yield return Done(runtime.ReleaseAsync());
            foreach (var snapshot in snapshots) yield return Done(snapshot.ReleaseAsync());
            Assert.That(source.Active, Is.Zero); Assert.That(source.Disposed, Is.False);
            runtimes.Clear(); snapshots.Clear(); Object.DestroyImmediate(texture); Object.DestroyImmediate(config);
        }
        [Test] public void ReadsBeforeInitializationRejectWithoutLoadingAnything()
        {
            var runtime = Runtime(); int reads = source.Reads;
            Assert.Throws<InvalidOperationException>(() => runtime.Get("caption"));
            Assert.Throws<InvalidOperationException>(() => runtime.GetAsset<Texture2D>("picture"));
            Assert.That(source.Reads, Is.EqualTo(reads));
        }
        [UnityTest] public IEnumerator PreparationKeepsOldStateThenPublishesTheEntireFallbackChain()
        {
            source.Delay = true; int events = 0;
            current.Changed += (runtime, _) =>
            {
                events++; Assert.That(runtime.GetAsset<Texture2D>("picture"), Is.SameAs(texture));
                Assert.That(runtime.GetChannel().TryGetText("global", out var value), Is.True);
                Assert.That(value.Language, Is.EqualTo("en"));
            };
            var switching = current.SwitchLanguageAsync("ru");
            Assert.That(switching.IsCompleted, Is.False); Assert.That(current.Get("caption"), Is.EqualTo("English")); Assert.That(events, Is.Zero);
            source.Complete(); yield return Done(switching); Assert.That(events, Is.EqualTo(1));
            Assert.That(current.GetChannel().TryGetText("caption", out var result), Is.True);
            Assert.That(result.Text, Is.EqualTo("Polish")); Assert.That(result.Language, Is.EqualTo("pl")); Assert.That(result.RequestedLanguage, Is.EqualTo("ru"));
            Assert.That(current.GetChannel().TryGetAsset<Texture2D>("picture", out var asset), Is.True); Assert.That(asset.Language, Is.EqualTo("pl"));
            int reads = source.Reads;
            for (int i = 0; i < 100; i++)
            {
                Assert.That(current.GetChannel().TryGetText("missing", out _), Is.False);
                Assert.That(current.GetChannel().TryGetAsset<AudioClip>("picture", out _), Is.False);
                Assert.That(current.Get("count", 2), Is.EqualTo("2 items"));
            }
            Assert.That(source.Reads, Is.EqualTo(reads));
        }
        [Test] public void ReleasingRetainedSnapshotDoesNotInvalidateRuntimeOrAnotherOwner()
        {
            Completed(current.SwitchLanguageAsync("ru"));
            var other = Runtime(); Completed(other.InitializeAsync("ru"));
            var retained = current.GetChannel().Retain(); snapshots.Add(retained);
            Completed(retained.ReleaseAsync()); Completed(retained.ReleaseAsync());
            Assert.That(current.GetAsset<Texture2D>("picture"), Is.SameAs(texture));
            Completed(current.ReleaseAsync());
            Assert.That(other.GetAsset<Texture2D>("picture"), Is.SameAs(texture)); Assert.That(source.Disposed, Is.False);
        }
        [Test] public void ProviderBuffersAreCopiedBeforePublishing()
        {
            Completed(current.SwitchLanguageAsync("ru"));
            source.Text["pl"]["caption"] = "changed"; source.Assets["pl"].Clear();
            Assert.That(current.Get("caption"), Is.EqualTo("Polish")); Assert.That(current.GetAsset<Texture2D>("picture"), Is.SameAs(texture));
        }
        [TestCase("text")] [TestCase("assets")] [TestCase("nullText")] [TestCase("nullAssets")]
        public void FailedPreparationPreservesStateAndCleansItsPartialOwners(string failure)
        {
            int events = 0, owners = source.Active; current.Changed += (_, _) => events++; source.Failure = failure;
            var failed = current.SwitchLanguageAsync("ru"); Assert.That(failed.IsFaulted, Is.True); _ = failed.Exception;
            Assert.That(current.GetChannel().RequestedLanguage, Is.EqualTo("en")); Assert.That(current.Get("caption"), Is.EqualTo("English"));
            Assert.That(events, Is.Zero); Assert.That(source.Active, Is.EqualTo(owners)); Assert.That(source.Disposed, Is.False);
        }
        [TestCase(false)] [TestCase(true)]
        public void CancellationBeforeOrDuringSynchronousSourceCallDoesNotPublish(bool during)
        {
            using var cancel = new CancellationTokenSource();
            if (during) source.OnLoad = () => cancel.Cancel(); else cancel.Cancel();
            int owners = source.Active; var task = current.SwitchLanguageAsync("ru", cancel.Token);
            Assert.That(task.IsCanceled, Is.True); Assert.That(source.Reads, Is.EqualTo(during ? 1 : 0));
            Assert.That(source.Active, Is.EqualTo(owners)); Assert.That(current.GetChannel().RequestedLanguage, Is.EqualTo("en"));
        }
        [Test] public void ReleasedSnapshotRejectsReadsAndRetain()
        {
            var snapshot = current.GetChannel();
            Completed(current.SwitchLanguageAsync("ru"));
            Assert.Throws<ObjectDisposedException>(() => snapshot.TryGetText("caption", out _));
            Assert.Throws<ObjectDisposedException>(() => snapshot.Retain());
        }
        [TestCase(true)] [TestCase(false)]
        public void SelectedSourceKindsDoNotLoadTheOtherCategory(bool textOnly)
        {
            Sources(textOnly, !textOnly); var runtime = Runtime(); source.Reads = source.TextReads = source.AssetReads = 0;
            Completed(runtime.InitializeAsync("ru"));
            Assert.That(source.Reads, Is.EqualTo(3));
            Assert.That(textOnly ? source.AssetReads : source.TextReads, Is.Zero);
            Assert.That(runtime.GetChannel().TryGetText("caption", out _), Is.EqualTo(textOnly));
            Assert.That(runtime.GetAsset<Texture2D>("picture") != null, Is.EqualTo(!textOnly));
        }
        [UnityTest] public IEnumerator FrozenDefinitionIsNotInvalidatedByAuthoringChangesDuringPreparation()
        {
            source.Delay = true; var switching = current.SwitchLanguageAsync("ru");
            config.MainChannel.SetFallback("ru", "en"); config.Sets[0].SetSources("pl");
            source.Complete(); yield return Done(switching);
            Assert.That(current.Get("caption"), Is.EqualTo("Polish"));
            var replacement = Runtime(); yield return Done(replacement.InitializeAsync("ru"));
            Assert.That(replacement.Get("caption"), Is.EqualTo("English"));
        }
        [UnityTest] public IEnumerator AsyncFailureAfterPartialWorkPreservesOldStateAndCallerOwnedProvider()
        {
            source.Delay = true; source.Failure = "assets"; int owners = source.Active;
            var switching = current.SwitchLanguageAsync("ru"); source.Complete(); yield return Done(switching, false);
            Assert.That(switching.IsFaulted, Is.True); Assert.That(current.Get("caption"), Is.EqualTo("English"));
            Assert.That(source.Active, Is.EqualTo(owners)); Assert.That(source.Disposed, Is.False);
        }
        [UnityTest] public IEnumerator CancellationWaitsForUncooperativeSourceCleanupBeforeCompletion()
        {
            using var cancel = new CancellationTokenSource(); source.Delay = true; int owners = source.Active;
            var switching = current.SwitchLanguageAsync("ru", cancel.Token); cancel.Cancel();
            Assert.That(switching.IsCompleted, Is.False, "The new contract drains native/provider work before reporting completion.");
            Assert.That(current.Get("caption"), Is.EqualTo("English"));
            source.Complete(); yield return Done(switching, false);
            Assert.That(switching.IsCanceled, Is.True); Assert.That(source.Active, Is.EqualTo(owners)); Assert.That(source.Disposed, Is.False);
        }
        private sealed class Source : ILocalizationSourceProvider, IDisposable
        {
            internal readonly Dictionary<string, Dictionary<string, string>> Text = new();
            internal readonly Dictionary<string, Dictionary<string, Object>> Assets = new();
            internal int Reads, TextReads, AssetReads, Active;
            internal bool Delay, Disposed;
            internal string Failure;
            internal Action OnLoad;
            internal TaskCompletionSource<LocalizationSourceLease> Pending;
            private LocalizationSourceRequest request;
            public LocalizationProviderCapabilities GetCapabilities(LocalizationSourceKind kind) => new(LocalizationLoadUnit.Table, true, false);
            public Task<LocalizationSourceLease> LoadAsync(LocalizationSourceRequest value, CancellationToken token)
            {
                Reads++; OnLoad?.Invoke();
                bool text = value.Source.Kind == LocalizationSourceKind.TextTable;
                if (text) TextReads++; else AssetReads++;
                if (Delay && value.Language == "ru" && text) { request = value; Pending = new(); return Pending.Task; }
                if (value.Language == "pl")
                {
                    if (Failure == (text ? "text" : "assets")) throw new InvalidOperationException("injected " + Failure);
                    if (Failure == (text ? "nullText" : "nullAssets")) return Task.FromResult<LocalizationSourceLease>(null);
                }
                return Task.FromResult(Data(value));
            }
            private LocalizationSourceLease Data(LocalizationSourceRequest value)
            {
                var data = value.Source.Kind == LocalizationSourceKind.TextTable
                    ? LocalizationSourceData.FromText(Text.TryGetValue(value.Language, out var text) ? text : new())
                    : LocalizationSourceData.FromAssets(Assets.TryGetValue(value.Language, out var assets) ? assets : new());
                Active++; return new LocalizationSourceLease(data, () => { Active--; return Task.CompletedTask; });
            }
            internal void Complete() { Delay = false; Pending.SetResult(Data(request)); }
            public void Dispose() => Disposed = true;
        }
    }
}
