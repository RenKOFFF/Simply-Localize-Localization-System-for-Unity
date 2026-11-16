using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using SimplyLocalize.Data;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace SimplyLocalize.Tests
{
    public class PreparedLocalizationTests
    {
        private readonly List<Object> _objects = new();
        private LocalizationConfig _config;
        private LocalizationManager _manager;
        private Provider _provider;
        private Texture2D _texture;
        private T Create<T>() where T : ScriptableObject { var obj = ScriptableObject.CreateInstance<T>(); _objects.Add(obj); return obj; }
        [SetUp] public void Setup()
        {
            _config = Create<LocalizationConfig>();
            foreach (string code in new[] { "en", "ru", "pl" })
            { var profile = Create<LanguageProfile>(); profile.languageCode = code; _config.languages.Add(profile); }
            _config.defaultLanguage = _config.fallbackLanguage = _config.languages[0];
            _config.languages[1].fallbackProfile = _config.languages[2];
            _provider = new Provider();
            _provider.Text["en"] = new Dictionary<string, string> { ["caption"] = "English", ["global"] = "Global", ["count"] = "{0} items" };
            _provider.Text["pl"] = new Dictionary<string, string> { ["caption"] = "Polish" };
            _provider.Text["ru"] = new Dictionary<string, string> { ["direct"] = "Russian", ["caption"] = "" };
            _texture = new Texture2D(1, 1); _objects.Add(_texture);
            var table = Create<LocalizationAssetTable>(); table.Set("picture", _texture);
            _provider.Tables["pl"] = new List<LocalizationAssetTable> { table };
            _manager = new LocalizationManager(_config); _manager.SetDataProvider(_provider); _manager.SetLanguage("en");
            _provider.Reads = 0;
        }
        [TearDown] public void Cleanup()
        {
            _provider.Gate?.TrySetCanceled();
            _manager.ClearCaches();
            for (int i = _objects.Count - 1; i >= 0; i--) if (_objects[i]) Object.DestroyImmediate(_objects[i]);
            _objects.Clear();
        }

        [Test] public void LegacyFirstAssetLookupStillLoads_WhileReadyLookupNeverDoes()
        {
            _manager.SetLanguage("ru"); int before = _provider.Reads;
            Assert.That(_manager.ResolvePreparedAsset<Texture2D>("picture").Status, Is.EqualTo(LocalizationResolveStatus.NotPrepared));
            Assert.That(_provider.Reads, Is.EqualTo(before));
            Assert.That(_manager.GetAsset<Texture2D>("picture"), Is.SameAs(_texture));
            Assert.That(_provider.Reads, Is.GreaterThan(before));
        }

        [Test] public void PreparationIsDetached_ApplyPublishesCompleteFallbackStateWithoutReads()
        {
            int events = 0;
            _manager.OnLanguageChanged += code =>
            {
                events++; Assert.That(code, Is.EqualTo("ru"));
                Assert.That(_manager.ResolvePreparedAsset<Texture2D>("picture").Value, Is.SameAs(_texture));
                Assert.That(_manager.ResolvePreparedText("global").ResolvedLanguage, Is.EqualTo("en"));
            };
            using var prepared = _manager.PrepareLanguage("ru");
            Assert.That(prepared.TextLanguageCount, Is.EqualTo(3)); Assert.That(prepared.AssetLanguageCount, Is.EqualTo(3));
            Assert.That(_manager.CurrentLanguage, Is.EqualTo("en")); Assert.That(events, Is.Zero);
            int reads = _provider.Reads;
            _manager.ApplyPreparedLanguage(prepared);
            Assert.That(events, Is.EqualTo(1));
            var text = _manager.ResolvePreparedText("caption");
            Assert.That(text.Value, Is.EqualTo("Polish")); Assert.That(text.RequestedLanguage, Is.EqualTo("ru"));
            Assert.That(text.ResolvedLanguage, Is.EqualTo("pl")); Assert.That(text.IsFallback, Is.True);
            var asset = _manager.ResolvePreparedAsset<Texture2D>("picture"); Assert.That(asset.ResolvedLanguage, Is.EqualTo("pl"));
            for (int i = 0; i < 100; i++)
            {
                Assert.That(_manager.ResolvePreparedText("missing").Status, Is.EqualTo(LocalizationResolveStatus.Missing));
                Assert.That(_manager.ResolvePreparedAsset<AudioClip>("picture").Status, Is.EqualTo(LocalizationResolveStatus.Missing));
                Assert.That(_manager.Get("count", new object[] { 2 }), Is.EqualTo("2 items"));
            }
            Assert.That(_provider.Reads, Is.EqualTo(reads));
        }

        [Test] public void DisposeOfPreparedHandle_DoesNotInvalidateAppliedDataOrSharedOwner()
        {
            var other = new LocalizationManager(_config); other.SetDataProvider(_provider);
            using var first = _manager.PrepareLanguage("ru"); using var second = other.PrepareLanguage("ru");
            _manager.ApplyPreparedLanguage(first); other.ApplyPreparedLanguage(second);
            first.Dispose(); first.Dispose(); second.Dispose(); _manager.ClearCaches();
            Assert.That(first.TextLanguageCount, Is.Zero); Assert.That(first.AssetLanguageCount, Is.Zero);
            Assert.That(other.ResolvePreparedAsset<Texture2D>("picture").Value, Is.SameAs(_texture));
            Assert.That(_texture != null, Is.True); Assert.That(_provider.Disposed, Is.False);
            Assert.That(_manager.ResolvePreparedAsset<Texture2D>("picture").Status, Is.EqualTo(LocalizationResolveStatus.NotPrepared));
            other.ClearCaches();
        }

        [Test] public void ProviderBuffersAreCopiedBeforePublishing()
        {
            using var prepared = _manager.PrepareLanguage("ru");
            _provider.Text["pl"]["caption"] = "changed"; _provider.Tables["pl"].Clear();
            _manager.ApplyPreparedLanguage(prepared);
            Assert.That(_manager.ResolvePreparedText("caption").Value, Is.EqualTo("Polish"));
            Assert.That(_manager.ResolvePreparedAsset<Texture2D>("picture").Value, Is.SameAs(_texture));
        }

        [TestCase("text")] [TestCase("assets")] [TestCase("nullText")] [TestCase("nullAssets")]
        public void FailedPreparation_PreservesActiveStateAndDoesNotRaiseEvents(string failure)
        {
            int events = 0; _manager.OnLanguageChanged += _ => events++;
            _provider.Fail = failure;
            Assert.That(() => _manager.PrepareLanguage("ru"), Throws.Exception);
            Assert.That(_manager.CurrentLanguage, Is.EqualTo("en")); Assert.That(_manager.Get("caption"), Is.EqualTo("English"));
            Assert.That(events, Is.Zero); Assert.That(_provider.Disposed, Is.False);
        }

        [TestCase(false)] [TestCase(true)]
        public void Cancellation_BeforeOrDuringSyncCalls_DoesNotApplyPartialData(bool during)
        {
            using var cancellation = new CancellationTokenSource();
            if (during) _provider.OnText = _ => cancellation.Cancel(); else cancellation.Cancel();
            Assert.Throws<OperationCanceledException>(() => _manager.PrepareLanguage("ru", true, cancellation.Token));
            Assert.That(_provider.Reads, Is.EqualTo(during ? 1 : 0)); Assert.That(_manager.CurrentLanguage, Is.EqualTo("en"));
        }

        [TestCase("clear")] [TestCase("reload")] [TestCase("provider")] [TestCase("language")]
        [TestCase("fallback")] [TestCase("textFlag")] [TestCase("resourcesPath")]
        public void StalePreparation_IsRejectedBeforeChangingActiveState(string mutation)
        {
            using var prepared = _manager.PrepareLanguage("ru");
            switch (mutation)
            {
                case "clear": _manager.ClearCaches(); break;
                case "reload": _manager.Reload(); break;
                case "provider": _manager.SetDataProvider(new Provider()); break;
                case "language": _manager.SetLanguage("pl"); break;
                case "fallback": _config.languages[1].fallbackProfile = null; break;
                case "textFlag": _config.loadTextData = false; break;
                case "resourcesPath": _config.resourcesBasePath = "Other"; break;
            }
            string language = _manager.CurrentLanguage;
            Assert.Throws<InvalidOperationException>(() => _manager.ApplyPreparedLanguage(prepared));
            Assert.That(_manager.CurrentLanguage, Is.EqualTo(language));
        }

        [Test] public void DisposedAndForeignHandles_AreRejected()
        {
            var other = new LocalizationManager(_config);
            using var prepared = _manager.PrepareLanguage("ru");
            Assert.Throws<ArgumentException>(() => other.ApplyPreparedLanguage(prepared));
            prepared.Dispose(); Assert.Throws<ObjectDisposedException>(() => _manager.ApplyPreparedLanguage(prepared));
            Assert.Throws<ArgumentNullException>(() => _manager.ApplyPreparedLanguage(null));
        }

        [Test] public void TextOnlyAndAssetOnlyPreparation_DoNotLoadTheDisabledCategory()
        {
            using (var text = _manager.PrepareLanguage("ru", false))
            {
                Assert.That(text.AssetLanguageCount, Is.Zero); Assert.That(_provider.Reads, Is.EqualTo(3));
                _manager.ApplyPreparedLanguage(text);
                Assert.That(_manager.ResolvePreparedAsset<Texture2D>("picture").Status, Is.EqualTo(LocalizationResolveStatus.NotPrepared));
            }
            _config.loadTextData = false; _provider.Reads = 0;
            using var assets = _manager.PrepareLanguage("ru");
            Assert.That(assets.TextLanguageCount, Is.Zero); Assert.That(_provider.Reads, Is.EqualTo(3));
            _manager.ApplyPreparedLanguage(assets);
            Assert.That(_manager.ResolvePreparedText("caption").Status, Is.EqualTo(LocalizationResolveStatus.NotPrepared));
        }

        [Test] public void PreparedSwitches_KeepOnlyTheAppliedChainAndClearTemplates()
        {
            for (int i = 0; i < 10; i++)
            {
                using var ru = _manager.PrepareLanguage("ru"); _manager.ApplyPreparedLanguage(ru);
                _manager.Get("count", new object[] { 2 });
                using var en = _manager.PrepareLanguage("en"); _manager.ApplyPreparedLanguage(en);
                Assert.That(((IDictionary)typeof(LocalizationManager).GetField("_textCache", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_manager)).Count, Is.EqualTo(1));
                Assert.That(((IDictionary)typeof(LocalizationManager).GetField("_assetTableCache", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_manager)).Count, Is.EqualTo(1));
                Assert.That(((IDictionary)typeof(LocalizationManager).GetField("_templateCache", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_manager)).Count, Is.Zero);
            }
        }

        [Test] public void EventFailureOccursAfterCommit_AndDoesNotPretendToRollBack()
        {
            using var prepared = _manager.PrepareLanguage("ru");
            _manager.OnLanguageChanged += _ => throw new InvalidOperationException("observer failure");
            Assert.Throws<InvalidOperationException>(() => _manager.ApplyPreparedLanguage(prepared));
            Assert.That(_manager.CurrentLanguage, Is.EqualTo("ru"));
            Assert.That(_manager.ResolvePreparedAsset<Texture2D>("picture").Value, Is.SameAs(_texture));
        }

        [Test] public void ResourcesAsyncPreparation_IsExplicitlyUnsupportedWithoutAsyncProvider()
        {
            var manager = new LocalizationManager(_config);
            var task = manager.PrepareLanguageAsync("en");
            Assert.That(task.IsFaulted, Is.True); Assert.That(task.Exception.InnerException, Is.TypeOf<NotSupportedException>());
            Assert.That(manager.CurrentLanguage, Is.Null);
        }

        [UnityTest] public IEnumerator AsyncPreparation_PublishesOnlyAfterAwaitAndExplicitApply()
        {
            _provider.Gate = new TaskCompletionSource<Dictionary<string, string>>();
            var task = _manager.PrepareLanguageAsync("ru");
            Assert.That(task.IsCompleted, Is.False); Assert.That(_manager.CurrentLanguage, Is.EqualTo("en"));
            _provider.Gate.SetResult(_provider.Text["ru"]);
            while (!task.IsCompleted) yield return null;
            Assert.That(task.IsFaulted, Is.False, task.Exception?.ToString());
            using var prepared = task.Result;
            Assert.That(_manager.CurrentLanguage, Is.EqualTo("en"));
            _manager.ApplyPreparedLanguage(prepared);
            Assert.That(_manager.ResolvePreparedAsset<Texture2D>("picture").Value, Is.SameAs(_texture));
        }

        [UnityTest] public IEnumerator CancellationAfterAsyncProviderCompletes_StillCannotPublish()
        {
            using var cancellation = new CancellationTokenSource();
            _provider.Gate = new TaskCompletionSource<Dictionary<string, string>>();
            var task = _manager.PrepareLanguageAsync("ru", true, cancellation.Token);
            cancellation.Cancel(); _provider.Gate.SetResult(_provider.Text["ru"]); // Deliberately uncooperative provider.
            while (!task.IsCompleted) yield return null;
            Assert.That(task.IsCanceled, Is.True); Assert.That(_manager.CurrentLanguage, Is.EqualTo("en"));
        }

        [UnityTest] public IEnumerator ConcurrentAsyncResults_CannotOverwriteANewerAppliedState()
        {
            _provider.Gate = new TaskCompletionSource<Dictionary<string, string>>();
            var old = _manager.PrepareLanguageAsync("ru");
            using var fresh = _manager.PrepareLanguage("pl"); _manager.ApplyPreparedLanguage(fresh);
            _provider.Gate.SetResult(_provider.Text["ru"]);
            while (!old.IsCompleted) yield return null;
            Assert.That(old.IsFaulted, Is.True); Assert.That(old.Exception.InnerException, Is.TypeOf<InvalidOperationException>());
            Assert.That(_manager.CurrentLanguage, Is.EqualTo("pl"));
        }

        [Test] public void CancellationBeforeApply_PreservesStateAndLeavesHandleUsable()
        {
            using var prepared = _manager.PrepareLanguage("ru");
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            Assert.Throws<OperationCanceledException>(() => _manager.ApplyPreparedLanguage(prepared, cancellation.Token));
            Assert.That(_manager.CurrentLanguage, Is.EqualTo("en"));
            _manager.ApplyPreparedLanguage(prepared); Assert.That(_manager.CurrentLanguage, Is.EqualTo("ru"));
        }

        [UnityTest] public IEnumerator AsyncFailureAfterPartialWork_DoesNotPublishOrDisposeTheProvider()
        {
            _provider.Gate = new TaskCompletionSource<Dictionary<string, string>>(); _provider.Fail = "assets";
            var task = _manager.PrepareLanguageAsync("ru");
            _provider.Gate.SetResult(_provider.Text["ru"]);
            while (!task.IsCompleted) yield return null;
            Assert.That(task.IsFaulted, Is.True); Assert.That(task.Exception.InnerException, Is.TypeOf<InvalidOperationException>());
            Assert.That(_manager.Get("caption"), Is.EqualTo("English")); Assert.That(_manager.CurrentLanguage, Is.EqualTo("en"));
            Assert.That(_provider.Disposed, Is.False);
        }

        [UnityTest] public IEnumerator Cancellation_DoesNotWaitForAnUncooperativeAsyncProvider()
        {
            using var cancellation = new CancellationTokenSource();
            _provider.Gate = new TaskCompletionSource<Dictionary<string, string>>();
            var task = _manager.PrepareLanguageAsync("ru", true, cancellation.Token);
            cancellation.Cancel();
            var timeout = System.Diagnostics.Stopwatch.StartNew();
            while (!task.IsCompleted && timeout.Elapsed.TotalSeconds < 5) yield return null;
            Assert.That(task.IsCanceled, Is.True); Assert.That(_provider.Gate.Task.IsCompleted, Is.False);
            _provider.Gate.SetResult(_provider.Text["ru"]); yield return null;
            Assert.That(_provider.Reads, Is.Zero); Assert.That(_manager.CurrentLanguage, Is.EqualTo("en"));
        }

        private sealed class Provider : IAsyncLocalizationDataProvider, IDisposable
        {
            internal readonly Dictionary<string, Dictionary<string, string>> Text = new();
            internal readonly Dictionary<string, List<LocalizationAssetTable>> Tables = new();
            internal int Reads; internal bool Disposed; internal string Fail;
            internal Action<string> OnText;
            internal TaskCompletionSource<Dictionary<string, string>> Gate;
            public Dictionary<string, string> LoadTextData(string code)
            {
                Reads++; OnText?.Invoke(code);
                if (code == "pl" && Fail == "text") throw new InvalidOperationException("text failure");
                if (code == "pl" && Fail == "nullText") return null;
                return Text.TryGetValue(code, out var data) ? data : new();
            }
            public List<LocalizationAssetTable> LoadAssetTables(string code)
            {
                Reads++;
                if (code == "pl" && Fail == "assets") throw new InvalidOperationException("asset failure");
                if (code == "pl" && Fail == "nullAssets") return null;
                return Tables.TryGetValue(code, out var tables) ? tables : new();
            }
            public Task<Dictionary<string, string>> LoadTextDataAsync(string code, CancellationToken token) =>
                code == "ru" && Gate != null ? Gate.Task : Task.FromResult(LoadTextData(code));
            public Task<List<LocalizationAssetTable>> LoadAssetTablesAsync(string code, CancellationToken token) => Task.FromResult(LoadAssetTables(code));
            public bool HasTextData(string code) => Text.ContainsKey(code);
            public void Dispose() => Disposed = true;
        }
    }
}
