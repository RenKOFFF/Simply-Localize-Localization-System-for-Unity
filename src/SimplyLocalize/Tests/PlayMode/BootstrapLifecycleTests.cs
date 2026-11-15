using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace SimplyLocalize.Tests
{
    public class BootstrapLifecycleTests
    {
        private readonly List<Object> _objects = new();
        private readonly List<Scene> _scenes = new();
        private LocalizationConfig _config;

        [SetUp]
        public void SetUp()
        {
            Localization.Shutdown();
            _config = ScriptableObject.CreateInstance<LocalizationConfig>();
            _objects.Add(_config);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (var obj in _objects) if (obj != null) Object.Destroy(obj);
            _objects.Clear();
            yield return null;
            foreach (var scene in _scenes)
                if (scene.IsValid() && scene.isLoaded) yield return SceneManager.UnloadSceneAsync(scene);
            _scenes.Clear();
            Localization.Shutdown();
        }

        [UnityTest]
        public IEnumerator DuplicateBootstrap_DoesNotReplaceOrShutdownOwner()
        {
            var first = Bootstrap();
            var manager = Localization.Manager;
            var second = Bootstrap();
            Assert.That(Localization.Manager, Is.SameAs(manager));
            Object.Destroy(second);
            yield return null;
            Assert.That(Localization.Manager, Is.SameAs(manager));
            Object.Destroy(first);
            yield return null;
            Assert.That(Localization.IsInitialized, Is.False);
        }

        [UnityTest]
        public IEnumerator OldBootstrap_DoesNotShutdownManualReplacement()
        {
            var bootstrap = Bootstrap();
            Localization.Initialize(_config);
            var replacement = Localization.Manager;
            Object.Destroy(bootstrap);
            yield return null;
            Assert.That(Localization.Manager, Is.SameAs(replacement));
        }

        [UnityTest]
        public IEnumerator Bootstrap_DoesNotAcquireManagerReplacedByStartupSubscriber()
        {
            var language = ScriptableObject.CreateInstance<LanguageProfile>();
            _objects.Add(language);
            language.languageCode = "en";
            _config.languages.Add(language);
            _config.defaultLanguage = language;
            bool replaced = false;
            LocalizationManager replacement = null;
            System.Action<string> handler = _ =>
            {
                if (replaced) return;
                replaced = true;
                Localization.Initialize(_config);
                replacement = Localization.Manager;
            };
            Localization.OnLanguageChanged += handler;
            GameObject bootstrap;
            try { bootstrap = Bootstrap(); }
            finally { Localization.OnLanguageChanged -= handler; }
            Assert.That(replaced, Is.True);
            Object.Destroy(bootstrap);
            yield return null;
            Assert.That(Localization.Manager, Is.SameAs(replacement));
        }

        [UnityTest]
        public IEnumerator InvalidBootstrap_DoesNotShutdownExistingManager()
        {
            Localization.Initialize(_config);
            var manager = Localization.Manager;
            var go = new GameObject("Invalid bootstrap");
            _objects.Add(go);
            LogAssert.Expect(LogType.Error, "[SimplyLocalize] LocalizationBootstrap: No config assigned!");
            go.AddComponent<LocalizationBootstrap>();
            Object.Destroy(go);
            yield return null;
            Assert.That(Localization.Manager, Is.SameAs(manager));
        }

        [UnityTest]
        public IEnumerator AdditiveSceneUnload_OnlyOwnerCanShutdown()
        {
            var ownerScene = SceneManager.CreateScene("Localization owner");
            var otherScene = SceneManager.CreateScene("Localization duplicate");
            _scenes.Add(ownerScene);
            _scenes.Add(otherScene);
            var first = Bootstrap(scene: ownerScene);
            var manager = Localization.Manager;
            Bootstrap(scene: otherScene);
            yield return SceneManager.UnloadSceneAsync(otherScene);
            Assert.That(Localization.Manager, Is.SameAs(manager));
            yield return SceneManager.UnloadSceneAsync(ownerScene);
            Assert.That(Localization.IsInitialized, Is.False);
        }

        [UnityTest]
        public IEnumerator PersistentBootstrap_SurvivesOwningSceneUnload()
        {
            var scene = SceneManager.CreateScene("Localization persistent owner");
            _scenes.Add(scene);
            var owner = Bootstrap(true, scene);
            var manager = Localization.Manager;
            yield return SceneManager.UnloadSceneAsync(scene);
            Assert.That(owner != null, Is.True);
            Assert.That(Localization.Manager, Is.SameAs(manager));
        }

        private GameObject Bootstrap(bool persistent = false, Scene scene = default)
        {
            var go = new GameObject("Bootstrap test");
            go.SetActive(false);
            _objects.Add(go);
            if (scene.IsValid()) SceneManager.MoveGameObjectToScene(go, scene);
            var bootstrap = go.AddComponent<LocalizationBootstrap>();
            Set(bootstrap, "_config", _config);
            Set(bootstrap, "_dontDestroyOnLoad", persistent);
            go.SetActive(true);
            return go;
        }

        private static void Set(object obj, string field, object value) =>
            obj.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(obj, value);
    }
}
