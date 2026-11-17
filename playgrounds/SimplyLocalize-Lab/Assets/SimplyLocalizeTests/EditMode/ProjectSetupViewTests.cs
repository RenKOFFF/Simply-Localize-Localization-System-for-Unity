using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using SimplyLocalize.Editor;
using SimplyLocalize.Editor.Data;
using SimplyLocalize.Editor.Windows;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace SimplyLocalize.Tests
{
    public class ProjectSetupViewTests
    {
        private const string Root = "Assets/SimplyLocalize";
        private bool ownsRoot;
        private EditorWindow host;
        private LocalizationProjectConfig selected;
        [SetUp] public void Setup()
        {
            ownsRoot = false; host = null; selected = null;
            if (Directory.Exists(Root) || File.Exists(Root + ".meta")) Assert.Ignore("Use a clean lab without a user-owned SimplyLocalize setup folder.");
            ownsRoot = true;
            host = ScriptableObject.CreateInstance<SetupHost>(); host.ShowUtility();
            host.rootVisualElement.Add(new ProjectSetupView(host, config => selected = config));
        }
        [TearDown] public void Cleanup()
        {
            if (host != null) host.Close();
            if (ownsRoot) AssetDatabase.DeleteAsset(Root);
        }
        private void Submit()
        {
            var button = host.rootVisualElement.Q<Button>("createConfig"); Assert.That(button.enabledInHierarchy, Is.True);
            using var submit = NavigationSubmitEvent.GetPooled(); submit.target = button; button.SendEvent(submit);
        }
        [Test] public void CreateButtonPublishesProjectConfigWithMainCommonAndPreservesEditorSelection()
        {
            var before = Selection.activeObject; Submit();
            Assert.That(selected, Is.Not.Null); Assert.That(selected.AutoInitialize, Is.True);
            Assert.That(selected.MainChannel.Name, Is.EqualTo("Main")); Assert.That(selected.Sets[0].Name, Is.EqualTo("Common"));
            Assert.That(selected.Languages.Select(l => l.Code), Is.EqualTo(new[] { "en", "ru" }));
            Assert.That(Selection.activeObject, Is.SameAs(before)); Assert.That(selected.MainChannel.FallbackLanguage, Is.EqualTo("en"));
        }
        [Test] public void OccupiedStartupAddressProducesInlineErrorAndLeavesForeignData()
        {
            Directory.CreateDirectory(Root + "/Resources"); string path = Root + "/Resources/SimplyLocalizeSettings.txt";
            File.WriteAllText(path, "Foreign"); AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport); Submit();
            Assert.That(selected, Is.Null); Assert.That(File.ReadAllText(path), Is.EqualTo("Foreign"));
            Assert.That(host.rootVisualElement.Q<HelpBox>("setupMessage").text, Does.Contain("occupied"));
            Assert.That(File.Exists(LocalizationProjectAuthoring.ConfigAssetPath), Is.False);
        }
        [Test] public void RepeatedSetupPreservesIdsNamesAndExternalTranslations()
        {
            var first = LocalizationProjectAuthoring.Configure(new[] { "en", "ru" }, "en", "en",
                new Dictionary<string, string> { ["en"] = "My English" }).Config;
            string main = first.MainChannelId, set = first.Sets[0].Id;
            string file = Root + "/Resources/SimplyLocalize/Translations/en/Common.json";
            File.WriteAllText(file, "{\"translations\":{\"external\":\"Keep\"}}"); byte[] before = File.ReadAllBytes(file);
            var result = LocalizationProjectAuthoring.Configure(new[] { "en", "ru", "x-elvish" }, "ru", null,
                new Dictionary<string, string> { ["en"] = "English", ["x-elvish"] = "Elvish" });
            Assert.That(result.Config, Is.SameAs(first)); Assert.That(first.MainChannelId, Is.EqualTo(main)); Assert.That(first.Sets[0].Id, Is.EqualTo(set));
            Assert.That(first.Languages.Single(l => l.Code == "en").DisplayName, Is.EqualTo("My English"));
            Assert.That(first.Languages.Single(l => l.Code == "x-elvish").DisplayName, Is.EqualTo("Elvish"));
            Assert.That(first.MainChannel.DefaultLanguage, Is.EqualTo("ru")); Assert.That(first.MainChannel.FallbackLanguage, Is.Empty.Or.Null);
            Assert.That(File.ReadAllBytes(file), Is.EqualTo(before));
        }
        [Test] public void MultiLanguageSetupFailureDoesNotLeaveEarlierPartialLanguage()
        {
            var config = LocalizationProjectAuthoring.Configure(new[] { "en", "ru" }, "en", "en").Config;
            string conflict = Root + "/Resources/SimplyLocalize/Translations/de/Common.json";
            Directory.CreateDirectory(Path.GetDirectoryName(conflict)); File.WriteAllText(conflict, "Foreign");
            Assert.Catch<Exception>(() => LocalizationProjectAuthoring.Configure(new[] { "en", "ru", "pl", "de" }, "en", "en"));
            Assert.That(config.Languages.Select(l => l.Code), Is.EqualTo(new[] { "en", "ru" }));
            Assert.That(File.Exists(Root + "/Resources/SimplyLocalize/Translations/pl/Common.json"), Is.False);
            Assert.That(File.ReadAllText(conflict), Is.EqualTo("Foreign"));
        }
        [Test] public void StartupMovePreservesGuidPendingSettingsIdentitiesAndSourceBytes()
        {
            var config = LocalizationProjectAuthoring.Create(new[] { "en", "ru" }, "en", "en", Root + "/Manual.asset", false);
            string guid = AssetDatabase.AssetPathToGUID(Root + "/Manual.asset"), main = config.MainChannelId;
            string text = Root + "/Resources/SimplyLocalize/Translations/en/Common.json"; byte[] bytes = File.ReadAllBytes(text);
            config.MainChannel.DefaultLanguage = "ru"; config.AutoDetectLanguage = true; EditorUtility.SetDirty(config);
            LocalizationProjectAuthoring.UseForAutomaticStartup(config);
            Assert.That(AssetDatabase.GetAssetPath(config), Is.EqualTo(LocalizationProjectAuthoring.ConfigAssetPath));
            Assert.That(AssetDatabase.AssetPathToGUID(LocalizationProjectAuthoring.ConfigAssetPath), Is.EqualTo(guid));
            Assert.That(config.MainChannelId, Is.EqualTo(main)); Assert.That(config.MainChannel.DefaultLanguage, Is.EqualTo("ru"));
            Assert.That(config.AutoDetectLanguage && config.AutoInitialize, Is.True); Assert.That(EditorUtility.IsDirty(config), Is.False);
            Assert.That(File.ReadAllBytes(text), Is.EqualTo(bytes)); Assert.That(File.Exists(Root + "/Manual.asset"), Is.False);
        }
        [Test] public void StartupMoveRefusesConflictingAddressWithoutChangingEitherFile()
        {
            var config = LocalizationProjectAuthoring.Create(new[] { "en" }, "en", null, Root + "/Manual.asset", false);
            string other = Root + "/Resources/SimplyLocalizeSettings.txt"; File.WriteAllText(other, "Keep");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport); byte[] bytes = File.ReadAllBytes(Root + "/Manual.asset");
            Assert.Throws<InvalidOperationException>(() => LocalizationProjectAuthoring.UseForAutomaticStartup(config));
            Assert.That(AssetDatabase.GetAssetPath(config), Is.EqualTo(Root + "/Manual.asset")); Assert.That(config.AutoInitialize, Is.False);
            Assert.That(File.ReadAllBytes(Root + "/Manual.asset"), Is.EqualTo(bytes)); Assert.That(File.ReadAllText(other), Is.EqualTo("Keep"));
        }
        [Test] public void StartupMoveRefusesReadOnlyConfigBeforeNativeMoveAndKeepsPendingEdits()
        {
            string path = Root + "/Manual.asset";
            var config = LocalizationProjectAuthoring.Create(new[] { "en" }, "en", null, path, false);
            config.AutoDetectLanguage = true; EditorUtility.SetDirty(config); var attributes = File.GetAttributes(path);
            File.SetAttributes(path, attributes | FileAttributes.ReadOnly);
            try { Assert.Throws<IOException>(() => LocalizationProjectAuthoring.UseForAutomaticStartup(config)); }
            finally { File.SetAttributes(path, attributes); }
            Assert.That(AssetDatabase.GetAssetPath(config), Is.EqualTo(path)); Assert.That(config.AutoDetectLanguage, Is.True);
            Assert.That(EditorUtility.IsDirty(config), Is.True); Assert.That(File.Exists(LocalizationProjectAuthoring.ConfigAssetPath), Is.False);
        }
        private sealed class SetupHost : EditorWindow { }
    }
}
