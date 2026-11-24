using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NUnit.Framework;
using SimplyLocalize.Editor;
using SimplyLocalize.Editor.Data;
using SimplyLocalize.Editor.Utilities;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.TestTools;

namespace SimplyLocalize.Tests
{
    public class ConfigDiscoveryTests
    {
        private const string Root = "Assets/SimplyLocalize";
        private const string Startup = LocalizationProjectAuthoring.ConfigAssetPath;
        private string other;
        private string[] selection;
        private static readonly string[] SelectionKeys = { "SimplyLocalize.ProjectEditor.Config", "SimplyLocalize.ProjectEditor.Channel", "SimplyLocalize.ProjectEditor.Set" };
        [SetUp] public void Setup()
        {
            if (Directory.Exists(Root) || File.Exists(Root + ".meta")) Assert.Ignore("Existing project setup; use the dedicated lab.");
            selection = SelectionKeys.Select(k => SessionState.GetString(k, "")).ToArray();
            other = "Assets/SetupRegression_" + Guid.NewGuid().ToString("N"); Directory.CreateDirectory(other); AssetDatabase.Refresh();
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            if (other == null) yield break;
            yield return Done(Localization.ShutdownAsync()); AssetDatabase.DeleteAsset(Root); AssetDatabase.DeleteAsset(other); other = null;
            for (int i = 0; i < SelectionKeys.Length; i++) SessionState.SetString(SelectionKeys[i], selection[i]);
            EditorProjectSelection.Invalidate();
        }
        private static IEnumerator Done(Task task) { while (!task.IsCompleted) yield return null; task.GetAwaiter().GetResult(); }
        private static LocalizationProjectConfig Find(bool automatic)
        {
            var config = AssetDatabase.LoadAssetAtPath<LocalizationProjectConfig>(Startup);
            return automatic && config != null && !config.AutoInitialize ? null : config;
        }
        private static LocalizationProjectConfig Create() => LocalizationProjectAuthoring.Configure(new[] { "en", "ru" }, "en", "en").Config;
        private static void Save(LocalizationProjectConfig config) { EditorUtility.SetDirty(config); AssetDatabase.SaveAssetIfDirty(config); }
        private LocalizationProjectConfig Config(string relative, bool automatic = false)
        {
            string path = other + "/" + relative; Directory.CreateDirectory(Path.GetDirectoryName(path)); AssetDatabase.Refresh();
            var config = LocalizationProjectConfig.Create(new[] { "en" }); config.AutoInitialize = automatic;
            AssetDatabase.CreateAsset(config, path); Save(config); return config;
        }
        private static void ForeignStartup()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Startup)); AssetDatabase.Refresh();
            AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<LocalizationAppearance>(), Startup);
        }
        private static Task<LocalizationProjectConfig> Discover(bool automatic) => (Task<LocalizationProjectConfig>)typeof(Localization)
            .GetMethod("FindResourceConfigAsync", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { automatic });
        [UnityTest] public IEnumerator RuntimeDiscoveryRequiresProjectTypeAtFixedAddressAndHonorsAutoFlag()
        {
            ForeignStartup(); var missing = Localization.InitializeAsync(); while (!missing.IsCompleted) yield return null;
            Assert.That(missing.IsFaulted, Is.True); Assert.That(missing.Exception.ToString(), Does.Contain("Project Configuration"));
            AssetDatabase.DeleteAsset(Startup); var project = Create();
            yield return Done(Localization.InitializeAsync()); Assert.That(Localization.Config, Is.SameAs(project)); Assert.That(Localization.CurrentLanguage, Is.EqualTo("en"));
            yield return Done(Localization.ShutdownAsync()); project.AutoInitialize = false; Save(project);
            var automatic = (Task<LocalizationRuntime>)typeof(Localization).GetMethod("InitializeAutomaticallyAsync", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
            yield return Done(automatic); Assert.That(automatic.Result, Is.Null); Assert.That(Localization.IsInitialized, Is.False);
            yield return Done(Localization.InitializeAsync()); Assert.That(Localization.Config, Is.SameAs(project), "Explicit initialization may use a config with AutoInitialize disabled.");
        }
        [Test] public void SetupCreatesOneLightweightConfigWithUsableLanguagesAndExactSources()
        {
            var config = Create(); Assert.That(config.Validate(), Is.Empty);
            Assert.That(config.Languages.Select(l => l.Code), Is.EqualTo(new[] { "en", "ru" })); Assert.That(Find(true), Is.SameAs(config));
            Assert.That(config.MainChannel.DefaultLanguage, Is.EqualTo("en")); Assert.That(config.MainChannel.FallbackLanguage, Is.EqualTo("en"));
            Assert.That(AssetDatabase.FindAssets("t:LanguageProfile", new[] { Root }), Is.Empty);
            Assert.That(Directory.GetFiles(Root, "*.json", SearchOption.AllDirectories).Length, Is.EqualTo(2));
        }
        [Test] public void RepeatSetupPreservesGuidExternalTranslationsFlagsAndIdentities()
        {
            var config = Create(); config.AutoDetectLanguage = true; config.AutoInitialize = false; Save(config);
            string guid = AssetDatabase.AssetPathToGUID(Startup), channel = config.MainChannelId, set = config.Sets[0].Id;
            string path = Root + "/Resources/SimplyLocalize/Translations/en/Common.json";
            File.WriteAllText(path, "{\"translations\":{\"Menu/Play\":\"External edit\"},\"custom\":42}"); byte[] before = File.ReadAllBytes(path);
            var again = LocalizationProjectAuthoring.Configure(new[] { "en", "ru" }, "en", "en");
            Assert.That(again.Config, Is.SameAs(config)); Assert.That(again.Change.HasChanges, Is.False);
            Assert.That(config.AutoDetectLanguage, Is.True); Assert.That(Find(true), Is.Null); Assert.That(Find(false), Is.SameAs(config));
            Assert.That(config.MainChannelId, Is.EqualTo(channel)); Assert.That(config.Sets[0].Id, Is.EqualTo(set));
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(before)); Assert.That(AssetDatabase.AssetPathToGUID(Startup), Is.EqualTo(guid));
        }
        [Test] public void ExplicitConfigRemainsUsableOutsideResources()
        {
            var config = Config("Manual.asset"); using var runtime = new LocalizationRuntime(config);
            Assert.That(runtime.InitializeAsync().IsCompletedSuccessfully, Is.True); Assert.That(runtime.GetChannel().RequestedLanguage, Is.EqualTo("en"));
        }
        [UnityTest] public IEnumerator AutomaticConfigsAtOtherAddressesAreNotDiscovered()
        {
            Config("Resources/Nested/Other.asset", true); var task = Discover(true); yield return Done(task); Assert.That(task.Result, Is.Null);
            var explicitLookup = Discover(false); while (!explicitLookup.IsCompleted) yield return null;
            Assert.That(explicitLookup.IsFaulted, Is.True); Assert.That(Localization.IsInitialized, Is.False);
        }
        [UnityTest] public IEnumerator FixedAddressIgnoresOtherAutomaticConfigs()
        {
            var config = Create(); Config("Resources/Other.asset", true);
            var task = Discover(true); yield return Done(task); Assert.That(task.Result, Is.SameAs(config));
        }
        [Test] public void EditorRequiresExplicitChoiceWhenSeveralManualConfigsExist()
        {
            Config("One.asset"); var chosen = Config("Two.asset"); EditorProjectSelection.Select(null);
            Assert.That(EditorProjectSelection.Config, Is.Null); Assert.That(EditorProjectSelection.Message, Does.Contain("Select"));
            EditorProjectSelection.Select(chosen); Assert.That(EditorProjectSelection.Config, Is.SameAs(chosen)); Assert.That(File.Exists(Startup), Is.False);
        }
        [Test] public void StartupMovePreservesGuidLanguageIdentitiesAndSourceRouting()
        {
            var config = LocalizationProjectAuthoring.Create(new[] { "en" }, "en", null, other + "/Manual.asset", false);
            string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(config)), set = config.Sets[0].Id;
            string address = config.Sets[0].Sources[0].Sources[0].Address;
            LocalizationProjectAuthoring.UseForAutomaticStartup(config);
            Assert.That(AssetDatabase.AssetPathToGUID(Startup), Is.EqualTo(guid)); Assert.That(config.Sets[0].Id, Is.EqualTo(set));
            Assert.That(config.Sets[0].Sources[0].Sources[0].Address, Is.EqualTo(address)); Assert.That(config.AutoInitialize, Is.True);
            LocalizationProjectAuthoring.UseForAutomaticStartup(config); Assert.That(Find(true), Is.SameAs(config));
        }
        [Test] public void StartupMoveRefusesOccupiedDestinationWithoutChangingSource()
        {
            var installed = Create(); var source = Config("Manual.asset"); string path = AssetDatabase.GetAssetPath(source); byte[] before = File.ReadAllBytes(path);
            Assert.Throws<InvalidOperationException>(() => LocalizationProjectAuthoring.UseForAutomaticStartup(source));
            Assert.That(AssetDatabase.GetAssetPath(source), Is.EqualTo(path)); Assert.That(File.ReadAllBytes(path), Is.EqualTo(before));
            Assert.That(source.AutoInitialize, Is.False); Assert.That(Find(true), Is.SameAs(installed));
        }
        [Test] public void DuplicateResourceAddressIsRejectedBeforeAnyWrites()
        {
            Config("Resources/SimplyLocalizeSettings.asset"); Assert.Throws<InvalidOperationException>(() => Create()); Assert.That(Directory.Exists(Root), Is.False);
        }
        [TestCase("../ru")][TestCase("ru/en")][TestCase("")]
        public void InvalidCodesAreRejectedBeforeWrites(string code)
        { Assert.Throws<ArgumentException>(() => LocalizationProjectAuthoring.Configure(new[] { code }, code)); Assert.That(Directory.Exists(Root), Is.False); }
        [Test] public void DuplicateNormalizedCodesAreRejectedBeforeWrites()
        { Assert.Throws<ArgumentException>(() => LocalizationProjectAuthoring.Configure(new[] { "en", "EN" }, "en")); Assert.That(Directory.Exists(Root), Is.False); }
        [Test] public void InvalidExistingSourceDoesNotPartiallyAddLanguages()
        {
            var config = Create(); string path = Root + "/Resources/SimplyLocalize/Translations/pl/Common.json";
            Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, "foreign file");
            Assert.Catch<Exception>(() => LocalizationProjectAuthoring.Configure(new[] { "en", "ru", "de", "pl" }, "en"));
            Assert.That(config.Languages.Select(l => l.Code), Is.EqualTo(new[] { "en", "ru" })); Assert.That(File.ReadAllText(path), Is.EqualTo("foreign file"));
            Assert.That(File.Exists(Root + "/Resources/SimplyLocalize/Translations/de/Common.json"), Is.False);
        }
        [Test] public void ReadOnlyConfigDoesNotLeaveNewLanguageOrTranslation()
        {
            var config = Create(); var attributes = File.GetAttributes(Startup); File.SetAttributes(Startup, attributes | FileAttributes.ReadOnly);
            try { Assert.Catch<Exception>(() => LocalizationProjectAuthoring.AddLanguage(config, "pl")); }
            finally { File.SetAttributes(Startup, attributes); }
            Assert.That(config.Languages.Any(l => l.Code == "pl"), Is.False); Assert.That(File.Exists(Root + "/Resources/SimplyLocalize/Translations/pl/Common.json"), Is.False);
        }
        [Test] public void AssetOnlySetupAndCoverageDoNotCreateTextSources()
        {
            var config = Config("AssetsOnly.asset"); config.Sets[0].AddEntry("icon", LocalizationEntryKind.Asset); Save(config);
            LocalizationProjectAuthoring.AddLanguage(config, "ru");
            Assert.That(config.Sets[0].Sources, Is.Empty); Assert.That(Directory.GetFiles(other, "*.json", SearchOption.AllDirectories), Is.Empty);
            var report = ProjectCoverageAnalyzer.Analyze(config); Assert.That(report.TextCoverage.All(row => row.TotalCount == 0), Is.True);
            Assert.That(report.Warnings.Any(w => w.Message.Contains("No text")), Is.False);
        }
        [Test] public void FreshSetupEditorSelectionAndExternalEditingUseSameConfiguration()
        {
            var config = Create(); EditorProjectSelection.Select(null); Assert.That(EditorProjectSelection.Config, Is.SameAs(config));
            string entry; using (var edit = new LocalizationProjectEdit(config, config.Sets[0].Id))
            { entry = edit.Draft.Sets[0].AddEntry("setup.test").Id; edit.SetTranslation(entry, "en", "Ready"); edit.Commit(); }
            EditorProjectSelection.Invalidate(); Assert.That(EditorProjectSelection.Current.Text.TryGetDirect(entry, "en", out var cell), Is.True); Assert.That(cell.Value, Is.EqualTo("Ready"));
        }

        private static void BuildCheck() => ((IPreprocessBuildWithReport)Activator.CreateInstance(typeof(LocalizationProjectAuthoring).Assembly
            .GetType("SimplyLocalize.Editor.LocalizationStartupBuildCheck"), true)).OnPreprocessBuild(null);
        [Test] public void BuildGateAcceptsProjectConfigWithoutLoadingItsContents()
        {
            var config = Create(); config.Sets[0].SetSources("en", new LocalizationSourceReference("custom", "remote-table", LocalizationSourceKind.TextTable)); Save(config);
            Assert.DoesNotThrow(BuildCheck); Assert.That(Localization.IsInitialized, Is.False); LogAssert.NoUnexpectedReceived();
        }
        [Test] public void BuildGateAllowsExplicitBootstrapWithoutStartupConfig()
        { Config("Manual.asset"); Assert.DoesNotThrow(BuildCheck); Assert.That(File.Exists(Startup), Is.False); }
        [Test] public void BuildGateRejectsWrongTypeAtStartupAddress()
        { ForeignStartup(); Assert.That(Assert.Throws<BuildFailedException>(BuildCheck).Message, Does.Contain("LocalizationProjectConfig").And.Contain(Startup)); }
        [Test] public void BuildGateRejectsDuplicateAddressWithoutChangingEitherAsset()
        {
            Create(); var otherConfig = Config("Resources/SimplyLocalizeSettings.asset"); byte[] before = File.ReadAllBytes(Startup);
            Assert.That(Assert.Throws<BuildFailedException>(BuildCheck).Message, Does.Contain(AssetDatabase.GetAssetPath(otherConfig)));
            Assert.That(File.ReadAllBytes(Startup), Is.EqualTo(before));
        }
        [Test] public void BuildGateRejectsInvalidStartupConfiguration()
        {
            var config = Create(); config.MainChannel.DefaultLanguage = "pl"; Save(config);
            Assert.That(Assert.Throws<BuildFailedException>(BuildCheck).Message, Does.Contain("Default language"));
        }
        [Test] public void BuildGateWarnsAboutIgnoredAutomaticConfig()
        {
            var config = Config("Resources/Other.asset", true);
            LogAssert.Expect(LogType.Warning, new Regex("Auto-initialize config is outside the startup path: " + Regex.Escape(AssetDatabase.GetAssetPath(config))));
            BuildCheck(); LogAssert.NoUnexpectedReceived();
        }
        private static LocalizationProjectAuthoring.Result Command(params string[] args)
        {
            try { return (LocalizationProjectAuthoring.Result)typeof(LocalizationProjectAuthoring).GetMethod("ConfigureFromArguments", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { args }); }
            catch (TargetInvocationException error) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
        }
        [Test] public void CommandLineUsesSameConfigurationAndRepeatedSetupPreservesExternalText()
        {
            var config = Command("Unity", "-batchmode", "-slLanguages", "EN,pt-br", "-slDefault", "en", "-slFallback", "pt-BR").Config;
            Assert.That(config.Languages.Select(l => l.Code), Is.EqualTo(new[] { "en", "pt-BR" })); Assert.That(config.MainChannel.FallbackLanguage, Is.EqualTo("pt-BR"));
            string source = Root + "/Resources/SimplyLocalize/Translations/en/Common.json"; File.WriteAllText(source, "{\"translations\":{\"external\":\"Keep\"}}");
            byte[] before = File.ReadAllBytes(source); string guid = AssetDatabase.AssetPathToGUID(Startup);
            Command("-slLanguages", "en,pt-BR", "-slDefault", "pt-BR"); Assert.That(config.MainChannel.FallbackLanguage, Is.Null.Or.Empty);
            Assert.That(File.ReadAllBytes(source), Is.EqualTo(before)); Assert.That(AssetDatabase.AssetPathToGUID(Startup), Is.EqualTo(guid));
        }
        [TestCase("")][TestCase("-slLanguages en,ru")][TestCase("-slLanguages -slDefault en")]
        [TestCase("-slLanguages en,ru -slDefault")][TestCase("-slLanguages en,ru -slDefault en -slFallback")]
        [TestCase("-slLanguages en,en -slDefault en")][TestCase("-slLanguages en -slDefault ru")]
        [TestCase("-slLanguages en -slDefault en -slDefault ru")][TestCase("-slLanguages en,,ru -slDefault en")]
        public void InvalidCommandLineCreatesNothing(string command)
        { Assert.Throws<ArgumentException>(() => Command(command.Split(' '))); Assert.That(Directory.Exists(Root), Is.False); }
    }
}
