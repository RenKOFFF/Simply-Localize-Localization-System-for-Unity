using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SimplyLocalize.Data;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SimplyLocalize.Tests
{
    public class ManagerLoggingTests
    {
        private readonly List<Object> _objects = new();
        [TearDown] public void Cleanup() { Localization.Shutdown(); foreach (var o in _objects) Object.DestroyImmediate(o); _objects.Clear(); }
        private LocalizationConfig Config(bool enabled)
        {
            var config = ScriptableObject.CreateInstance<LocalizationConfig>(); _objects.Add(config);
            config.enableLogging = config.enableLoggingInBuild = enabled;
            var language = ScriptableObject.CreateInstance<LanguageProfile>(); _objects.Add(language);
            language.languageCode = "en"; config.languages.Add(language); return config;
        }
        private static List<string> Capture(Action action)
        {
            var logs = new List<string>();
            void Handler(string message, string trace, LogType type) { if (type == LogType.Log) logs.Add(message); }
            Application.logMessageReceived += Handler;
            try { action(); } finally { Application.logMessageReceived -= Handler; }
            return logs;
        }
        [TestCase(true, false)] [TestCase(false, true)]
        public void StandaloneManagers_HaveIndependentInfoSettings(bool firstEnabled, bool secondEnabled)
        {
            var first = new LocalizationManager(Config(firstEnabled)); var second = new LocalizationManager(Config(secondEnabled));
            Assert.That(Capture(first.ClearCaches).Count(m => m.Contains("All caches cleared")), Is.EqualTo(firstEnabled ? 1 : 0));
            Assert.That(Capture(second.ClearCaches).Count(m => m.Contains("All caches cleared")), Is.EqualTo(secondEnabled ? 1 : 0));
        }
        [TestCase(true, false)] [TestCase(false, true)]
        public void StaticPublicLogging_FollowsFacadeInsteadOfLastConstructedManager(bool facadeEnabled, bool otherEnabled)
        {
            Localization.Initialize(Config(facadeEnabled)); new LocalizationManager(Config(otherEnabled));
            Assert.That(Capture(() => LocalizationLogger.LogInfo("facade-marker")).Count(m => m.Contains("facade-marker")), Is.EqualTo(facadeEnabled ? 1 : 0));
        }
        [TestCase(true)] [TestCase(false)]
        public void ProviderLogs_FollowCallingManager(bool enabled)
        {
            Localization.Initialize(Config(!enabled));
            var manager = new LocalizationManager(Config(enabled));
            manager.SetDataProvider(new Provider(() => LocalizationLogger.LogInfo("provider-marker")));
            Assert.That(Capture(() => manager.SetLanguage("en")).Count(m => m.Contains("provider-marker")), Is.EqualTo(enabled ? 1 : 0));
        }
        [Test] public void ProviderFailure_RestoresStaticLoggingContext()
        {
            Localization.Initialize(Config(false)); var manager = new LocalizationManager(Config(true));
            manager.SetDataProvider(new Provider(() => { LocalizationLogger.LogInfo("inside-provider"); throw new InvalidOperationException("failure"); }));
            var logs = Capture(() => {
                Assert.Throws<InvalidOperationException>(() => manager.SetLanguage("en"));
                LocalizationLogger.LogInfo("after-provider");
            });
            Assert.That(logs.Any(m => m.Contains("inside-provider")), Is.True);
            Assert.That(logs.Any(m => m.Contains("after-provider")), Is.False);
        }
        [Test] public void NestedProvider_RestoresOuterManagerLogging()
        {
            var muted = new LocalizationManager(Config(false)); muted.SetDataProvider(new Provider(() => LocalizationLogger.LogInfo("nested-marker")));
            var enabled = new LocalizationManager(Config(true));
            enabled.SetDataProvider(new Provider(() => { muted.SetLanguage("en"); LocalizationLogger.LogInfo("outer-marker"); }));
            var logs = Capture(() => enabled.SetLanguage("en"));
            Assert.That(logs.Any(m => m.Contains("nested-marker")), Is.False);
            Assert.That(logs.Any(m => m.Contains("outer-marker")), Is.True);
        }
        private sealed class Provider : ILocalizationDataProvider
        {
            private readonly Action _load;
            internal Provider(Action load) => _load = load;
            public Dictionary<string,string> LoadTextData(string code) { _load(); return new Dictionary<string,string>(); }
            public List<LocalizationAssetTable> LoadAssetTables(string code) => new List<LocalizationAssetTable>();
            public bool HasTextData(string code) => true;
        }
    }
}
