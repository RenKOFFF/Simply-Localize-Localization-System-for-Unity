using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SimplyLocalize.Tests
{
    public class RuntimeLoggingTests
    {
        private readonly List<LocalizationProjectConfig> configs = new();
        private readonly List<LocalizationRuntime> runtimes = new();
        private bool previousEnabled, previousBuildEnabled;
        [SetUp] public void Setup()
        {
            previousEnabled = LocalizationLogger.Enabled;
            previousBuildEnabled = LocalizationLogger.EnabledInBuild;
        }
        [TearDown] public void Cleanup()
        {
            foreach (var runtime in runtimes) Complete(runtime.ReleaseAsync()); runtimes.Clear();
            foreach (var config in configs) Object.DestroyImmediate(config); configs.Clear();
            LocalizationLogger.Enabled = previousEnabled; LocalizationLogger.EnabledInBuild = previousBuildEnabled;
        }
        private LocalizationRuntime Runtime(Action onLoad = null)
        {
            var config = LocalizationProjectConfig.Create(new[] { "en" }); configs.Add(config);
            config.Sets[0].AddEntry("key"); config.Sets[0].SetSources("en", new LocalizationSourceReference("test", "text", LocalizationSourceKind.TextTable));
            var registry = new LocalizationSourceRegistry(false); registry.Register("test", new Provider(onLoad));
            var runtime = new LocalizationRuntime(config, registry); runtimes.Add(runtime); return runtime;
        }
        private static void Complete(Task task)
        { Assert.That(task.IsCompleted, Is.True); task.GetAwaiter().GetResult(); }
        private static List<string> Capture(Action action)
        {
            var logs = new List<string>();
            void Handler(string message, string trace, LogType type) { if (type == LogType.Log) logs.Add(message); }
            Application.logMessageReceived += Handler;
            try { action(); } finally { Application.logMessageReceived -= Handler; }
            return logs;
        }
        [TestCase(true)] [TestCase(false)]
        public void ConstructionAndReleaseDoNotOverrideExplicitStaticLogging(bool enabled)
        {
            LocalizationLogger.Enabled = enabled; LocalizationLogger.EnabledInBuild = !enabled;
            var first = Runtime(); var second = Runtime(); Complete(first.InitializeAsync()); Complete(second.InitializeAsync());
            Complete(first.ReleaseAsync());
            Assert.That(LocalizationLogger.Enabled, Is.EqualTo(enabled));
            Assert.That(LocalizationLogger.EnabledInBuild, Is.EqualTo(!enabled));
            Assert.That(Capture(() => LocalizationLogger.LogInfo("public-marker")).Count(m => m.Contains("public-marker")), Is.EqualTo(enabled ? 1 : 0));
        }
        [TestCase(true)] [TestCase(false)]
        public void ProviderInfoUsesExplicitLoggerSettingWithoutHiddenRuntimeScope(bool enabled)
        {
            LocalizationLogger.Enabled = enabled;
            var runtime = Runtime(() => LocalizationLogger.LogInfo("provider-marker"));
            Assert.That(Capture(() => Complete(runtime.InitializeAsync())).Count(m => m.Contains("provider-marker")), Is.EqualTo(enabled ? 1 : 0));
        }
        [TestCase(true)] [TestCase(false)]
        public void ProviderFailureDoesNotLeakOrChangeLoggingSettings(bool enabled)
        {
            LocalizationLogger.Enabled = enabled; LocalizationLogger.EnabledInBuild = !enabled;
            var runtime = Runtime(() => { LocalizationLogger.LogInfo("inside-provider"); throw new InvalidOperationException("failure probe"); });
            var logs = Capture(() =>
            {
                var initialization = runtime.InitializeAsync();
                Assert.That(initialization.IsFaulted, Is.True); Assert.That(initialization.Exception.ToString(), Does.Contain("failure probe"));
                LocalizationLogger.LogInfo("after-provider");
            });
            Assert.That(logs.Any(m => m.Contains("inside-provider")), Is.EqualTo(enabled));
            Assert.That(logs.Any(m => m.Contains("after-provider")), Is.EqualTo(enabled));
            Assert.That(LocalizationLogger.EnabledInBuild, Is.EqualTo(!enabled));
        }
        [TestCase(true)] [TestCase(false)]
        public void NestedRuntimeCannotSilentlyChangeOuterProviderLogging(bool enabled)
        {
            LocalizationLogger.Enabled = enabled;
            var nested = Runtime(() => LocalizationLogger.LogInfo("nested-marker"));
            var outer = Runtime(() => { Complete(nested.InitializeAsync()); LocalizationLogger.LogInfo("outer-marker"); });
            var logs = Capture(() => Complete(outer.InitializeAsync()));
            Assert.That(logs.Any(m => m.Contains("nested-marker")), Is.EqualTo(enabled));
            Assert.That(logs.Any(m => m.Contains("outer-marker")), Is.EqualTo(enabled));
        }
        private sealed class Provider : ILocalizationSourceProvider
        {
            private readonly Action onLoad;
            internal Provider(Action onLoad) => this.onLoad = onLoad;
            public LocalizationProviderCapabilities GetCapabilities(LocalizationSourceKind kind) => new(LocalizationLoadUnit.Table, false, false);
            public Task<LocalizationSourceLease> LoadAsync(LocalizationSourceRequest request, CancellationToken token)
            {
                onLoad?.Invoke();
                return Task.FromResult(new LocalizationSourceLease(LocalizationSourceData.FromText(new Dictionary<string, string> { ["key"] = "value" }), () => Task.CompletedTask));
            }
        }
    }
}
