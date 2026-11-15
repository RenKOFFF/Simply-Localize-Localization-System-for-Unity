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
            Localization.Shutdown();
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
                var config = ScriptableObject.CreateInstance<LocalizationConfig>();
                Localization.Initialize(config);
                var previous = Localization.Manager;
                Object.DestroyImmediate(config);
                _staleEvents = 0;
                Localization.OnLanguageChanged += StaleHandler;
                PluralRuleProvider.RegisterRule("en", new AlwaysFirstRule());
                yield return new EnterPlayMode(false);
                Assert.That(Localization.Manager, Is.Not.SameAs(previous));
                Assert.That(PluralRuleProvider.GetRule("en").Evaluate(2), Is.EqualTo(1));
                Localization.Reload();
                Assert.That(_staleEvents, Is.Zero);
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
