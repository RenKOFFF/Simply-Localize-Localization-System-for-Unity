using System;
using System.Collections.Generic;
using System.Linq;
using SimplyLocalize.Editor.Data;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace SimplyLocalize.Editor.Windows
{
    /// <summary>Scan and review text bindings before saving. The host owns the selected channel/set
    /// and must resolve pending edits on other pages before opening this view.</summary>
    public sealed class ProjectAutoLocalizeView : VisualElement, IDisposable
    {
        private enum ScanScope { ActiveScene, SelectedObjects }
        private readonly LocalizationProjectConfig config;
        private readonly string channelId, setId;
        private readonly Action changed;
        private readonly VisualElement settings;
        private readonly EnumField scope, prefabs, pattern;
        private readonly Toggle skipNumeric, skipLocalized;
        private readonly IntegerField minimum;
        private readonly TextField prefix, custom, detail;
        private readonly DropdownField destination;
        private readonly Label summary, previewSummary;
        private readonly HelpBox status, warnings;
        private readonly ListView rows;
        private readonly Button preview, apply;
        private List<AutoLocalizeCandidate> candidates = new();
        private AutoLocalizePlan plan;
        public IReadOnlyList<AutoLocalizeCandidate> Candidates => candidates.AsReadOnly();
        public AutoLocalizePlan Preview => plan;
        public bool HasPendingChanges => candidates.Count > 0;
        public event Action DraftChanged;
        internal ProjectAutoState CaptureState() => new() { Config = config, Channel = channelId, Set = setId, Initialized = true, SelectedObjects = (ScanScope)scope.value == ScanScope.SelectedObjects,
            Prefabs = (AutoLocalizePrefabMode)prefabs.value, Pattern = (AutoLocalizeKeyPattern)pattern.value, Custom = custom.value, Prefix = prefix.value,
            Minimum = minimum.value, SkipNumeric = skipNumeric.value, SkipLocalized = skipLocalized.value, Destination = destination.value,
            Candidates = candidates.Select(c => c.Capture()).ToList() };
        internal void RestoreState(ProjectAutoState state)
        {
            if (state?.Initialized != true) return;
            if (state.Config != config || state.Channel != channelId || state.Set != setId)
                throw new InvalidOperationException("Restore the original config, channel and content set before rebuilding this scan preview.");
            ClearScan(); scope.SetValueWithoutNotify(state.SelectedObjects ? ScanScope.SelectedObjects : ScanScope.ActiveScene);
            prefabs.SetValueWithoutNotify(state.Prefabs); pattern.SetValueWithoutNotify(state.Pattern); custom.SetValueWithoutNotify(state.Custom); prefix.SetValueWithoutNotify(state.Prefix);
            custom.style.display = state.Pattern == AutoLocalizeKeyPattern.Custom ? DisplayStyle.Flex : DisplayStyle.None;
            minimum.SetValueWithoutNotify(state.Minimum); skipNumeric.SetValueWithoutNotify(state.SkipNumeric); skipLocalized.SetValueWithoutNotify(state.SkipLocalized);
            if (destination.choices.Contains(state.Destination)) destination.SetValueWithoutNotify(state.Destination);
            candidates = state.Candidates.Select(c => new AutoLocalizeCandidate(c)).ToList(); rows.itemsSource = candidates; rows.Rebuild(); UpdateSummary();
            if (candidates.Count > 0) ShowError("Scan choices restored. Build and review a new preview before applying; changed objects will require a new scan.");
        }

        public ProjectAutoLocalizeView(LocalizationProjectConfig config, string channelId = null, string setId = null, Action changed = null)
        {
            this.config = config != null ? config : throw new ArgumentNullException(nameof(config));
            this.channelId = channelId ?? config.MainChannelId;
            var channel = config.Channels.Single(c => c.Id == this.channelId);
            this.setId = setId ?? channel.DefaultSetId; this.changed = changed;
            var set = config.Sets.Single(s => s.Id == this.setId);
            name = "projectAutoLocalize"; style.flexGrow = 1; style.paddingLeft = style.paddingRight = 16; style.paddingTop = 12;
            Add(LanguagePickerStyle.Heading("Auto-Localize"));
            Add(new Label(channel.Name + " / " + set.Name + " · New translations: " + channel.DefaultLanguage) { name = "autoContext" });
            Add(new Label("Scan text components, review keys, then save bindings. Existing keys keep their authored translations. Scene changes remain unsaved until you save the scene.")
                { style = { whiteSpace = WhiteSpace.Normal, marginTop = 6 } });
            settings = new VisualElement { name = "autoSettings" }; Add(settings);
            var scanning = new Foldout { text = "Scan settings", value = true }; settings.Add(scanning);
            scope = new EnumField("Scan", ScanScope.ActiveScene) { name = "autoScope" }; scanning.Add(scope);
            prefabs = new EnumField("Prefabs", AutoLocalizePrefabMode.Skip) { name = "autoPrefabs", tooltip = "Skip excludes prefab instances. Instance Overrides changes only scene instances. Prefab Assets edits source assets using their original text." }; scanning.Add(prefabs);
            skipLocalized = new Toggle("Skip already localized") { name = "autoSkipLocalized", value = true }; scanning.Add(skipLocalized);
            skipNumeric = new Toggle("Skip numeric-only text") { name = "autoSkipNumeric", value = true }; scanning.Add(skipNumeric);
            minimum = new IntegerField("Minimum text length") { name = "autoMinimum", value = 2 }; scanning.Add(minimum);
            var keys = new Foldout { text = "Key generation", value = false }; settings.Add(keys);
            pattern = new EnumField("Pattern", AutoLocalizeKeyPattern.ParentName) { name = "autoPattern" }; keys.Add(pattern);
            custom = new TextField("Custom pattern") { name = "autoCustom", value = "{parent}/{name}", tooltip = "Tokens: {scene}, {parent}, {name}, {text}" }; keys.Add(custom);
            prefix = new TextField("Prefix") { name = "autoPrefix" }; keys.Add(prefix);
            custom.style.display = DisplayStyle.None;
            pattern.RegisterValueChangedCallback(e => custom.style.display = (AutoLocalizeKeyPattern)e.newValue == AutoLocalizeKeyPattern.Custom ? DisplayStyle.Flex : DisplayStyle.None);
            var addresses = set.Sources.Where(s => s.Language == channel.DefaultLanguage).SelectMany(s => s.Sources)
                .Where(s => s.Kind == LocalizationSourceKind.TextTable).Select(s => s.Address).Distinct().ToList();
            addresses.Insert(0, "Automatic"); destination = new DropdownField("Text source", addresses, 0) { name = "autoDestination", tooltip = "Automatic requires a single source. If none exists, preview proposes a new JSON file." }; settings.Add(destination);
            destination.RegisterValueChangedCallback(e => { CancelPreview(); e.StopPropagation(); });
            Add(new Button(() => Scan((ScanScope)scope.value == ScanScope.SelectedObjects ? Selection.gameObjects : SceneManager.GetActiveScene().GetRootGameObjects())) { text = "Scan", name = "autoScan" });
            settings.RegisterCallback<ChangeEvent<Enum>>(_ => ClearScan()); settings.RegisterCallback<ChangeEvent<bool>>(_ => ClearScan());
            settings.RegisterCallback<ChangeEvent<int>>(_ => ClearScan()); settings.RegisterCallback<ChangeEvent<string>>(_ => ClearScan());
            status = new HelpBox("", HelpBoxMessageType.Error) { name = "autoStatus" }; status.style.display = DisplayStyle.None; Add(status);
            warnings = new HelpBox("", HelpBoxMessageType.Warning) { name = "autoWarnings" }; warnings.style.display = DisplayStyle.None; Add(warnings);
            summary = new Label("No scan results.") { name = "autoSummary", style = { marginTop = 10 } }; Add(summary);
            var selection = new VisualElement { name = "autoSelection", style = { flexDirection = FlexDirection.Row } };
            selection.Add(new Button(() => SelectAll(true)) { text = "Select all" }); selection.Add(new Button(() => SelectAll(false)) { text = "Select none" }); Add(selection);
            rows = new ListView { name = "autoResults", fixedItemHeight = 30, virtualizationMethod = CollectionVirtualizationMethod.FixedHeight,
                makeItem = MakeRow, bindItem = BindRow, itemsSource = candidates };
            rows.style.flexGrow = 1; rows.style.minHeight = 90;
            rows.selectionChanged += selected => { if (selected.FirstOrDefault() is AutoLocalizeCandidate candidate) ShowDetail(candidate); }; Add(rows);
            detail = new TextField("Selected text") { name = "autoDetail", multiline = true, isReadOnly = true }; detail.style.height = 85; Add(detail);
            previewSummary = new Label("") { name = "autoPreviewSummary", style = { whiteSpace = WhiteSpace.Normal, marginTop = 6 } }; Add(previewSummary);
            var actions = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            preview = new Button(() => PreviewChanges()) { text = "Preview changes", name = "autoPreview" }; actions.Add(preview);
            apply = new Button(() => ApplyPreview()) { text = "Apply and save", name = "autoApply" }; LanguagePickerStyle.Primary(apply); actions.Add(apply);
            actions.Add(new Button(CancelPreview) { text = "Cancel preview", name = "autoCancel" }); Add(actions);
            UpdateSummary(); apply.SetEnabled(false); RegisterCallback<DetachFromPanelEvent>(_ => Dispose());
        }
        private VisualElement MakeRow()
        {
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
            var selected = new Toggle { name = "selected", style = { width = 24 } }; row.Add(selected);
            var key = new TextField { name = "key", style = { flexGrow = 1, flexBasis = 0, minWidth = 100 } }; row.Add(key);
            var text = new Label { name = "text", enableRichText = false, style = { flexGrow = 1, flexBasis = 0, marginLeft = 10, overflow = Overflow.Hidden, textOverflow = TextOverflow.Ellipsis } }; row.Add(text);
            selected.RegisterValueChangedCallback(e => { if (row.userData is AutoLocalizeCandidate c && plan == null) { c.Selected = e.newValue; UpdateSummary(); } });
            key.RegisterValueChangedCallback(e => { if (row.userData is AutoLocalizeCandidate c && plan == null) c.Key = e.newValue; }); return row;
        }
        private void BindRow(VisualElement row, int index)
        {
            var candidate = candidates[index]; row.userData = candidate;
            row.Q<Toggle>("selected").SetValueWithoutNotify(candidate.Selected); row.Q<TextField>("key").SetValueWithoutNotify(candidate.Key);
            row.Q<Toggle>("selected").SetEnabled(plan == null); row.Q<TextField>("key").isReadOnly = plan != null;
            row.Q<Label>("text").text = candidate.Text.Replace('\n', ' ').Replace('\r', ' '); row.tooltip = candidate.Path;
        }
        public bool Scan(IEnumerable<GameObject> roots)
        {
            ClearScan();
            try
            {
                candidates = ProjectAutoLocalize.Scan(config, roots, new AutoLocalizeOptions
                {
                    Prefabs = (AutoLocalizePrefabMode)prefabs.value, KeyPattern = (AutoLocalizeKeyPattern)pattern.value,
                    CustomPattern = custom.value, KeyPrefix = prefix.value, MinimumLength = minimum.value,
                    SkipNumericOnly = skipNumeric.value, SkipAlreadyLocalized = skipLocalized.value
                }, channelId, setId).ToList();
                rows.itemsSource = candidates; rows.Rebuild(); UpdateSummary(); return true;
            }
            catch (Exception error) { ShowError(error.Message); return false; }
        }
        public bool PreviewChanges()
        {
            CancelPreview();
            try
            {
                plan = ProjectAutoLocalize.Preview(config, candidates, channelId, setId, destination.index == 0 ? null : destination.value);
                int scene = plan.Bindings.Count(b => b.PrefabPath == null), assets = plan.Bindings.Select(b => b.PrefabPath).Where(p => p != null).Distinct().Count();
                previewSummary.text = "Bindings: " + plan.Bindings.Count + " · New entries: " + plan.Bindings.Count(b => b.CreatesEntry)
                    + " · Scene objects: " + scene + " · Prefab files: " + assets
                    + (plan.CreatedSourcePath == null ? "" : "\nCreate text source: " + plan.CreatedSourcePath);
                warnings.text = string.Join("\n", plan.Warnings); warnings.style.display = plan.Warnings.Count == 0 ? DisplayStyle.None : DisplayStyle.Flex;
                settings.SetEnabled(false); rows.RefreshItems(); apply.SetEnabled(true); preview.SetEnabled(false);
                this.Q<VisualElement>("autoSelection").SetEnabled(false);
                if (rows.selectedItem is AutoLocalizeCandidate selected) ShowDetail(selected); return true;
            }
            catch (Exception error) { ShowError(error.Message); return false; }
        }
        public bool ApplyPreview()
        {
            if (plan == null || !apply.enabledSelf) { ShowError("Prepare a valid preview first."); return false; }
            try { plan.Apply(); }
            catch (Exception error) { ShowError(error.Message + " Scan again before applying."); apply.SetEnabled(false); return false; }
            ClearScan(); summary.text = "Bindings and translations saved. Save scene changes when ready; Undo restores this operation together.";
            changed?.Invoke(); return true;
        }
        public void CancelPreview()
        {
            plan?.Dispose(); plan = null; previewSummary.text = ""; apply.SetEnabled(false); settings.SetEnabled(true);
            this.Q<VisualElement>("autoSelection").SetEnabled(true); rows.RefreshItems(); UpdateSummary(); ShowError(null);
            warnings.style.display = DisplayStyle.None; if (rows.selectedItem is AutoLocalizeCandidate selected) ShowDetail(selected);
        }
        public void ClearScan()
        { CancelPreview(); candidates.Clear(); rows.itemsSource = candidates; rows.Rebuild(); detail.SetValueWithoutNotify(""); UpdateSummary(); }
        private void SelectAll(bool selected)
        { if (plan != null) return; foreach (var candidate in candidates) candidate.Selected = selected; rows.RefreshItems(); UpdateSummary(); }
        private void UpdateSummary()
        { summary.text = "Found: " + candidates.Count + " · Selected: " + candidates.Count(c => c.Selected); preview.SetEnabled(plan == null && candidates.Any(c => c.Selected)); DraftChanged?.Invoke(); }
        private void ShowDetail(AutoLocalizeCandidate candidate)
        {
            var binding = plan?.Bindings.FirstOrDefault(b => b.Target == candidate.Target);
            detail.SetValueWithoutNotify(candidate.Path + "\n" + (candidate.PrefabPath ?? "Scene object") + "\n" + candidate.Text
                + (binding == null ? "" : "\n" + (binding.CreatesEntry ? "New entry: " : "Use existing entry: ") + binding.EntryId));
        }
        public bool CanLeave()
        {
            if (!HasPendingChanges) return true;
            if (!EditorUtility.DisplayDialog("Auto-Localize scan", "Discard this scan and its preview? Nothing has been saved.", "Discard", "Cancel")) return false;
            ClearScan(); return true;
        }
        private void ShowError(string message) { status.text = message ?? ""; status.style.display = string.IsNullOrEmpty(message) ? DisplayStyle.None : DisplayStyle.Flex; }
        public void Dispose() => ClearScan();
    }
}
