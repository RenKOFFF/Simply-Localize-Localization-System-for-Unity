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
        private Label summary, details, contextCount, resourceCount, eventCount, resourceEmpty, historyStatus, eventDetails;
        private ListView resources, history;
        private ScrollView ownerScroll;
        private VisualElement sets, owners, resourcePane, detailPane;
        private Toggle recording;
        private Foldout historySection;
        private IVisualElementScheduledItem refresh;
        private static bool Dark => EditorGUIUtility.isProSkin;
        private static Color Surface => Dark ? new Color(.13f, .14f, .16f) : new Color(.94f, .95f, .96f);
        private static Color Muted => Dark ? new Color(.78f, .81f, .85f) : new Color(.30f, .34f, .39f);
        private static Color Line => Dark ? new Color(.30f, .32f, .35f) : new Color(.74f, .77f, .80f);
        [InitializeOnLoadMethod] private static void RestoreRecording() => LocalizationResourceDiagnostics.RecordingEnabled = SessionState.GetBool(RecordingKey, false);

        public void Build(VisualElement container)
        {
            var root = new ScrollView { name = "resourceDiagnostics" };
            root.AddToClassList(Dark ? "diagnostics-dark" : "diagnostics-light");
            var stylesheet = AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/com.renkoff.simply-localize/Editor/Windows/Tabs/DiagnosticsTab.uss");
            if (stylesheet != null) root.styleSheets.Add(stylesheet);
            root.style.backgroundColor = Dark ? new Color(.085f, .09f, .10f) : new Color(.85f, .87f, .89f);
            root.style.color = Dark ? new Color(.94f, .95f, .97f) : new Color(.10f, .12f, .15f);
            root.style.flexGrow = 1; Pad(root, 16); container.Add(root);
            var heading = Row(); heading.style.justifyContent = Justify.SpaceBetween; heading.style.flexWrap = Wrap.Wrap; root.Add(heading);
            var intro = new VisualElement(); heading.Add(intro);
            intro.Add(Text("Resource diagnostics", 20, true));
            intro.Add(Secondary("Live LocalizationContext resources · all configs"));
            var actions = Row(); actions.style.marginTop = 6; heading.Add(actions);
            var update = new Button(Refresh) { name = "refreshResources", text = "Refresh" }; update.style.height = 28; update.style.marginRight = 12; actions.Add(update);
            var auto = new Toggle("Auto refresh · 1s") { name = "autoRefreshResources" }; actions.Add(auto);

            var metrics = Row(); metrics.style.marginTop = 14; metrics.style.marginBottom = 12; root.Add(metrics);
            contextCount = Metric(metrics, "CONTEXTS", "contextCount");
            resourceCount = Metric(metrics, "TRACKED OBJECTS", "trackedResourceCount");
            eventCount = Metric(metrics, "RECORDED EVENTS", "recordedEventCount");
            summary = Secondary(""); summary.name = "resourceSummary"; root.Add(summary);

            var contextSection = new Foldout { text = "Contexts / prepared sets", value = true }; contextSection.style.marginTop = 12; root.Add(contextSection);
            sets = new VisualElement { name = "diagnosticSets" }; sets.style.marginTop = 6; contextSection.Add(sets);

            var workspace = Row(); workspace.name = "resourceWorkspace"; workspace.style.alignItems = Align.Stretch; workspace.style.marginTop = 14; root.Add(workspace);
            resourcePane = Card(); resourcePane.name = "resourceBrowser"; resourcePane.style.flexGrow = 1; resourcePane.style.flexBasis = 0; workspace.Add(resourcePane);
            resourcePane.Add(Text("Resources", 15, true));
            filter = new TextField("Search") { name = "resourceFilter", tooltip = "Name, type, context, source language or address" };
            filter.labelElement.style.minWidth = 48; filter.style.marginTop = 8; filter.style.marginBottom = 8;
            filter.RegisterValueChangedCallback(_ => RenderResources()); resourcePane.Add(filter);
            var columns = Row(); columns.Add(Cell("RESOURCE / TYPE", "resourceColumn", 0, true)); columns.Add(Cell("OWNERS", "ownersColumn", 65)); columns.style.marginBottom = 4; resourcePane.Add(columns);
            resources = new ListView { name = "diagnosticResourceList", fixedItemHeight = 48, selectionType = SelectionType.Single, showAlternatingRowBackgrounds = AlternatingRowBackground.ContentOnly };
            resources.style.height = 240;
            resources.makeItem = () => {
                var row = Row(); Pad(row, 6);
                var identity = new VisualElement(); identity.style.flexGrow = 1; identity.style.minWidth = 0; row.Add(identity);
                var name = Text("", 13, true); name.name = "resourceName"; Ellipsis(name); identity.Add(name);
                var type = Secondary(""); type.name = "resourceType"; Ellipsis(type); identity.Add(type);
                row.Add(Cell("", "ownerCount", 65)); return row;
            };
            resources.bindItem = (element, index) => {
                var item = (LocalizationResourceInfo)resources.itemsSource[index];
                element.Q<Label>("resourceName").text = item.Name;
                element.Q<Label>("resourceType").text = item.Type;
                element.Q<Label>("ownerCount").text = item.ReferenceCount.ToString();
                element.tooltip = item.Name + " · #" + item.InstanceId;
            };
            resources.selectionChanged += selection => ShowResource(selection.OfType<LocalizationResourceInfo>().FirstOrDefault()); resourcePane.Add(resources);
            resourceEmpty = Secondary(""); resourceEmpty.name = "resourceEmpty"; resourcePane.Add(resourceEmpty);

            detailPane = Card(); detailPane.name = "resourceInspector"; detailPane.style.flexGrow = 1; detailPane.style.flexBasis = 0; detailPane.style.marginLeft = 12; workspace.Add(detailPane);
            detailPane.Add(Secondary("SELECTED RESOURCE"));
            details = Text("Select a resource", 17, true); details.name = "resourceDetails"; details.style.whiteSpace = WhiteSpace.Normal; details.style.marginTop = 8; detailPane.Add(details);
            ownerScroll = new ScrollView(); ownerScroll.style.height = 260; detailPane.Add(ownerScroll);
            owners = new VisualElement { name = "resourceOwners" }; ownerScroll.Add(owners);
            root.RegisterCallback<GeometryChangedEvent>(e => {
                bool compact = e.newRect.width < 900;
                workspace.style.flexDirection = compact ? FlexDirection.Column : FlexDirection.Row;
                resourcePane.style.flexBasis = detailPane.style.flexBasis = compact ? new StyleLength(StyleKeyword.Auto) : new StyleLength(0f);
                resourcePane.style.flexGrow = detailPane.style.flexGrow = compact ? 0 : 1;
                resourcePane.style.flexShrink = detailPane.style.flexShrink = 0;
                detailPane.style.marginLeft = compact ? 0 : 12; detailPane.style.marginTop = compact ? 12 : 0;
            });

            historySection = new Foldout { text = "Event history", value = LocalizationResourceDiagnostics.RecordingEnabled }; historySection.style.marginTop = 16; root.Add(historySection);
            var historyCard = Card(); historyCard.style.marginTop = 6; historySection.Add(historyCard);
            var historyActions = Row(); historyActions.style.justifyContent = Justify.SpaceBetween; historyActions.style.flexWrap = Wrap.Wrap; historyCard.Add(historyActions);
            recording = new Toggle("Record history") { name = "recordResourceHistory", value = LocalizationResourceDiagnostics.RecordingEnabled }; historyActions.Add(recording);
            recording.RegisterValueChangedCallback(e => { LocalizationResourceDiagnostics.RecordingEnabled = e.newValue; SessionState.SetBool(RecordingKey, e.newValue); Refresh(); });
            historyActions.Add(new Button(() => { LocalizationResourceDiagnostics.ClearHistory(); Refresh(); }) { name = "clearResourceHistory", text = "Clear history" });
            historyStatus = Secondary(""); historyStatus.style.marginTop = historyStatus.style.marginBottom = 8; historyCard.Add(historyStatus);
            history = new ListView { name = "resourceHistory", fixedItemHeight = 36, selectionType = SelectionType.Single, showAlternatingRowBackgrounds = AlternatingRowBackground.ContentOnly }; history.style.height = 144;
            history.makeItem = () => {
                var row = Row(); Pad(row, 4); row.Add(Cell("", "eventTime", 68));
                var kind = Text("", 11, true); kind.name = "eventKind"; kind.style.width = 115; kind.style.flexShrink = 0; row.Add(kind);
                row.Add(Cell("", "eventSubject", 0, true)); return row;
            };
            history.bindItem = (element, index) => {
                var item = (LocalizationResourceEvent)history.itemsSource[index];
                element.Q<Label>("eventTime").text = item.Utc.ToLocalTime().ToString("HH:mm:ss");
                var kind = element.Q<Label>("eventKind"); kind.text = EventTitle(item.Kind); kind.style.color = StateColor(item.Kind.ToString());
                element.Q<Label>("eventSubject").text = string.IsNullOrEmpty(item.ResourceName) ? item.ContextName + " · " + item.Language : item.ResourceName + " · " + item.ContextName;
                element.tooltip = DescribeEvent(item);
            };
            history.selectionChanged += selection => { var item = selection.OfType<LocalizationResourceEvent>().FirstOrDefault(); eventDetails.text = item == null ? "Select an event for details." : DescribeEvent(item); };
            historyCard.Add(history); eventDetails = Secondary("Select an event for details."); eventDetails.name = "resourceEventDetails"; eventDetails.style.marginTop = 8; historyCard.Add(eventDetails);

            var help = new Foldout { text = "What these numbers mean", value = false }; help.style.marginTop = 14; root.Add(help);
            help.Add(Secondary("Only LocalizationContext resources are tracked. Legacy managers, other Players and external references are outside this view. Owners are prepared sets, including candidates during language changes."));
            help.Add(Secondary("Unloaded: Resources.UnloadAsset completed. Set closed / Borrowed released: ownership ended, not necessarily native residency. These counts are not process RAM."));
            help.Add(Secondary("History starts when recording is enabled and keeps the latest 256 events. No global resource scans or forced unloads are performed."));
            refresh = root.schedule.Execute(Refresh).Every(1000); refresh.Pause();
            auto.RegisterValueChangedCallback(e => { if (e.newValue) refresh.Resume(); else refresh.Pause(); });
            root.RegisterCallback<DetachFromPanelEvent>(_ => refresh.Pause());
            root.RegisterCallback<AttachToPanelEvent>(_ => { if (auto.value) refresh.Resume(); });
            Refresh();
        }

        private void ShowResource(LocalizationResourceInfo item)
        {
            owners.Clear();
            ownerScroll.style.height = item == null ? 80 : 260;
            details.text = item == null ? "Select a resource" : item.Name;
            if (item == null) { owners.Add(Secondary("Choose a row on the left to see who holds the object and where it was loaded from.")); return; }
            owners.Add(Secondary(item.Type + " · Instance #" + item.InstanceId));
            var policy = Text(item.UnloadWhenUnused ? "Unloads after the last owner" : "Borrowed · no native unload", 12, true);
            policy.style.marginTop = 10; policy.style.marginBottom = 8; owners.Add(policy);
            if (item.Owners.Count == 0) owners.Add(Secondary("Owner details are unavailable. A lost context still requires proper release; this view does not find external references."));
            foreach (var owner in item.Owners)
            {
                var block = new VisualElement(); block.style.borderTopWidth = 1; block.style.borderTopColor = Line; block.style.marginTop = 8; block.style.paddingTop = 10; owners.Add(block);
                var heading = Row(); heading.style.flexWrap = Wrap.Wrap; block.Add(heading);
                var name = Text(owner.ContextName, 13, true); name.style.whiteSpace = WhiteSpace.Normal; name.style.flexGrow = 1; heading.Add(name); heading.Add(Badge(owner.Stage.ToString()));
                block.Add(Secondary(owner.Ownership + " · context #" + owner.ContextId + " / set #" + owner.SetId));
                var locale = Text(owner.RequestedLanguage + " → " + (owner.SourceLanguage ?? "pending"), 13, true); locale.style.marginTop = 8; block.Add(locale);
                block.Add(Secondary("Requested language → loaded source"));
                var address = Secondary(owner.Address ?? "Waiting for a source address"); address.style.marginTop = 6; address.tooltip = owner.Address; block.Add(address);
            }
        }
        private void Refresh()
        {
            snapshot = LocalizationResourceDiagnostics.Capture(); recording.SetValueWithoutNotify(snapshot.RecordingEnabled);
            contextCount.text = snapshot.Sets.Select(s => s.Owner.ContextId).Distinct().Count().ToString();
            resourceCount.text = snapshot.Resources.Count.ToString(); eventCount.text = snapshot.History.Count.ToString();
            summary.text = "Current snapshot · tracked objects, not memory usage";
            sets.Clear();
            if (snapshot.Sets.Count == 0) sets.Add(Secondary("No prepared contexts. Enter Play Mode and prepare a LocalizationContext."));
            foreach (var set in snapshot.Sets)
            {
                var card = Card(); card.style.marginBottom = 6; sets.Add(card);
                var row = Row(); row.style.flexWrap = Wrap.Wrap; card.Add(row);
                var name = Text(set.Owner.ContextName, 13, true); name.style.flexGrow = 1; name.style.whiteSpace = WhiteSpace.Normal; row.Add(name);
                row.Add(Badge(set.Owner.Stage.ToString()));
                card.Add(Secondary($"{set.Owner.RequestedLanguage} · {set.ResourceCount} objects · {set.PendingAddresses.Count} pending · prepare {set.PreparationMilliseconds:F1} ms · close {set.ReleaseMilliseconds:F1} ms"));
                card.tooltip = $"Context #{set.Owner.ContextId} / set #{set.Owner.SetId} · {set.Owner.Ownership}\n" + string.Join("\n", set.PendingAddresses);
            }
            RenderResources();
            long? selected = (history.selectedItem as LocalizationResourceEvent)?.Sequence;
            var events = snapshot.History.Reverse().ToList(); history.itemsSource = events; history.ClearSelection(); history.Rebuild();
            eventDetails.text = "Select an event for details.";
            int index = events.FindIndex(e => e.Sequence == selected); if (index >= 0) history.SetSelection(index);
            historyStatus.text = (snapshot.RecordingEnabled ? "Recording" : "Recording paused") + $" · {events.Count} / 256 events · {snapshot.DroppedEvents} older events dropped";
            if (events.Count == 0) historyStatus.text += "\nEnable recording before the action you want to inspect.";
        }
        private void RenderResources()
        {
            if (snapshot == null || resources == null) return;
            int? selectedId = (resources.selectedItem as LocalizationResourceInfo)?.InstanceId;
            string query = filter.value ?? "";
            var rows = snapshot.Resources.Where(r => (r.Name + " " + r.Type + " " + string.Join(" ", r.Owners.Select(o => o.ContextName + " " + o.Address + " " + o.SourceLanguage))).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            resources.itemsSource = rows; resources.ClearSelection(); resources.Rebuild(); ShowResource(null);
            resources.style.display = rows.Count == 0 ? DisplayStyle.None : DisplayStyle.Flex;
            resourceEmpty.text = rows.Count == 0 ? (snapshot.Resources.Count == 0 ? "No tracked resources in this snapshot." : "No resources match your search.") : rows.Count + " objects · select a row to inspect";
            int index = rows.FindIndex(r => r.InstanceId == selectedId); if (index >= 0) resources.SetSelection(index);
        }
        private static string EventTitle(LocalizationResourceEventKind kind) => kind switch {
            LocalizationResourceEventKind.ReleaseRequested => "Release requested",
            LocalizationResourceEventKind.BorrowedReleased => "Borrowed released",
            LocalizationResourceEventKind.Released => "Set closed", _ => kind.ToString()
        };
        private static string DescribeEvent(LocalizationResourceEvent item) => $"{item.ContextName} · {item.Language} · context #{item.ContextId} / set #{item.SetId}\n{item.ResourceName} · resource #{item.InstanceId}\n{item.Detail}";
        private static VisualElement Row() { var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.alignItems = Align.Center; return row; }
        private static void Pad(VisualElement element, int size) { element.style.paddingLeft = element.style.paddingRight = element.style.paddingTop = element.style.paddingBottom = size; }
        private static Label Text(string value, int size, bool bold = false) { var label = new Label(value); label.style.fontSize = size; if (bold) label.style.unityFontStyleAndWeight = FontStyle.Bold; return label; }
        private static Label Secondary(string value) { var label = Text(value, 12); label.style.color = Muted; label.style.whiteSpace = WhiteSpace.Normal; return label; }
        private static void Ellipsis(Label label) { label.style.overflow = Overflow.Hidden; label.style.textOverflow = TextOverflow.Ellipsis; label.style.whiteSpace = WhiteSpace.NoWrap; }
        private static Label Cell(string text, string name, int width, bool grow = false) { var label = Secondary(text); label.name = name; Ellipsis(label); label.style.minWidth = 0; if (grow) label.style.flexGrow = 1; else { label.style.width = width; label.style.flexShrink = 0; } return label; }
        private static VisualElement Card() { var card = new VisualElement(); Pad(card, 12); card.style.backgroundColor = Surface; card.style.borderTopLeftRadius = card.style.borderTopRightRadius = card.style.borderBottomLeftRadius = card.style.borderBottomRightRadius = 5; card.style.minWidth = 0; return card; }
        private static Label Metric(VisualElement parent, string title, string name) { var card = Card(); card.style.flexGrow = 1; card.style.flexBasis = 0; card.style.marginRight = 6; parent.Add(card); var number = Text("0", 24, true); number.name = name; card.Add(number); card.Add(Secondary(title)); return number; }
        private static Label Badge(string state) { var label = Text(state == "PreparingAudio" ? "Preparing audio" : state == "WaitingForAudio" ? "Waiting for audio" : state, 11, true); label.style.color = StateColor(state); label.style.marginLeft = 8; return label; }
        private static Color StateColor(string state)
        {
            bool good = state == "Ready" || state == "Unloaded";
            bool bad = state == "Failed";
            if (Dark) return bad ? new Color(1f, .55f, .53f) : good ? new Color(.48f, .85f, .67f) : new Color(.57f, .77f, 1f);
            return bad ? new Color(.68f, .14f, .13f) : good ? new Color(.10f, .40f, .26f) : new Color(.13f, .33f, .61f);
        }
    }
}
