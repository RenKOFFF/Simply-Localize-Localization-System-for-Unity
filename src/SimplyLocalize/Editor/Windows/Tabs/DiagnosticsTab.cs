using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace SimplyLocalize.Editor.Windows.Tabs
{
    public sealed class DiagnosticsTab : IEditorTab
    {
        private const string RecordingKey = "SimplyLocalize.Diagnostics.RecordHistory";
        private LocalizationResourceSnapshot snapshot;
        private TextField filter;
        private Label summary, details;
        private ListView resources, history;
        private VisualElement sets;
        private Toggle recording;
        private IVisualElementScheduledItem refresh;
        [InitializeOnLoadMethod] private static void RestoreRecording() => LocalizationResourceDiagnostics.RecordingEnabled = SessionState.GetBool(RecordingKey, false);

        public void Build(VisualElement container)
        {
            var root = new ScrollView { name = "resourceDiagnostics" };
            root.style.flexGrow = 1; root.style.paddingLeft = root.style.paddingRight = 12;
            root.style.paddingTop = root.style.paddingBottom = 10; container.Add(root);
            var title = new Label("Resources") { tooltip = "Live LocalizationContext instances in this Editor, across all configs." };
            title.style.fontSize = 16; title.style.unityFontStyleAndWeight = FontStyle.Bold; root.Add(title);
            root.Add(new HelpBox("Tracks LocalizationContext resources across all configs. Legacy managers and external references are not tracked. Closing Borrowed references does not unload native assets. Counts are not process RAM.", HelpBoxMessageType.Info));
            var toolbar = new VisualElement(); toolbar.style.flexDirection = FlexDirection.Row; toolbar.style.flexWrap = Wrap.Wrap; root.Add(toolbar);
            toolbar.Add(new Button(Refresh) { name = "refreshResources", text = "Refresh" });
            var auto = new Toggle("Auto refresh (1s)") { name = "autoRefreshResources" }; toolbar.Add(auto);
            recording = new Toggle("Record history") { name = "recordResourceHistory", value = LocalizationResourceDiagnostics.RecordingEnabled }; toolbar.Add(recording);
            recording.RegisterValueChangedCallback(e => { LocalizationResourceDiagnostics.RecordingEnabled = e.newValue; SessionState.SetBool(RecordingKey, e.newValue); Refresh(); });
            toolbar.Add(new Button(() => { LocalizationResourceDiagnostics.ClearHistory(); Refresh(); }) { name = "clearResourceHistory", text = "Clear history" });
            summary = new Label { name = "resourceSummary" }; summary.style.marginTop = summary.style.marginBottom = 8; root.Add(summary);
            var contextSection = new Foldout { text = "Contexts / prepared sets", value = true }; root.Add(contextSection);
            sets = new VisualElement { name = "diagnosticSets" }; contextSection.Add(sets);
            filter = new TextField("Find resource") { name = "resourceFilter" }; filter.RegisterValueChangedCallback(_ => RenderResources()); root.Add(filter);
            resources = new ListView { name = "diagnosticResourceList", fixedItemHeight = 26, selectionType = SelectionType.Single };
            resources.style.height = 210;
            resources.makeItem = () => new Label();
            resources.bindItem = (element, index) => {
                var item = (LocalizationResourceInfo)resources.itemsSource[index]; var label = (Label)element;
                label.text = $"{item.Name}  ·  {item.Type}  ·  {item.ReferenceCount} retained  ·  #{item.InstanceId}";
                label.tooltip = string.Join("\n", item.Owners.Select(DescribeOwner));
            };
            resources.selectionChanged += selection => { var item = selection.OfType<LocalizationResourceInfo>().FirstOrDefault(); details.text = item == null ? "Select a resource to see its owners." : $"{item.Name} · #{item.InstanceId}\n" + (item.UnloadWhenUnused ? "Native unload after the last tracked owner.\n" : "Borrowed: native unload is not managed.\n") + string.Join("\n", item.Owners.Select(DescribeOwner)); };
            root.Add(resources);
            details = new Label("Select a resource to see its owners.") { name = "resourceDetails" }; details.style.whiteSpace = WhiteSpace.Normal; details.style.marginBottom = 12; root.Add(details);
            var historySection = new Foldout { text = "Recent history (up to 256 events)", value = true }; root.Add(historySection);
            history = new ListView { name = "resourceHistory", fixedItemHeight = 25, selectionType = SelectionType.None }; history.style.height = 150;
            history.makeItem = () => new Label();
            history.bindItem = (element, index) => { var item = (LocalizationResourceEvent)history.itemsSource[index]; var label = (Label)element; label.text = $"{item.Utc.ToLocalTime():HH:mm:ss}  {item.Kind}  ·  {item.ContextName} ({item.Language})  ·  {item.ResourceName}"; label.tooltip = $"Context #{item.ContextId}, set #{item.SetId}, resource #{item.InstanceId}\n{item.Detail}"; };
            historySection.Add(history);
            root.Add(new HelpBox("Unloaded means Resources.UnloadAsset completed. Released means a set closed. History starts when recording is enabled; older events can be dropped. No global resource scans or forced unloads are performed.", HelpBoxMessageType.None));
            refresh = root.schedule.Execute(Refresh).Every(1000); refresh.Pause();
            auto.RegisterValueChangedCallback(e => { if (e.newValue) refresh.Resume(); else refresh.Pause(); });
            root.RegisterCallback<DetachFromPanelEvent>(_ => refresh.Pause());
            root.RegisterCallback<AttachToPanelEvent>(_ => { if (auto.value) refresh.Resume(); });
            Refresh();
        }
        private static string DescribeOwner(LocalizationResourceOwnerInfo owner) =>
            $"{owner.ContextName} · context #{owner.ContextId}, set #{owner.SetId} · {owner.Ownership} · {owner.Stage}\nRequested: {owner.RequestedLanguage} → source: {owner.SourceLanguage ?? "pending"} · via {owner.Address ?? "pending"}";
        private void Refresh()
        {
            snapshot = LocalizationResourceDiagnostics.Capture(); recording.SetValueWithoutNotify(snapshot.RecordingEnabled);
            summary.text = $"{snapshot.Sets.Select(s => s.Owner.ContextId).Distinct().Count()} contexts · {snapshot.Resources.Count} tracked objects · {snapshot.History.Count} events · {snapshot.DroppedEvents} older events dropped";
            sets.Clear();
            if (snapshot.Sets.Count == 0) sets.Add(new Label("No live prepared sets. Enter Play Mode and prepare a LocalizationContext."));
            foreach (var set in snapshot.Sets)
            {
                var label = new Label($"{set.Owner.ContextName} · {set.Owner.RequestedLanguage} · {set.Owner.Stage} · {set.ResourceCount} objects · {set.PendingAddresses.Count} pending requests · prepare {set.PreparationMilliseconds:F1} ms / close {set.ReleaseMilliseconds:F1} ms");
                label.style.whiteSpace = WhiteSpace.Normal; label.tooltip = string.Join("\n", set.PendingAddresses); sets.Add(label);
            }
            RenderResources(); history.itemsSource = snapshot.History.Reverse().ToList(); history.Rebuild();
        }
        private void RenderResources()
        {
            if (snapshot == null || resources == null) return;
            int? selectedId = (resources.selectedItem as LocalizationResourceInfo)?.InstanceId;
            string query = filter.value ?? "";
            var rows = snapshot.Resources.Where(r => (r.Name + " " + r.Type + " " + string.Join(" ", r.Owners.Select(o => o.ContextName + " " + o.Address + " " + o.SourceLanguage))).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            resources.itemsSource = rows;
            resources.ClearSelection(); resources.Rebuild(); details.text = "Select a resource to see its owners.";
            int selectedIndex = rows.FindIndex(r => r.InstanceId == selectedId);
            if (selectedIndex >= 0) resources.SetSelection(selectedIndex);
        }
    }
}
