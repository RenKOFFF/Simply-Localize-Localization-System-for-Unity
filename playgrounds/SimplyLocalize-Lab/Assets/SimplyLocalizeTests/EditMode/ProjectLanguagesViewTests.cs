using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using SimplyLocalize.Editor;
using SimplyLocalize.Editor.Data;
using SimplyLocalize.Editor.Windows;
using SimplyLocalize.Pluralization;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace SimplyLocalize.Tests
{
    public class ProjectLanguagesViewTests
    {
        private string root;
        private LocalizationProjectConfig config;
        private EditorWindow host;
        private HashSet<int> previousUndo;
        [SetUp] public void Setup()
        {
            root = "Assets/SimplyLocalizeLanguageView_" + Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(root); AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            config = LocalizationProjectAuthoring.Create(new[] { "en", "ru" }, "en", "en", root + "/Project.asset", false);
            previousUndo = new HashSet<int>(Resources.FindObjectsOfTypeAll<LocalizationProjectUndo>().Select(r => r.GetInstanceID()));
            host = ScriptableObject.CreateInstance<LanguageHost>(); host.ShowUtility();
        }
        [TearDown] public void Cleanup()
        {
            if (host != null) host.Close();
            if (previousUndo != null)
                foreach (var record in Resources.FindObjectsOfTypeAll<LocalizationProjectUndo>().Where(r => !previousUndo.Contains(r.GetInstanceID()))) record.Dispose();
            AssetDatabase.DeleteAsset(root);
        }
        private void Open(string channel = null, LocalizationPluralRules rules = null, Action<string> appearance = null)
        { host.rootVisualElement.Clear(); host.rootVisualElement.Add(new ProjectLanguagesView(config, host, channel, editAppearance: appearance, pluralRules: rules)); }
        private T Field<T>(string name) where T : VisualElement => host.rootVisualElement.Q<T>(name);
        private void Click(string name)
        {
            var button = Field<Button>(name); Assert.That(button, Is.Not.Null);
            using var submit = NavigationSubmitEvent.GetPooled(); submit.target = button; button.SendEvent(submit);
        }
        private void Save() { EditorUtility.SetDirty(config); AssetDatabase.SaveAssetIfDirty(config); }

        [Test] public void DefaultsSaveThroughWindowAndNativeUndoRefreshesTheDisplayedValue()
        {
            Open(); Field<DropdownField>("defaultLanguage").value = "ru";
            Assert.That(config.MainChannel.DefaultLanguage, Is.EqualTo("ru")); Assert.That(EditorUtility.IsDirty(config), Is.False);
            Undo.PerformUndo(); Assert.That(config.MainChannel.DefaultLanguage, Is.EqualTo("en")); Assert.That(Field<DropdownField>("defaultLanguage").value, Is.EqualTo("en"));
            Undo.PerformRedo(); Assert.That(Field<DropdownField>("defaultLanguage").value, Is.EqualTo("ru"));
        }
        [Test] public void AvailabilityAndDefaultsBelongToSelectedChannel()
        {
            string voice = config.AddChannel("Voice", new[] { "en" }).Id; Save(); Open(voice);
            Assert.That(Field<Toggle>("languageAvailable_ru").value, Is.False);
            Field<Toggle>("languageAvailable_ru").value = true; Field<DropdownField>("defaultLanguage").value = "ru";
            Assert.That(config.Channels.Single(c => c.Id == voice).DefaultLanguage, Is.EqualTo("ru")); Assert.That(config.MainChannel.DefaultLanguage, Is.EqualTo("en"));
            Field<Toggle>("languageAvailable_ru").value = false;
            Assert.That(Field<HelpBox>("languageMessage").text, Does.Contain("defaults"));
            Assert.That(config.Channels.Single(c => c.Id == voice).Languages, Does.Contain("ru"));
        }
        [Test] public void RemovingLanguageDropsBindingsButKeepsSourceFiles()
        {
            Open(); string path = root + "/Resources/SimplyLocalize/Translations/ru/Common.json"; byte[] before = File.ReadAllBytes(path);
            Click("removeLanguage_ru");
            Assert.That(config.Languages.Select(l => l.Code), Does.Not.Contain("ru")); Assert.That(config.MainChannel.Languages, Does.Not.Contain("ru"));
            Assert.That(config.Sets[0].Sources.Any(s => s.Language == "ru"), Is.False); Assert.That(File.ReadAllBytes(path), Is.EqualTo(before));
            Undo.PerformUndo(); Assert.That(config.Languages.Select(l => l.Code), Does.Contain("ru")); Assert.That(config.Sets[0].Sources.Any(s => s.Language == "ru"), Is.True);
        }
        [Test] public void ReaddingRemovedLanguageReusesItsPreservedSourceWithoutRewritingIt()
        {
            Open(); string path = root + "/Resources/SimplyLocalize/Translations/ru/Common.json";
            File.WriteAllText(path, "{\"translations\":{\"external\":\"Сохранить\"}}"); byte[] before = File.ReadAllBytes(path);
            string guid = AssetDatabase.AssetPathToGUID(path); Click("removeLanguage_ru");
            LocalizationProjectAuthoring.AddLanguage(config, "ru");
            Assert.That(config.Languages.Select(l => l.Code), Does.Contain("ru")); Assert.That(config.Sets[0].Sources.Any(s => s.Language == "ru"), Is.True);
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(before)); Assert.That(AssetDatabase.AssetPathToGUID(path), Is.EqualTo(guid));
        }
        [Test] public void ReadOnlyConfigReportsInlineFailureWithoutChangingDefaults()
        {
            Open(); string path = AssetDatabase.GetAssetPath(config); var attributes = File.GetAttributes(path); File.SetAttributes(path, attributes | FileAttributes.ReadOnly);
            try
            {
                Field<DropdownField>("defaultLanguage").value = "ru";
                Assert.That(config.MainChannel.DefaultLanguage, Is.EqualTo("en")); Assert.That(Field<HelpBox>("languageMessage"), Is.Not.Null);
                Assert.That(Field<DropdownField>("defaultLanguage").value, Is.EqualTo("en"));
            }
            finally { File.SetAttributes(path, attributes); }
        }
        [Test] public void PluralStatusDistinguishesGeneralAndInjectedCustomRuleWithoutBlockingLanguage()
        {
            config.AddLanguage("x-elvish", "Elvish"); Save(); Open();
            Assert.That(Field<Label>("pluralStatus_x-elvish").text, Does.Contain("General rule"));
            var rules = new LocalizationPluralRules(); rules.Register("x-elvish", new OneForm()); Open(rules: rules);
            Assert.That(Field<Label>("pluralStatus_x-elvish").text, Does.Contain("Custom rule"));
            Assert.That(Field<Label>("pluralStatus_en").text, Does.Contain("Built-in"));
        }
        [Test] public void AppearanceActionStaysInsideTheOwnerFlowAndDoesNotSelectAssets()
        {
            string language = null; var selected = Selection.activeObject;
            Open(appearance: code => language = code); Click("editAppearance_ru");
            Assert.That(language, Is.EqualTo("ru")); Assert.That(Selection.activeObject, Is.SameAs(selected));
        }
        [Test] public void EditorDefaultChangeDoesNotSwitchAnInitializedGameRuntime()
        {
            var registry = new LocalizationSourceRegistry(false); registry.Register("resources", new EmptyProvider());
            using var runtime = new LocalizationRuntime(config, registry); var initialization = runtime.InitializeAsync("en");
            Assert.That(initialization.IsCompletedSuccessfully, Is.True); var snapshot = runtime.GetChannel();
            Open(); Field<DropdownField>("defaultLanguage").value = "ru";
            Assert.That(runtime.GetChannel(), Is.SameAs(snapshot)); Assert.That(snapshot.RequestedLanguage, Is.EqualTo("en"));
            Assert.That(runtime.ReleaseAsync().IsCompletedSuccessfully, Is.True);
        }
        private sealed class LanguageHost : EditorWindow { }
        private sealed class OneForm : IPluralRule { public int FormCount => 1; public int Evaluate(int count) => 0; }
        private sealed class EmptyProvider : ILocalizationSourceProvider
        {
            public LocalizationProviderCapabilities GetCapabilities(LocalizationSourceKind kind) => new(LocalizationLoadUnit.Table, false, true);
            public Task<LocalizationSourceLease> LoadAsync(LocalizationSourceRequest request, CancellationToken token) =>
                Task.FromResult(new LocalizationSourceLease(LocalizationSourceData.FromText(new Dictionary<string, string>())));
        }
    }
}
