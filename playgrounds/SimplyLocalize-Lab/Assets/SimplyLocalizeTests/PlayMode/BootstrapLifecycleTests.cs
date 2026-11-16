using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using static SimplyLocalize.Tests.ComponentSourceFixture;
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
        private LocalizationProjectConfig _config;

        [SetUp]
        public void SetUp()
        {
            Completed(Localization.ShutdownAsync());
            _config = LocalizationProjectConfig.Create(new[] { "en" });
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
            Completed(Localization.ShutdownAsync());
        }

        [UnityTest]
        public IEnumerator DuplicateBootstrap_DoesNotReplaceOrShutdownOwner()
        {
            var first = Bootstrap();
            var manager = Localization.Runtime;
            var second = Bootstrap();
            Assert.That(Localization.Runtime, Is.SameAs(manager));
            Object.Destroy(second);
            yield return null;
            Assert.That(Localization.Runtime, Is.SameAs(manager));
            Object.Destroy(first);
            yield return null;
            Assert.That(Localization.IsInitialized, Is.False);
        }

        [UnityTest]
        public IEnumerator OldBootstrap_DoesNotShutdownManualReplacement()
        {
            var bootstrap = Bootstrap();
            Completed(Localization.InitializeAsync(_config));
            var replacement = Localization.Runtime;
            Object.Destroy(bootstrap);
            yield return null;
            Assert.That(Localization.Runtime, Is.SameAs(replacement));
        }

        [UnityTest]
        public IEnumerator Bootstrap_DoesNotAcquireManagerReplacedByStartupSubscriber()
        {
            bool replaced = false;
            System.Threading.Tasks.Task<LocalizationRuntime> replacement = null;
            System.Action<string> handler = _ =>
            {
                if (replaced) return;
                replaced = true;
                replacement = Localization.InitializeAsync(_config);
            };
            Localization.OnLanguageChanged += handler;
            GameObject bootstrap;
            try { bootstrap = Bootstrap(); }
            finally { Localization.OnLanguageChanged -= handler; }
            Assert.That(replaced, Is.True);
            while (!replacement.IsCompleted) yield return null;
            Completed(replacement);
            Object.Destroy(bootstrap);
            yield return null;
            Assert.That(Localization.Runtime, Is.SameAs(replacement.Result));
        }

        [UnityTest]
        public IEnumerator InvalidBootstrap_DoesNotShutdownExistingManager()
        {
            Completed(Localization.InitializeAsync(_config));
            var manager = Localization.Runtime;
            var go = new GameObject("Invalid bootstrap");
            _objects.Add(go);
            // An unused duplicate bootstrap must not validate or replace the active runtime.
            go.AddComponent<LocalizationBootstrap>();
            Object.Destroy(go);
            yield return null;
            Assert.That(Localization.Runtime, Is.SameAs(manager));
        }

        [UnityTest]
        public IEnumerator AdditiveSceneUnload_OnlyOwnerCanShutdown()
        {
            var ownerScene = SceneManager.CreateScene("Localization owner");
            var otherScene = SceneManager.CreateScene("Localization duplicate");
            _scenes.Add(ownerScene);
            _scenes.Add(otherScene);
            var first = Bootstrap(scene: ownerScene);
            var manager = Localization.Runtime;
            Bootstrap(scene: otherScene);
            yield return SceneManager.UnloadSceneAsync(otherScene);
            Assert.That(Localization.Runtime, Is.SameAs(manager));
            yield return SceneManager.UnloadSceneAsync(ownerScene);
            Assert.That(Localization.IsInitialized, Is.False);
        }

        [UnityTest]
        public IEnumerator PersistentBootstrap_SurvivesOwningSceneUnload()
        {
            var scene = SceneManager.CreateScene("Localization persistent owner");
            _scenes.Add(scene);
            var owner = Bootstrap(true, scene);
            var manager = Localization.Runtime;
            yield return SceneManager.UnloadSceneAsync(scene);
            Assert.That(owner != null, Is.True);
            Assert.That(Localization.Runtime, Is.SameAs(manager));
        }

        [UnityTest]
        public IEnumerator PendingAutomaticDiscoveryDoesNotSuppressSceneBootstrapOrTransferOwnership()
        {
            foreach (bool found in new[] { false, true })
            {
                var discovered = new System.Threading.Tasks.TaskCompletionSource<LocalizationProjectConfig>();
                System.Func<System.Threading.Tasks.Task<LocalizationProjectConfig>> find = () => discovered.Task;
                var automatic = (System.Threading.Tasks.Task<LocalizationRuntime>)typeof(Localization)
                    .GetMethod("StartInitialization", BindingFlags.Static | BindingFlags.NonPublic)
                    .Invoke(null, new object[] { find, null, null, null, null, System.Threading.CancellationToken.None });
                var owner = Bootstrap();
                var startup = owner.GetComponent<LocalizationBootstrap>().Initialization;
                Assert.That(startup.IsCompleted, Is.False);
                discovered.SetResult(found ? _config : null);
                while (!startup.IsCompleted || !automatic.IsCompleted) yield return null;
                Completed(startup); Completed(automatic);
                Assert.That(Localization.IsInitialized, Is.True);
                Object.Destroy(owner); yield return null;
                Assert.That(Localization.IsInitialized, Is.EqualTo(found), "Only the initializer that published the runtime owns it.");
                Completed(Localization.ShutdownAsync());
            }
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
