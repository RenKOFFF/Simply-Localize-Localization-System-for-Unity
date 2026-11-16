using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using SimplyLocalize.Data;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace SimplyLocalize.Tests
{
    public class AssetOnlyContextTests
    {
        private readonly List<Object> _objects = new();
        private LocalizationConfig _config;
        private T Create<T>() where T : ScriptableObject { var x = ScriptableObject.CreateInstance<T>(); _objects.Add(x); return x; }
        [SetUp] public void Setup()
        {
            _config = Create<LocalizationConfig>();
            foreach (string code in new[] { "en", "ru" }) { var p = Create<LanguageProfile>(); p.languageCode = code; _config.languages.Add(p); }
            _config.defaultLanguage = _config.languages[1]; _config.fallbackLanguage = _config.languages[0];
            _config.resourcesBasePath = "__MissingAssetOnly_" + Guid.NewGuid().ToString("N");
        }
        [TearDown] public void Cleanup() { foreach(var o in _objects) Object.DestroyImmediate(o); _objects.Clear(); }
        private Provider Data()
        {
            var provider = new Provider();
            var table = Create<LocalizationAssetTable>();
            var texture = new Texture2D(2, 2); _objects.Add(texture); table.Set("icon", texture);
            provider.Assets["en"] = new List<LocalizationAssetTable> { table }; return provider;
        }
        [Test] public void ExistingConfigs_KeepTextEnabledWhenFieldIsAbsent()
        {
            JsonUtility.FromJsonOverwrite("{\"resourcesBasePath\":\"Legacy\"}", _config);
            Assert.That(_config.loadTextData, Is.True);
            var provider = Data(); var manager = new LocalizationManager(_config); manager.SetDataProvider(provider); manager.SetLanguage("ru");
            Assert.That(provider.TextCalls, Is.EqualTo(2));
            Assert.That(manager.Get("greeting"), Is.EqualTo("hello"));
            Assert.That(manager.GetAsset<Texture2D>("icon"), Is.Not.Null);
        }
        [Test] public void AssetOnly_SwitchReloadAndProviderReplacement_NeverRequestText()
        {
            _config.loadTextData = false; var provider = Data(); provider.RejectText = true;
            var manager = new LocalizationManager(_config); manager.SetDataProvider(provider); manager.SetLanguage("ru");
            var original = manager.GetAsset<Texture2D>("icon"); Assert.That(original, Is.Not.Null);
            Assert.That(manager.HasKey("greeting"), Is.False); Assert.That(manager.HasTranslation("greeting", "en"), Is.False);
            Assert.That(manager.GetAllKeys(), Is.Empty);
            manager.SetLanguage("en"); manager.SetLanguage("ru"); manager.Reload();
            Assert.That(manager.GetAsset<Texture2D>("icon"), Is.SameAs(original));
            var replacement = Data(); replacement.RejectText = true; manager.SetDataProvider(replacement);
            Assert.That(manager.GetAsset<Texture2D>("icon"), Is.Not.SameAs(original));
            Assert.That(provider.TextCalls + replacement.TextCalls, Is.Zero);
        }
        [Test] public void AssetOnly_FailedProviderReplacementPreservesActiveAssetsAndEvents()
        {
            _config.loadTextData = false; var provider = Data(); provider.RejectText = true;
            var manager = new LocalizationManager(_config); manager.SetDataProvider(provider); manager.SetLanguage("ru");
            var original = manager.GetAsset<Texture2D>("icon"); int events = 0; manager.OnLanguageChanged += _ => events++;
            var failing = Data(); failing.RejectText = true; failing.FailAssets = true;
            Assert.Throws<InvalidOperationException>(() => manager.SetDataProvider(failing));
            Assert.That(manager.CurrentLanguage, Is.EqualTo("ru")); Assert.That(events, Is.Zero);
            Assert.That(manager.GetAsset<Texture2D>("icon"), Is.SameAs(original));
            Assert.That(failing.TextCalls, Is.Zero);
        }
        [Test] public void AssetOnly_MissingTextFolderDoesNotProduceSpuriousWarning()
        {
            _config.loadTextData = false; var warnings = new List<string>();
            void Capture(string message,string trace,LogType type) { if(type==LogType.Warning) warnings.Add(message); }
            Application.logMessageReceived += Capture;
            try { var manager = new LocalizationManager(_config); manager.SetLanguage("ru"); manager.Reload(); }
            finally { Application.logMessageReceived -= Capture; }
            Assert.That(warnings.Any(w => w.Contains("No text files found")), Is.False);
        }
        [Test] public void TextEnabled_MissingTextFolderStillProducesDiagnostic()
        {
            _config.fallbackLanguage = null;
            LogAssert.Expect(LogType.Warning, new Regex("No text files found.*ru"));
            new LocalizationManager(_config).SetLanguage("ru");
        }
        [Test] public void TextEnabled_ProviderErrorsAreNotHidden()
        {
            var provider = Data(); provider.RejectText = true;
            var manager = new LocalizationManager(_config); manager.SetDataProvider(provider);
            Assert.Throws<InvalidOperationException>(() => manager.SetLanguage("en"));
            Assert.That(manager.CurrentLanguage, Is.Null);
        }
        private sealed class Provider : ILocalizationDataProvider
        {
            public int TextCalls; public bool RejectText, FailAssets;
            public readonly Dictionary<string,List<LocalizationAssetTable>> Assets = new();
            public Dictionary<string,string> LoadTextData(string code)
            { TextCalls++; if(RejectText) throw new InvalidOperationException("Text should not be requested"); return new Dictionary<string,string>{{"greeting","hello"}}; }
            public bool HasTextData(string code) => throw new InvalidOperationException("Unexpected text probe");
            public List<LocalizationAssetTable> LoadAssetTables(string code)
            { if(FailAssets) throw new InvalidOperationException("Asset load failure"); return Assets.TryGetValue(code,out var tables) ? tables : new List<LocalizationAssetTable>(); }
        }
    }
}
