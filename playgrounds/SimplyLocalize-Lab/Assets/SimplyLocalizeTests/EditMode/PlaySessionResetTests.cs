using System;
using System.Collections;
using NUnit.Framework;
using SimplyLocalize.Pluralization;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace SimplyLocalize.Tests
{
    public class PlaySessionResetTests
    {
        [SerializeField] private bool _previousEnabled;
        [SerializeField] private EnterPlayModeOptions _previousOptions;
        private static int _staleEvents;
        private static readonly Action<string> StaleHandler = _ => _staleEvents++;
        private GameObject persistentProbe;
        private LocalizationProjectConfig probeConfig;

        [SetUp]
        public void SetUp()
        {
            _previousEnabled = EditorSettings.enterPlayModeOptionsEnabled;
            _previousOptions = EditorSettings.enterPlayModeOptions;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (Application.isPlaying) yield return new ExitPlayMode();
            Localization.OnLanguageChanged -= StaleHandler;
            var closing = Localization.ShutdownAsync();
            while (!closing.IsCompleted) yield return null;
            closing.GetAwaiter().GetResult();
            if (persistentProbe != null) Object.DestroyImmediate(persistentProbe);
            if (probeConfig != null) Object.DestroyImmediate(probeConfig);
            persistentProbe = null; probeConfig = null;
            EditorSettings.enterPlayModeOptions = _previousOptions;
            EditorSettings.enterPlayModeOptionsEnabled = _previousEnabled;
        }

        [UnityTest]
        public IEnumerator RepeatedPlaySessionsWithoutDomainReload_ResetStateRulesAndSubscriptions()
        {
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            for (int session = 0; session < 2; session++)
            {
                var config = LocalizationProjectConfig.Create(new[] { "en" });
                var initialization = Localization.InitializeAsync(config);
                while (!initialization.IsCompleted) yield return null;
                initialization.GetAwaiter().GetResult();
                var previous = Localization.Runtime;
                Object.DestroyImmediate(config);
                _staleEvents = 0;
                Localization.OnLanguageChanged += StaleHandler;
                PluralRuleProvider.RegisterRule("en", new AlwaysFirstRule());
                yield return new EnterPlayMode(false);
                Assert.That(Localization.Runtime, Is.Not.SameAs(previous));
                Assert.That(PluralRuleProvider.GetRule("en").Evaluate(2), Is.EqualTo(1));
                Assert.That(previous.IsReleased, Is.True);
                var activeConfig = LocalizationProjectConfig.Create(new[] { "en" });
                var active = Localization.InitializeAsync(activeConfig);
                while (!active.IsCompleted) yield return null;
                active.GetAwaiter().GetResult();
                var reload = Localization.ReloadAsync();
                while (!reload.IsCompleted) yield return null;
                reload.GetAwaiter().GetResult();
                Object.DestroyImmediate(activeConfig);
                Assert.That(_staleEvents, Is.Zero);
                yield return new ExitPlayMode();
            }
        }

        [UnityTest]
        public IEnumerator ExplicitBootstrapAndComponentsRestartWithoutDomainOrSceneReload()
        {
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload;
            probeConfig = LocalizationProjectConfig.Create(new[] { "en" });
            persistentProbe = new GameObject("No reload localization probe", typeof(RectTransform));
            persistentProbe.SetActive(false);
            var text = persistentProbe.AddComponent<UnityEngine.UI.Text>(); text.text = "Editor baseline";
            var localized = persistentProbe.AddComponent<Components.LocalizedText>(); localized.Key = "missing.key";
            var bootstrap = persistentProbe.AddComponent<LocalizationBootstrap>();
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            typeof(LocalizationBootstrap).GetField("_config", flags).SetValue(bootstrap, probeConfig);
            typeof(LocalizationBootstrap).GetField("_dontDestroyOnLoad", flags).SetValue(bootstrap, false);
            persistentProbe.SetActive(true);
            LocalizationRuntime previous = null;
            for (int session = 0; session < 2; session++)
            {
                yield return new EnterPlayMode(false);
                yield return null; // Let the first Player loop finish enabling scene components.
                float deadline = Time.realtimeSinceStartup + 10;
                while (!Localization.IsInitialized && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.That(Localization.IsInitialized, Is.True, "An explicit scene bootstrap must start every Play session.");
                // Unity can recreate the managed component on the Play/Edit boundary even with
                // scene reload disabled. Inspect the live scene object, not a destroyed wrapper.
                persistentProbe = GameObject.Find("No reload localization probe");
                Assert.That(persistentProbe != null, Is.True, "Probe must survive session " + session);
                localized = persistentProbe.GetComponent<Components.LocalizedText>();
                text = persistentProbe.GetComponent<UnityEngine.UI.Text>();
                Assert.That(EditorSettings.enterPlayModeOptions, Is.EqualTo(EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload));
                Assert.That(localized.BoundRuntime, Is.SameAs(Localization.Runtime), "Session " + session);
                Assert.That(text.text, Is.EqualTo("missing.key"));
                if (previous != null) { Assert.That(previous.IsReleased, Is.True); Assert.That(Localization.Runtime, Is.Not.SameAs(previous)); }
                previous = Localization.Runtime;
                yield return new ExitPlayMode();
            }
        }

        [UnityTest]
        public IEnumerator PlaySessionWithDomainReload_StartsWithBuiltInRules()
        {
            EditorSettings.enterPlayModeOptionsEnabled = false;
            yield return new EnterPlayMode();
            Assert.That(PluralRuleProvider.GetRule("en").Evaluate(2), Is.EqualTo(1));
            yield return new ExitPlayMode();
        }

        private sealed class AlwaysFirstRule : IPluralRule
        {
            public int Evaluate(int count) => 0;
            public int FormCount => 1;
        }
    }
}
