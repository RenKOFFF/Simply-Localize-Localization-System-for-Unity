using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using SimplyLocalize.Components;
using SimplyLocalize.Editor;
using SimplyLocalize.Editor.Data;
using SimplyLocalize.Editor.Windows;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace SimplyLocalize.Tests
{
    public class ProjectAutoLocalizeViewTests
    {
        private string root, source;
        private LocalizationProjectConfig config;
        private GameObject label;
        private EditorWindow host;
        private ProjectAutoLocalizeView view;
        private HashSet<int> previousUndo;
        private int undoGroup;
        [SetUp] public void Setup()
        {
            undoGroup = 0; previousUndo = null;
            root = "Assets/SimplyLocalizeAutoView_" + Guid.NewGuid().ToString("N"); Directory.CreateDirectory(root); AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            config = LocalizationProjectAuthoring.Create(new[] { "en" }, "en", "en", root + "/Project.asset", false);
            source = root + "/Resources/SimplyLocalize/Translations/en/Common.json";
            label = new GameObject("Greeting", typeof(RectTransform), typeof(Text)); label.GetComponent<Text>().text = "Hello";
            previousUndo = new HashSet<int>(Resources.FindObjectsOfTypeAll<LocalizationProjectUndo>().Select(r => r.GetInstanceID()));
            Undo.IncrementCurrentGroup(); undoGroup = Undo.GetCurrentGroup();
            host = ScriptableObject.CreateInstance<AutoHost>(); host.position = new Rect(100, 100, 900, 800); host.ShowUtility();
            view = new ProjectAutoLocalizeView(config); host.rootVisualElement.Add(view);
        }
        [TearDown] public void Cleanup()
        {
            view?.Dispose(); if (host != null) host.Close();
            if (previousUndo != null) foreach (var record in Resources.FindObjectsOfTypeAll<LocalizationProjectUndo>().Where(r => !previousUndo.Contains(r.GetInstanceID()))) record.Dispose();
            if (undoGroup > 0) Undo.RevertAllDownToGroup(undoGroup);
            if (label != null) Object.DestroyImmediate(label); AssetDatabase.DeleteAsset(root);
        }
        private T Field<T>(string name) where T : VisualElement => host.rootVisualElement.Q<T>(name);
        private void Click(string name) { var button = Field<UnityEngine.UIElements.Button>(name); using var submit = NavigationSubmitEvent.GetPooled(); submit.target = button; button.SendEvent(submit); }
        [Test] public void ScanPreviewAndApplyButtonsBindByIdentityAndUndoTogether()
        {
            byte[] before = File.ReadAllBytes(source); Assert.That(view.Scan(new[] { label }), Is.True);
            Assert.That(Field<ListView>("autoResults").virtualizationMethod, Is.EqualTo(CollectionVirtualizationMethod.FixedHeight));
            Click("autoPreview"); Assert.That(view.Preview, Is.Not.Null); Assert.That(Field<VisualElement>("autoSettings").enabledSelf, Is.False);
            Assert.That(label.GetComponent<LocalizedText>(), Is.Null); Assert.That(File.ReadAllBytes(source), Is.EqualTo(before));
            Click("autoApply"); Assert.That(label.GetComponent<LocalizedText>().Entry.IsSet, Is.True); Assert.That(view.HasPendingChanges, Is.False);
            Undo.PerformUndo(); Assert.That(label.GetComponent<LocalizedText>(), Is.Null); Assert.That(File.ReadAllBytes(source), Is.EqualTo(before));
        }
        [Test] public void CancelKeepsScanButCreatesNoFilesAndRescanFailureCannotApplyOldPreview()
        {
            view.Scan(new[] { label }); view.PreviewChanges(); Click("autoCancel");
            Assert.That(view.Preview, Is.Null); Assert.That(view.Candidates.Count, Is.EqualTo(1)); Assert.That(Field<VisualElement>("autoSettings").enabledSelf, Is.True);
            Assert.That(config.Sets[0].Entries, Is.Empty); Assert.That(label.GetComponent<LocalizedText>(), Is.Null);
            view.PreviewChanges(); Assert.That(view.Scan(null), Is.False); Assert.That(view.Preview, Is.Null); Assert.That(view.ApplyPreview(), Is.False);
        }
        [Test] public void ChangedScanSettingsInvalidateKeysAndSelectedRows()
        {
            view.Scan(new[] { label }); Field<TextField>("autoPrefix").value = "Menu";
            Assert.That(view.Candidates, Is.Empty); Assert.That(Field<UnityEngine.UIElements.Button>("autoPreview").enabledSelf, Is.False);
            view.Scan(new[] { label }); Assert.That(view.Candidates[0].Key, Does.StartWith("Menu/"));
        }
        [Test] public void StaleScenePreviewShowsErrorAndDoesNotSaveTranslations()
        {
            view.Scan(new[] { label }); view.PreviewChanges(); label.GetComponent<Text>().text = "External edit";
            Assert.That(view.ApplyPreview(), Is.False); Assert.That(Field<UnityEngine.UIElements.Button>("autoApply").enabledSelf, Is.False);
            Assert.That(Field<HelpBox>("autoStatus").text, Does.Contain("Scan again")); Assert.That(label.GetComponent<LocalizedText>(), Is.Null); Assert.That(config.Sets[0].Entries, Is.Empty);
        }
        [Test] public void SourceSelectionResolvesAmbiguityWithoutDiscardingEditedKeys()
        {
            string second = root + "/Resources/Second.json";
            using (var edit = new LocalizationProjectEdit(config, config.Sets[0].Id)) { edit.CreateTextSource(config.Sets[0].Id, "en", second); edit.Commit(); }
            view.Dispose(); host.rootVisualElement.Clear(); view = new ProjectAutoLocalizeView(config); host.rootVisualElement.Add(view);
            view.Scan(new[] { label }); view.Candidates[0].Key = "Chosen"; Assert.That(view.PreviewChanges(), Is.False);
            Field<DropdownField>("autoDestination").value = "Second"; Assert.That(view.Candidates.Single().Key, Is.EqualTo("Chosen")); Assert.That(view.PreviewChanges(), Is.True);
            view.ApplyPreview(); Assert.That(File.ReadAllText(second), Does.Contain("Chosen")); Assert.That(File.ReadAllText(source), Does.Not.Contain("Chosen"));
        }
        [Test] public void SharedSourceConnectionsAreVisibleBeforeSaving()
        {
            var other = config.AddSet("Dialogue"); other.SetSources("en", config.Sets[0].Sources[0].Sources.ToArray()); EditorUtility.SetDirty(config); AssetDatabase.SaveAssetIfDirty(config);
            view.Scan(new[] { label }); Assert.That(view.PreviewChanges(), Is.True);
            Assert.That(Field<HelpBox>("autoWarnings").text, Does.Contain("Common / en").And.Contain("Dialogue / en")); Assert.That(config.Sets[0].Entries, Is.Empty);
        }
        private sealed class AutoHost : EditorWindow { }
    }
}
