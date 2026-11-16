using System.Reflection;
using NUnit.Framework;
using SimplyLocalize.Editor.Windows;
using SimplyLocalize.Editor.Windows.Tabs;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
namespace SimplyLocalize.Tests
{
    public class ResourceDiagnosticsWindowTests
    {
        [TearDown] public void Cleanup() { LocalizationResourceDiagnostics.RecordingEnabled = false; LocalizationResourceDiagnostics.ClearHistory(); SessionState.SetBool("SimplyLocalize.Diagnostics.RecordHistory", false); }
        [Test] public void Diagnostics_IsAvailableWithoutConfigAndDoesNotInitializeLocalization()
        {
            var before = LocalizationResourceDiagnostics.Capture();
            var window = ScriptableObject.CreateInstance<LocalizationEditorWindow>();
            try {
                window.ShowUtility(); typeof(LocalizationEditorWindow).GetMethod("AssignConfig", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(window, new object[] { null });
                var tab = window.rootVisualElement.Q<Button>("tabDiagnostics"); Assert.That(tab.enabledSelf, Is.True);
                using (var submit = NavigationSubmitEvent.GetPooled()) { submit.target = tab; tab.SendEvent(submit); }
                Assert.That(window.rootVisualElement.Q("resourceDiagnostics"), Is.Not.Null);
                Assert.That(window.rootVisualElement.Q<Toggle>("autoRefreshResources").value, Is.False);
                Assert.That(LocalizationResourceDiagnostics.Capture().Resources.Count, Is.EqualTo(before.Resources.Count));
            } finally { window.Close(); }
        }
        [Test] public void RecordingToggle_ChangesOnlyRecordingAndCanBeDisabled()
        {
            var window = ScriptableObject.CreateInstance<EditorWindow>(); window.ShowUtility();
            try {
            var root = window.rootVisualElement; new DiagnosticsTab().Build(root);
            var record = root.Q<Toggle>("recordResourceHistory"); record.value = true; Assert.That(LocalizationResourceDiagnostics.RecordingEnabled, Is.True);
            record.value = false; Assert.That(LocalizationResourceDiagnostics.RecordingEnabled, Is.False);
            Assert.That(root.Q<Label>("resourceSummary").text, Does.Contain("tracked objects"));
            } finally { window.Close(); }
        }
        [Test] public void Refresh_KeepsSelectedResourceByIdAndClearsItWhenRemoved()
        {
            var window = ScriptableObject.CreateInstance<EditorWindow>(); window.ShowUtility();
            try {
                var root = window.rootVisualElement; var tab = new DiagnosticsTab(); tab.Build(root);
                LocalizationResourceInfo Row(int id, string name) => (LocalizationResourceInfo)System.Activator.CreateInstance(typeof(LocalizationResourceInfo), BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { id, name, "AudioClip", 1, true, System.Array.Empty<LocalizationResourceOwnerInfo>() }, null);
                void Show(params LocalizationResourceInfo[] rows)
                {
                    var snapshot = System.Activator.CreateInstance(typeof(LocalizationResourceSnapshot), BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { System.Array.Empty<LocalizationResourceSetInfo>(), rows, System.Array.Empty<LocalizationResourceEvent>(), 0 }, null);
                    typeof(DiagnosticsTab).GetField("snapshot", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(tab, snapshot);
                    typeof(DiagnosticsTab).GetMethod("RenderResources", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(tab, null);
                }
                Show(Row(1, "Intro"), Row(2, "Radio"));
                var list = root.Q<ListView>("diagnosticResourceList"); list.SetSelection(1);
                Show(Row(2, "Radio updated"), Row(1, "Intro"));
                Assert.That(((LocalizationResourceInfo)list.selectedItem).InstanceId, Is.EqualTo(2));
                Assert.That(root.Q<Label>("resourceDetails").text, Does.Contain("Radio updated"));
                Show(Row(1, "Intro"));
                Assert.That(list.selectedItem, Is.Null);
                Assert.That(root.Q<Label>("resourceDetails").text, Does.Contain("Select a resource"));
            } finally { window.Close(); }
        }
        [UnityEngine.TestTools.UnityTest] public System.Collections.IEnumerator Resize_KeepsBothPanesReadableInNarrowAndWideWindows()
        {
            var window = ScriptableObject.CreateInstance<EditorWindow>();
            try {
                window.ShowUtility(); window.position = new Rect(100, 100, 700, 850);
                var tab = new DiagnosticsTab(); tab.Build(window.rootVisualElement);
                var item = System.Activator.CreateInstance(typeof(LocalizationResourceInfo), BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { 1, "Radio", "AudioClip", 1, true, System.Array.Empty<LocalizationResourceOwnerInfo>() }, null);
                var snapshot = System.Activator.CreateInstance(typeof(LocalizationResourceSnapshot), BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { System.Array.Empty<LocalizationResourceSetInfo>(), new[] { (LocalizationResourceInfo)item }, System.Array.Empty<LocalizationResourceEvent>(), 0 }, null);
                typeof(DiagnosticsTab).GetField("snapshot", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(tab, snapshot);
                typeof(DiagnosticsTab).GetMethod("RenderResources", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(tab, null);
                window.rootVisualElement.Q<ListView>("diagnosticResourceList").SetSelection(0);
                for (int i = 0; i < 3; i++) yield return null;
                var browser = window.rootVisualElement.Q("resourceBrowser");
                var inspector = window.rootVisualElement.Q("resourceInspector");
                Assert.That(browser.worldBound.height, Is.GreaterThan(240));
                Assert.That(inspector.worldBound.height, Is.GreaterThan(240));
                Assert.That(inspector.worldBound.yMin, Is.GreaterThanOrEqualTo(browser.worldBound.yMax));
                window.position = new Rect(100, 100, 1100, 850);
                for (int i = 0; i < 3; i++) yield return null;
                Assert.That(browser.worldBound.height, Is.GreaterThan(240));
                Assert.That(inspector.worldBound.height, Is.GreaterThan(240));
                Assert.That(inspector.worldBound.xMin, Is.GreaterThanOrEqualTo(browser.worldBound.xMax));
            } finally { window.Close(); }
        }
    }
}
