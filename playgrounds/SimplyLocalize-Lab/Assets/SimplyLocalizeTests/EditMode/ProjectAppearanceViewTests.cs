using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using SimplyLocalize.Editor;
using SimplyLocalize.Editor.Data;
using SimplyLocalize.Editor.Windows;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace SimplyLocalize.Tests
{
    public class ProjectAppearanceViewTests
    {
        private string root;
        private LocalizationProjectConfig config;
        private EditorWindow host;
        private ProjectAppearanceView view;
        private HashSet<int> priorUndo;
        [SetUp] public void Setup()
        {
            root = "Assets/SimplyLocalizeAppearanceView_" + Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(root); AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            config = LocalizationProjectAuthoring.Create(new[] { "en", "ru" }, "en", "en", root + "/Project.asset", false);
            priorUndo = new HashSet<int>(Resources.FindObjectsOfTypeAll<LocalizationProjectUndo>().Select(r => r.GetInstanceID()));
            host = ScriptableObject.CreateInstance<AppearanceHost>(); host.position = new Rect(100, 100, 850, 700); host.ShowUtility(); Open();
        }
        [TearDown] public void Cleanup()
        {
            view?.Dispose(); if (host != null) host.Close();
            if (priorUndo != null) foreach (var record in Resources.FindObjectsOfTypeAll<LocalizationProjectUndo>().Where(r => !priorUndo.Contains(r.GetInstanceID()))) record.Dispose();
            AssetDatabase.DeleteAsset(root);
        }
        private void Open(string language = "en", Func<LocalizationSourceReference, LocalizationAppearance> reader = null)
        { view?.Dispose(); host.rootVisualElement.Clear(); view = new ProjectAppearanceView(config, language, reader: reader); host.rootVisualElement.Add(view); }
        private T Field<T>(string name) where T : VisualElement => host.rootVisualElement.Q<T>(name);
        private void Click(string name)
        { var button = Field<Button>(name); Assert.That(button, Is.Not.Null); using var submit = NavigationSubmitEvent.GetPooled(); submit.target = button; button.SendEvent(submit); }
        private string AppearancePath(string language) => new ResourcesEditorTextReader().ResolveSourceAssetPath(config.Languages.Single(l => l.Code == language).Appearance);
        private LocalizationAppearance Appearance(string language) => AssetDatabase.LoadAssetAtPath<LocalizationAppearance>(AppearancePath(language));
        private void Share()
        {
            using var edit = new LocalizationProjectEdit(config); edit.Draft.Languages[1].Appearance = edit.Draft.Languages[0].Appearance; edit.Commit();
        }
        [Test] public void CreateBuildsAddressBackedAppearanceAndShowsDisabledOverrideFields()
        {
            var selected = Selection.activeObject; Click("createAppearance"); string path = AppearancePath("en");
            Assert.That(File.Exists(path), Is.True); Assert.That(path, Does.EndWith("/SimplyLocalize/Appearance/en.asset"));
            Assert.That(Field<ObjectField>("primaryFont"), Is.Not.Null); Assert.That(Field<ObjectField>("primaryFont").enabledInHierarchy, Is.False);
            Assert.That(Field<Toggle>("overrideFont").enabledInHierarchy, Is.True); Assert.That(view.HasPendingChanges, Is.False);
            Assert.That(AssetDatabase.GetDependencies(AssetDatabase.GetAssetPath(config), true), Does.Not.Contain(path));
            Assert.That(Selection.activeObject, Is.SameAs(selected));
        }
        [Test] public void OverrideDraftSavesExplicitlyAndUndoUpdatesWindow()
        {
            Click("createAppearance"); var original = Appearance("en");
            Field<Toggle>("overrideTypography").value = true; Field<Slider>("fontSizeMultiplier").value = 1.4f;
            Assert.That(original.overrideTypography, Is.False); Assert.That(original.fontSizeMultiplier, Is.EqualTo(1)); Assert.That(view.HasPendingChanges, Is.True);
            Click("saveAppearance"); Assert.That(Appearance("en").overrideTypography, Is.True); Assert.That(Appearance("en").fontSizeMultiplier, Is.EqualTo(1.4f));
            Undo.PerformUndo(); Assert.That(Field<Toggle>("overrideTypography").value, Is.False); Assert.That(Field<Slider>("fontSizeMultiplier").value, Is.EqualTo(1));
            Undo.PerformRedo(); Assert.That(Field<Slider>("fontSizeMultiplier").value, Is.EqualTo(1.4f));
        }
        [Test] public void IndependentCopyKeepsOriginalValuesAndOtherLanguagesAndUndoPreservesGuid()
        {
            Click("createAppearance"); Field<Toggle>("overrideTypography").value = true; Field<Slider>("fontSizeMultiplier").value = 1.4f; Click("saveAppearance");
            Share(); Open("ru"); string enPath = AppearancePath("en");
            Assert.That(Field<HelpBox>("sharedAppearance").text, Does.Contain("en").And.Contain("ru")); Click("copyAppearance");
            string ruPath = AppearancePath("ru"), guid = AssetDatabase.AssetPathToGUID(ruPath);
            Assert.That(ruPath, Is.Not.EqualTo(enPath)); Assert.That(Appearance("ru").fontSizeMultiplier, Is.EqualTo(1.4f));
            Field<Slider>("fontSizeMultiplier").value = 1.7f; Click("saveAppearance");
            Assert.That(Appearance("en").fontSizeMultiplier, Is.EqualTo(1.4f)); Assert.That(Appearance("ru").fontSizeMultiplier, Is.EqualTo(1.7f));
            Undo.PerformUndo(); Undo.PerformUndo(); Assert.That(AppearancePath("ru"), Is.EqualTo(enPath)); Assert.That(File.Exists(ruPath), Is.False);
            Undo.PerformRedo(); Assert.That(AssetDatabase.AssetPathToGUID(ruPath), Is.EqualTo(guid)); Assert.That(Appearance("ru").fontSizeMultiplier, Is.EqualTo(1.4f));
        }
        [Test] public void ExternalAssetChangeStopsSaveAndKeepsTypedDraftUntilDiscard()
        {
            Click("createAppearance"); Field<Toggle>("overrideTypography").value = true; Field<Slider>("fontSizeMultiplier").value = 1.4f;
            var asset = Appearance("en"); asset.fontSizeMultiplier = 1.2f; EditorUtility.SetDirty(asset); AssetDatabase.SaveAssetIfDirty(asset);
            Assert.That(view.SaveChanges(), Is.False); Assert.That(Field<Slider>("fontSizeMultiplier").value, Is.EqualTo(1.4f));
            Assert.That(Appearance("en").fontSizeMultiplier, Is.EqualTo(1.2f)); Assert.That(view.Reload(), Is.False);
            Assert.That(view.Reload(true), Is.True); Assert.That(Field<Slider>("fontSizeMultiplier").value, Is.EqualTo(1.2f));
        }
        [Test] public void DisconnectKeepsSharedFileAndAnotherLanguagesConnection()
        {
            Click("createAppearance"); Share(); Open(); string path = AppearancePath("ru"); byte[] bytes = File.ReadAllBytes(path);
            Click("disconnectAppearance"); Assert.That(config.Languages[0].Appearance, Is.Null); Assert.That(AppearancePath("ru"), Is.EqualTo(path));
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(bytes)); Undo.PerformUndo(); Assert.That(AppearancePath("en"), Is.EqualTo(path));
        }
        [Test] public void NonResourcesConnectionFailsInlineWithoutChangingConfig()
        {
            var asset = ScriptableObject.CreateInstance<LocalizationAppearance>(); AssetDatabase.CreateAsset(asset, root + "/OutsideResources.asset"); AssetDatabase.SaveAssetIfDirty(asset);
            Field<ObjectField>("appearanceAsset").value = asset; Click("connectAppearance");
            Assert.That(config.Languages[0].Appearance, Is.Null); Assert.That(Field<HelpBox>("appearanceMessage").text, Does.Contain("Resources"));
        }
        [Test] public void CustomReaderPreviewIsReadOnlyAndNotOwnedByTheView()
        {
            using (var edit = new LocalizationProjectEdit(config))
            { edit.Draft.Languages[0].Appearance = new("game", "appearance", LocalizationSourceKind.Appearance); edit.Commit(); }
            var external = ScriptableObject.CreateInstance<LocalizationAppearance>(); external.overrideTypography = true; external.fontSizeMultiplier = 1.3f;
            try
            {
                Open(reader: _ => external); Assert.That(Field<Slider>("fontSizeMultiplier").value, Is.EqualTo(1.3f));
                Assert.That(Field<Slider>("fontSizeMultiplier").enabledInHierarchy, Is.False);
                Field<Slider>("fontSizeMultiplier").value = 1.8f; Assert.That(external.fontSizeMultiplier, Is.EqualTo(1.3f));
                view.Dispose(); Assert.That(external != null, Is.True);
            }
            finally { Object.DestroyImmediate(external); }
        }
        [Test] public void SelectingAnotherLanguageDoesNotDiscardPendingAppearance()
        {
            Click("createAppearance"); Field<Toggle>("overrideTypography").value = true;
            Assert.Throws<InvalidOperationException>(() => view.SelectLanguage("ru")); Assert.That(view.SelectedLanguage, Is.EqualTo("en"));
            view.Reload(true); view.SelectLanguage("ru"); Assert.That(Field<Button>("createAppearance"), Is.Not.Null);
            Assert.That(config.MainChannel.DefaultLanguage, Is.EqualTo("en")); Assert.That(config.Languages[1].Appearance, Is.Null);
        }
        private sealed class AppearanceHost : EditorWindow { }
    }
}
