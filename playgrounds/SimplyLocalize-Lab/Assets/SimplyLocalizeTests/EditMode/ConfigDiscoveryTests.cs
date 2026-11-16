using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using SimplyLocalize.Editor;
using SimplyLocalize.Editor.Utilities;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace SimplyLocalize.Tests
{
    public class ConfigDiscoveryTests
    {
        private const string Root = "Assets/SimplyLocalize";
        private string _other;
        [SetUp] public void Setup()
        {
            if (Directory.Exists(Root) || File.Exists(Root + ".meta")) Assert.Ignore("Existing project setup; use the dedicated lab.");
            _other = "Assets/SetupRegression_" + Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(_other); AssetDatabase.Refresh();
        }
        [TearDown] public void Cleanup()
        {
            if (_other == null) return;
            AssetDatabase.DeleteAsset(Root); AssetDatabase.DeleteAsset(_other); _other = null;
        }
        // These assertions describe the authoring output. Async runtime discovery is checked separately below.
        private static LocalizationConfig Find(bool automatic)
        {
            var config = AssetDatabase.LoadAssetAtPath<LocalizationConfig>(LocalizationProjectSetup.ConfigAssetPath);
            return automatic && config != null && !config.autoInitialize ? null : config;
        }
        private LocalizationProjectSetup.Result Create() => LocalizationProjectSetup.Configure(new[] { "en", "ru" }, "en", "en", true);

        [UnityTest] public System.Collections.IEnumerator RuntimeDiscovery_RequiresNewTypeAtFixedAddressAndHonorsAutoFlag()
        {
            var old = Create().Config;
            var missing = Localization.InitializeAsync();
            while (!missing.IsCompleted) yield return null;
            Assert.That(missing.IsFaulted, Is.True);
            StringAssert.Contains("Project Configuration", missing.Exception.ToString());
            AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(old));
            var project = LocalizationProjectConfig.Create(new[] { "en", "ru" });
            project.AutoInitialize = true;
            AssetDatabase.CreateAsset(project, LocalizationProjectSetup.ConfigAssetPath);
            AssetDatabase.SaveAssets(); AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var initialized = Localization.InitializeAsync();
            while (!initialized.IsCompleted) yield return null;
            initialized.GetAwaiter().GetResult();
            Assert.That(Localization.Config, Is.SameAs(project));
            Assert.That(Localization.CurrentLanguage, Is.EqualTo("en"));
            var close = Localization.ShutdownAsync();
            while (!close.IsCompleted) yield return null;
            close.GetAwaiter().GetResult();
            project.AutoInitialize = false;
            var automatic = (System.Threading.Tasks.Task<LocalizationRuntime>)typeof(Localization)
                .GetMethod("InitializeAutomaticallyAsync", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
            while (!automatic.IsCompleted) yield return null;
            Assert.That(automatic.GetAwaiter().GetResult(), Is.Null);
            Assert.That(Localization.IsInitialized, Is.False);
        }
        private LocalizationConfig Config(string relative, bool automatic)
        {
            string path = _other + "/" + relative;
            Directory.CreateDirectory(Path.GetDirectoryName(path)); AssetDatabase.Refresh();
            var config = ScriptableObject.CreateInstance<LocalizationConfig>(); config.autoInitialize = automatic;
            AssetDatabase.CreateAsset(config, path); return config;
        }
        [Test] public void Setup_CreatesUsableLanguagesAndFixedStartupConfig()
        {
            var result = Create();
            Assert.That(result.ValidationErrors, Is.Empty); Assert.That(result.CreatedPaths.Count, Is.EqualTo(5));
            Assert.That(result.Config.languages.Select(p => p.Code), Is.EqualTo(new[] { "en", "ru" }));
            Assert.That(Find(true), Is.SameAs(result.Config)); Assert.That(Find(false), Is.SameAs(result.Config));
            Assert.That(result.Config.defaultLanguage.Code, Is.EqualTo("en"));
        }
        [Test] public void RepeatSetup_PreservesGuidExternalTranslationsAndFlags()
        {
            var first = Create(); first.Config.enableLogging = true; first.Config.autoInitialize = false;
            EditorUtility.SetDirty(first.Config); AssetDatabase.SaveAssetIfDirty(first.Config);
            string guid = AssetDatabase.AssetPathToGUID(LocalizationProjectSetup.ConfigAssetPath);
            string text = first.CreatedPaths.First(p => p.EndsWith("/en/text/main.json"));
            File.WriteAllText(text, "{\"translations\":{\"Menu/Play\":\"External edit\"},\"custom\":42}");
            byte[] before = File.ReadAllBytes(text);
            var second = Create();
            Assert.That(second.CreatedPaths, Is.Empty); Assert.That(second.Config.enableLogging, Is.True);
            Assert.That(Find(true), Is.Null); Assert.That(Find(false), Is.SameAs(first.Config));
            Assert.That(File.ReadAllBytes(text), Is.EqualTo(before));
            Assert.That(AssetDatabase.AssetPathToGUID(LocalizationProjectSetup.ConfigAssetPath), Is.EqualTo(guid));
        }
        [Test] public void ExplicitConfig_RemainsUsableOutsideResources()
        {
            var config = LocalizationProjectConfig.Create(new[] { "en" });
            AssetDatabase.CreateAsset(config, _other + "/Manual.asset");
            using var runtime = new LocalizationRuntime(config);
            var initialization = runtime.InitializeAsync();
            Assert.That(initialization.IsCompletedSuccessfully, Is.True);
            Assert.That(runtime.GetChannel().RequestedLanguage, Is.EqualTo("en"));
        }
        [Test] public void LegacyAutoConfigs_AreNotDiscovered()
        {
            Config("Resources/Nested/Old.asset", true); Assert.That(Find(true), Is.Null);
            Assert.That(Find(false), Is.Null);
        }
        [Test] public void FixedAddress_IgnoresOtherAutomaticConfigs()
        {
            var config = Create().Config; Config("Resources/Old.asset", true); Assert.That(Find(true), Is.SameAs(config));
        }
        [Test] public void Setup_RequiresChoiceWhenExistingConfigsArePresent()
        {
            Config("Manual.asset", false);
            Assert.Throws<InvalidOperationException>(() => LocalizationProjectSetup.Configure(new[] { "en" }, "en"));
            Assert.That(Directory.Exists(Root), Is.False);
        }
        [Test] public void Migration_PreservesGuidSettingsAndLanguageReferences()
        {
            var config = Config("Old.asset", false); LocalizationProjectSetup.AddLanguages(config, "en");
            config.defaultLanguage = config.languages[0]; var language = config.defaultLanguage;
            string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(config));
            LocalizationProjectSetup.UseForAutomaticStartup(config);
            Assert.That(AssetDatabase.AssetPathToGUID(LocalizationProjectSetup.ConfigAssetPath), Is.EqualTo(guid));
            Assert.That(config.defaultLanguage, Is.SameAs(language)); Assert.That(config.autoInitialize, Is.True);
            Assert.That(config.name, Is.EqualTo("SimplyLocalizeSettings"));
            LocalizationProjectSetup.UseForAutomaticStartup(config); Assert.That(Find(true), Is.SameAs(config));
        }
        [Test] public void Migration_RefusesOccupiedDestinationWithoutChangingSource()
        {
            var installed = Create().Config; var source = Config("Old.asset", false);
            string path = AssetDatabase.GetAssetPath(source); byte[] before = File.ReadAllBytes(path);
            Assert.Throws<IOException>(() => LocalizationProjectSetup.UseForAutomaticStartup(source));
            Assert.That(AssetDatabase.GetAssetPath(source), Is.EqualTo(path)); Assert.That(File.ReadAllBytes(path), Is.EqualTo(before));
            Assert.That(source.autoInitialize, Is.False); Assert.That(Find(true), Is.SameAs(installed));
        }
        [Test] public void DuplicateResourceAddress_IsRejectedBeforeAnyWrites()
        {
            Config("Resources/SimplyLocalizeSettings.asset", false);
            Assert.Throws<InvalidOperationException>(() => Create()); Assert.That(Directory.Exists(Root), Is.False);
        }
        [TestCase("../ru")] [TestCase("ru/en")] [TestCase("")]
        public void InvalidCodes_AreRejectedBeforeWrites(string code)
        {
            Assert.That(() => LocalizationProjectSetup.Configure(new[] { code }, code, null, true), Throws.Exception);
            Assert.That(Directory.Exists(Root), Is.False);
        }
        [Test] public void DuplicateCodes_AreRejectedBeforeWrites()
        {
            Assert.Throws<ArgumentException>(() => LocalizationProjectSetup.Configure(new[] { "en", "EN" }, "en", null, true));
            Assert.That(Directory.Exists(Root), Is.False);
        }
        [Test] public void OccupiedProfilePath_DoesNotPartiallyAddLanguages()
        {
            var config = Create().Config; string path = Root + "/Languages/pl.asset";
            File.WriteAllText(path, "foreign file");
            Assert.Throws<IOException>(() => LocalizationProjectSetup.AddLanguages(config, "de", "pl"));
            Assert.That(config.GetProfile("de"), Is.Null); Assert.That(File.Exists(Root + "/Languages/de.asset"), Is.False);
            Assert.That(File.ReadAllText(path), Is.EqualTo("foreign file"));
        }
        [Test] public void ReadOnlyConfig_DoesNotLeaveNewLanguageOrTranslation()
        {
            var config = Create().Config; string path = LocalizationProjectSetup.ConfigAssetPath;
            File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly);
            try
            {
                Assert.That(() => LocalizationProjectSetup.AddLanguages(config, "pl"), Throws.Exception);
                Assert.That(config.GetProfile("pl"), Is.Null); Assert.That(File.Exists(Root + "/Languages/pl.asset"), Is.False);
                Assert.That(File.Exists(Root + "/Resources/SimplyLocalize/Translations/pl/text/main.json"), Is.False);
            }
            finally { File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly); }
        }
        [Test] public void AssetOnlySetupAndCoverage_DoNotCreateOrLoadText()
        {
            var config = Config("AssetsOnly.asset", false); config.loadTextData = false;
            var result = LocalizationProjectSetup.AddLanguages(config, "en");
            Assert.That(result.CreatedPaths.Any(p => p.EndsWith(".json")), Is.False);
            var report = CoverageAnalyzer.Analyze(config);
            Assert.That(report.TextCoverage, Is.Empty); Assert.That(report.Warnings.Any(w => w.Message.Contains("No text keys")), Is.False);
            LogAssert.NoUnexpectedReceived();
        }
        [Test] public void FreshSetup_EditorCacheAndDiskEditingUseTheSameConfiguration()
        {
            var result = Create();
            SimplyLocalize.Editor.Data.EditorDataCache.SelectConfig(null);
            SimplyLocalize.Editor.Data.EditorDataCache.Invalidate();
            Assert.That(SimplyLocalize.Editor.Data.EditorDataCache.Config, Is.SameAs(result.Config));
            var data = new SimplyLocalize.Editor.Data.EditorLocalizationData(); data.Load(result.Config);
            Assert.That(data.LoadError, Is.Null); Assert.That(data.LoadedLanguages, Is.EquivalentTo(new[] { "en", "ru" }));
            data.AddKey("setup.test", "main"); data.SetTranslation("setup.test", "en", "Ready"); data.SaveAll();
            data.Reload(result.Config); Assert.That(data.GetTranslation("setup.test", "en"), Is.EqualTo("Ready"));
            Assert.That(AssetDatabase.GetAssetPath(result.Config), Is.EqualTo(LocalizationProjectSetup.ConfigAssetPath));
        }
    }
}
