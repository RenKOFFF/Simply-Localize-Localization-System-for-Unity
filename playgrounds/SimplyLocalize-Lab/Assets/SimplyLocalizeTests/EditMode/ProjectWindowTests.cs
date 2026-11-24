using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.TestTools;
using SimplyLocalize.Editor;
using SimplyLocalize.Editor.Data;
using SimplyLocalize.Editor.Windows;
using UnityEditor;
using UnityEngine;
using Text = UnityEngine.UI.Text;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace SimplyLocalize.Tests
{
    public class ProjectWindowTests
    {
        private string root, entry, voice, source;
        private LocalizationProjectConfig config;
        private LocalizationEditorWindow window;
        private GameObject label;
        private HashSet<int> previousUndo;
        private ProjectNotesTestScope notes;
        private string[] selection;
        private static readonly string[] SelectionKeys = { "SimplyLocalize.ProjectEditor.Config", "SimplyLocalize.ProjectEditor.Channel", "SimplyLocalize.ProjectEditor.Set" };
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        [SetUp] public void Setup()
        {
            selection = SelectionKeys.Select(k => SessionState.GetString(k, "")).ToArray();
            root = "Assets/SimplyLocalizeWindow_" + Guid.NewGuid().ToString("N"); Directory.CreateDirectory(root); AssetDatabase.Refresh();
            config = LocalizationProjectAuthoring.Create(new[] { "en", "ru" }, "en", "en", root + "/Project.asset", false);
            notes = new ProjectNotesTestScope(config); entry = config.Sets[0].AddEntry("hello").Id;
            var channel = config.AddChannel("Voice", new[] { "en", "ru" }); channel.Mode = LocalizationChannelMode.Independent; channel.IncludeSet(config.Sets[0].Id, true); voice = channel.Id;
            SaveConfig(); source = root + "/Resources/SimplyLocalize/Translations/en/Common.json";
            File.WriteAllText(source, "{\"translations\":{\"hello\":\"Hello\"}}");
            previousUndo = Resources.FindObjectsOfTypeAll<LocalizationProjectUndo>().Select(r => r.GetInstanceID()).ToHashSet();
            window = ScriptableObject.CreateInstance<LocalizationEditorWindow>(); window.ShowUtility(); window.TrySelectConfig(config);
            Answer(1);
        }
        [TearDown] public void Cleanup()
        {
            if (window != null) { window.DiscardChanges(); window.Close(); }
            if (label != null) Object.DestroyImmediate(label);
            foreach (var record in Resources.FindObjectsOfTypeAll<LocalizationProjectUndo>().Where(r => !previousUndo.Contains(r.GetInstanceID()))) record.Dispose();
            notes?.Dispose(); AssetDatabase.DeleteAsset(root);
            for (int i = 0; i < SelectionKeys.Length; i++) SessionState.SetString(SelectionKeys[i], selection[i]);
            EditorProjectSelection.Invalidate();
        }
        private void SaveConfig() { EditorUtility.SetDirty(config); AssetDatabase.SaveAssetIfDirty(config); }
        private void Answer(int value) => typeof(LocalizationEditorWindow).GetField("ConfirmNavigation", Private).SetValue(window, (Func<string, int>)(_ => value));
        private void Invoke(string method) => typeof(LocalizationEditorWindow).GetMethod(method, Private).Invoke(window, null);
        private T View<T>() where T : VisualElement => window.rootVisualElement.Q<T>();
        private T Field<T>(string name) where T : VisualElement => window.rootVisualElement.Q<T>(name);
        private void Draft()
        {
            window.ShowTab(0); View<ProjectTranslationsView>().SelectEntry(entry); Field<TextField>("translation_en").value = "Draft";
            Assert.That(window.hasUnsavedChanges, Is.True);
        }
        private void RebuildAfterReload()
        {
            // Integration of lifecycle hooks. The separate probe verifies actual Unity domain serialization.
            Invoke("BeforeReload"); Invoke("OnDisable"); Invoke("OnEnable");
        }
        private void Click(string name)
        {
            var button = Field<Button>(name); Assert.That(button, Is.Not.Null, name);
            using var submit = NavigationSubmitEvent.GetPooled(); submit.target = button; button.SendEvent(submit);
        }
        [UnityTest] public IEnumerator ShortAndNormalWindowsKeepToolsControlsReadableAndReachable()
        {
            foreach (var size in new[] { new Vector2(700, 400), new Vector2(1100, 780) })
            foreach (int tab in new[] { 4, 5, 6 })
            {
                window.position = new Rect(40, 40, size.x, size.y); Assert.That(window.ShowTab(tab), Is.True);
                for (int frame = 0; frame < 4; frame++) yield return null;
                var page = tab == 4 ? (ScrollView)View<ProjectCoverageView>() : tab == 5 ? View<ProjectAutoLocalizeView>() : View<ProjectToolsView>();
                Assert.That(page.contentViewport.worldBound.height, Is.GreaterThan(100));
                if (tab == 4)
                {
                    var status = Field<HelpBox>("coverageStatus");
                    Assert.That(status.worldBound.height, Is.GreaterThanOrEqualTo(24));
                    Assert.That(Field<VisualElement>("coverageRows").worldBound.yMin, Is.GreaterThanOrEqualTo(status.worldBound.yMax - 1));
                }
                var buttons = tab == 4 ? new[] { "refreshCoverage" } : tab == 5 ? new[] { "autoScan", "autoPreview", "autoApply", "autoCancel" }
                    : new[] { "csvExport", "csvImport", "csvApply", "csvCancel" };
                foreach (string name in buttons)
                {
                    var button = Field<Button>(name); Assert.That(button, Is.Not.Null, name);
                    Assert.That(button.worldBound.height, Is.GreaterThanOrEqualTo(16), name + " at " + size);
                    page.ScrollTo(button); for (int frame = 0; frame < 3; frame++) yield return null;
                    Assert.That(button.worldBound.yMin, Is.GreaterThanOrEqualTo(page.contentViewport.worldBound.yMin - 1), name);
                    Assert.That(button.worldBound.yMax, Is.LessThanOrEqualTo(page.contentViewport.worldBound.yMax + 1), name);
                }
            }
            Assert.That(window.HasPendingChanges, Is.False);
        }

        [TestCase("tab")][TestCase("context")][TestCase("config")][TestCase("refresh")]
        public void CancelNavigationPreservesInputAndContext(string action)
        {
            Draft(); string before = File.ReadAllText(source);
            if (action == "tab") Assert.That(window.ShowTab(2), Is.False);
            if (action == "context") Assert.That(window.TrySelectContext(voice), Is.False);
            if (action == "config") Assert.That(window.TrySelectConfig(null), Is.False);
            if (action == "refresh") window.FullRefresh();
            Assert.That(window.ProjectConfig, Is.SameAs(config)); Assert.That(window.ActiveTabIndex, Is.Zero);
            Assert.That(window.Context.ChannelId, Is.EqualTo(config.MainChannelId));
            Assert.That(Field<TextField>("translation_en").value, Is.EqualTo("Draft")); Assert.That(File.ReadAllText(source), Is.EqualTo(before));
        }
        [Test] public void SaveNavigationCommitsBeforeSwitchingChannel()
        {
            Draft(); Answer(0); Assert.That(window.TrySelectContext(voice), Is.True);
            Assert.That(File.ReadAllText(source), Does.Contain("Draft")); Assert.That(window.Context.ChannelId, Is.EqualTo(voice));
            Assert.That(window.hasUnsavedChanges, Is.False); Assert.That(EditorProjectSelection.Current.ChannelId, Is.EqualTo(voice));
        }
        [Test] public void DiscardNavigationDoesNotWriteAndCloseFlagIsCleared()
        {
            Draft(); byte[] original = File.ReadAllBytes(source); Answer(2); Assert.That(window.ShowTab(2), Is.True);
            Assert.That(File.ReadAllBytes(source), Is.EqualTo(original)); Assert.That(window.hasUnsavedChanges, Is.False);
        }
        [Test] public void FailedSaveBlocksNavigationAndNativeCloseSaveFlag()
        {
            Draft(); File.WriteAllText(source, "{\"translations\":{\"hello\":\"External\"}}"); Answer(0);
            Assert.That(window.ShowTab(2), Is.False); window.SaveChanges(); Assert.That(window.hasUnsavedChanges, Is.True);
            Assert.That(Field<TextField>("translation_en").value, Is.EqualTo("Draft")); Assert.That(File.ReadAllText(source), Does.Contain("External"));
        }
        [Test] public void ReloadRestoresTextAndDoesNotOverwriteExternalEdit()
        {
            Draft(); File.WriteAllText(source, "{\"translations\":{\"hello\":\"External\"}}"); RebuildAfterReload();
            Assert.That(Field<TextField>("translation_en").value, Is.EqualTo("Draft")); Assert.That(window.hasUnsavedChanges, Is.True);
            Assert.That(window.SavePendingChanges(), Is.False); Assert.That(File.ReadAllText(source), Does.Contain("External"));
        }
        [Test] public void InvalidConfigExposesRecoveredInputAndDiscardRemovesRecovery()
        {
            Draft(); Invoke("BeforeReload"); Invoke("OnDisable"); config.MainChannel.DefaultLanguage = "pl"; Invoke("OnEnable");
            Assert.That(Field<TextField>("recovered_en").value, Is.EqualTo("Draft")); Assert.That(window.hasUnsavedChanges, Is.True);
            window.DiscardChanges(); Assert.That(Field<VisualElement>("draftRecovery"), Is.Null); Assert.That(window.hasUnsavedChanges, Is.False);
            config.MainChannel.DefaultLanguage = "en";
        }
        [Test] public void ReloadCsvRequiresReviewAndFailedRebuildKeepsOriginalInput()
        {
            window.ShowTab(6); const string csv = "Key,en\nhello,Imported\n";
            Assert.That(View<ProjectToolsView>().PreviewCsv(csv), Is.True); Assert.That(window.hasUnsavedChanges, Is.True);
            RebuildAfterReload(); Assert.That(View<ProjectToolsView>().Preview, Is.Null); Assert.That(window.SavePendingChanges(), Is.False);
            string invalidFile = root + "/Invalid.csv"; File.WriteAllBytes(invalidFile, new byte[] { 0xef, 0xbb, 0xbf, 0xff });
            Assert.That(View<ProjectToolsView>().PreviewFile(invalidFile), Is.False);
            Assert.That(Field<TextField>("csvRecoveredInput").value, Is.EqualTo(csv)); Assert.That(window.HasPendingChanges, Is.True);
            File.WriteAllText(source, "malformed JSON"); Click("csvRebuild");
            Assert.That(Field<TextField>("csvRecoveredInput").value, Is.EqualTo(csv)); Assert.That(window.HasPendingChanges, Is.True);
            File.WriteAllText(source, "{\"translations\":{\"hello\":\"Hello\"}}"); EditorProjectSelection.Invalidate(); Click("csvRebuild");
            Assert.That(View<ProjectToolsView>().Preview, Is.Not.Null); Assert.That(window.SavePendingChanges(), Is.True);
            Assert.That(File.ReadAllText(source), Does.Contain("Imported"));
        }
        [TestCase(false)][TestCase(true)] public void RemovedCsvRouteDoesNotRedirectImportToAnotherSet(bool removeChannel)
        {
            if (removeChannel) window.TrySelectContext(voice);
            string original = EditorJsonUtility.ToJson(config);
            window.ShowTab(6); Assert.That(View<ProjectToolsView>().PreviewCsv("Key,en\nhello,Imported\n"), Is.True);
            Invoke("BeforeReload"); Invoke("OnDisable");
            if (removeChannel) config.RemoveChannel(voice);
            else { var other = config.AddSet("Other"); config.MainChannel.RemoveSet(config.Sets[0].Id); config.MainChannel.IncludeSet(other.Id, true); }
            Invoke("OnEnable"); Assert.That(Field<VisualElement>("auxiliaryRecovery"), Is.Not.Null); Assert.That(window.SavePendingChanges(), Is.False);
            Assert.That(Field<TextField>("recoveredCsv").value, Does.Contain("Imported")); Assert.That(File.ReadAllText(source), Does.Not.Contain("Imported"));
            EditorJsonUtility.FromJsonOverwrite(original, config); Click("retryRecoveredDraft");
            Assert.That(View<ProjectToolsView>(), Is.Not.Null); Assert.That(Field<TextField>("csvRecoveredInput").value, Does.Contain("Imported"));
            Assert.That(window.Context.SetId, Is.EqualTo(config.Sets[0].Id));
        }
        [TestCase(false)][TestCase(true)] public void ReloadScanKeepsChoicesAndChecksOriginalTarget(bool changeTarget)
        {
            label = new GameObject("Label", typeof(RectTransform), typeof(Text)); label.GetComponent<Text>().text = "Scene greeting";
            window.ShowTab(5); Assert.That(View<ProjectAutoLocalizeView>().Scan(new[] { label }), Is.True);
            View<ProjectAutoLocalizeView>().Candidates[0].Key = "scene/greeting"; Assert.That(View<ProjectAutoLocalizeView>().PreviewChanges(), Is.True);
            if (changeTarget) label.GetComponent<Text>().text = "Changed externally";
            RebuildAfterReload(); var restored = View<ProjectAutoLocalizeView>();
            Assert.That(restored.Candidates.Single().Key, Is.EqualTo("scene/greeting")); Assert.That(restored.Candidates.Single().Target, Is.SameAs(label));
            Assert.That(restored.Preview, Is.Null); Assert.That(window.SavePendingChanges(), Is.False);
            Assert.That(restored.PreviewChanges(), Is.EqualTo(!changeTarget)); Assert.That(window.HasPendingChanges, Is.True);
        }
        [Test] public void SelectingEditorChannelDoesNotChangeDefinitionsOrInitializeRuntime()
        {
            string original = EditorJsonUtility.ToJson(config); var runtime = Localization.Runtime;
            Assert.That(window.TrySelectContext(voice), Is.True); window.ShowTab(2);
            Assert.That(EditorJsonUtility.ToJson(config), Is.EqualTo(original)); Assert.That(Localization.Runtime, Is.SameAs(runtime));
            Assert.That(Field<DropdownField>("channelSelector").value, Is.EqualTo(voice));
        }
    }
}
