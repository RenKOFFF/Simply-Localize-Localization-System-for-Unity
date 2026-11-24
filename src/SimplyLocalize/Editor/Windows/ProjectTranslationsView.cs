using System;
using System.Collections.Generic;
using System.Linq;
using SimplyLocalize.Editor.Data;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace SimplyLocalize.Editor.Windows
{
    /// <summary>Virtualized entry selection and explicit text edits for one channel/set.
    /// The host must call CanLeave before replacing a view with pending changes.</summary>
    public sealed class ProjectTranslationsView : VisualElement, IDisposable
    {
        private readonly LocalizationProjectConfig config;
        private readonly string channelId, setId;
        private readonly ILocalizationEditorTextReader reader;
        private readonly Action changed;
        private EditorProjectTextData data;
        private LocalizationProjectEdit edit;
        private readonly Dictionary<string, string> values = new(), destinations = new();
        private readonly List<KeyValuePair<string, string>> visibleRows = new();
        private ListView list;
        private ScrollView details;
        private HelpBox message;
        private string selected, pendingKey, query = "", editError;
        private string originalDescription, pendingDescription;
        private Button save;
        private bool disposed;
        private LocalizationDraftBaseline restoredBaseline;
        private ProjectViewDraft blockedDraft;
        public bool HasPendingChanges => blockedDraft != null || edit != null && (values.Count > 0 || pendingKey != data.Entries[selected] || pendingDescription != originalDescription);
        public event Action DraftChanged;
        public ProjectViewDraft CaptureDraft()
        {
            if (blockedDraft != null) return blockedDraft.Copy();
            if (!HasPendingChanges) return null;
            return new ProjectViewDraft { Active = true, Kind = ProjectViewDraft.DraftKind.Text, Config = config, Channel = channelId, Set = setId, Entry = selected,
                Key = pendingKey, Description = pendingDescription, Baseline = restoredBaseline ?? edit.CaptureBaseline(),
                Values = values.Select(p => new ProjectViewDraft.Value { Language = p.Key, Text = p.Value, Removed = p.Value == null,
                    Destination = destinations.TryGetValue(p.Key, out var address) ? address : null }).ToList() };
        }
        public void RestoreDraft(ProjectViewDraft snapshot)
        {
            snapshot.ValidateRoute(config, ProjectViewDraft.DraftKind.Text, channelId, setId);
            if (HasPendingChanges) throw new InvalidOperationException("Save or discard the existing draft first.");
            SelectEntry(snapshot.Entry);
            if (edit == null || data == null || snapshot.Values.Any(v => !data.Languages.Contains(v.Language)))
            {
                CloseEdit(); blockedDraft = snapshot.Copy();
                details.Clear(); details.Add(new ProjectDraftRecoveryView(blockedDraft, "The entry or its source is no longer available for editing.", RetryDraft, () => Reload(true)));
                DraftChanged?.Invoke(); return;
            }
            restoredBaseline = snapshot.Baseline; pendingKey = snapshot.Key; pendingDescription = snapshot.Description;
            foreach (var item in snapshot.Values) { values[item.Language] = item.Removed ? null : item.Text; if (!string.IsNullOrEmpty(item.Destination)) destinations[item.Language] = item.Destination; }
            BuildDetails(); UpdateSave();
            try { restoredBaseline.Validate(); } catch (Exception failure) { ShowError(failure.Message); }
        }
        private void RetryDraft() { using var snapshot = CaptureDraft(); CloseEdit(); Reload(); RestoreDraft(snapshot); }
        public string SelectedEntryId => selected;
        public ProjectTranslationsView(LocalizationProjectConfig config, string channelId = null, string setId = null,
            Action changed = null, ILocalizationEditorTextReader reader = null)
        {
            this.config = config != null ? config : throw new ArgumentNullException(nameof(config));
            this.channelId = channelId ?? config.MainChannelId;
            this.setId = setId ?? config.Channels.Single(c => c.Id == this.channelId).DefaultSetId;
            this.changed = changed; this.reader = reader;
            name = "projectTranslations"; style.flexGrow = 1;
            RegisterCallback<AttachToPanelEvent>(_ => LocalizationProjectUndo.Changed += OnHistory);
            RegisterCallback<DetachFromPanelEvent>(_ => { LocalizationProjectUndo.Changed -= OnHistory; Dispose(); });
            Build(); Reload();
        }
        private void Build()
        {
            var toolbar = new Toolbar();
            var search = new ToolbarSearchField { name = "textSearch" }; search.style.flexGrow = 1;
            search.RegisterValueChangedCallback(e => { query = e.newValue ?? ""; Filter(); }); toolbar.Add(search);
            toolbar.Add(new ToolbarButton(() => { if (CanLeave()) Reload(); }) { text = "Reload from disk", name = "reloadText" }); Add(toolbar);
            message = new HelpBox("", HelpBoxMessageType.Error) { name = "textMessage" }; message.style.display = DisplayStyle.None; Add(message);
            var split = new TwoPaneSplitView(0, 240, TwoPaneSplitViewOrientation.Horizontal); split.style.flexGrow = 1; Add(split);
            var left = new VisualElement { style = { minWidth = 180 } };
            list = new ListView(visibleRows, 25, () => new Label(), (element, index) =>
            { var label = (Label)element; label.text = visibleRows[index].Value; label.tooltip = visibleRows[index].Key; })
            { name = "textEntries", selectionType = SelectionType.Single, virtualizationMethod = CollectionVirtualizationMethod.FixedHeight };
            list.style.flexGrow = 1; list.RegisterSelectionChanged(items =>
            {
                if (items.FirstOrDefault() is KeyValuePair<string, string> item && item.Key != selected)
                {
                    if (CanLeave()) SelectEntry(item.Key); else RestoreSelection();
                }
            });
            left.Add(list);
            var key = new TextField { name = "newTextKey", tooltip = "New text key in the selected set" }; left.Add(key);
            left.Add(new Button(() => AddEntry(key.value)) { text = "Add key", name = "addTextKey" }); split.Add(left);
            details = new ScrollView { name = "textDetails", style = { flexGrow = 1, minWidth = 280, paddingLeft = 16, paddingRight = 16, paddingTop = 12 } }; split.Add(details);
        }
        /// <summary>Returns false on Cancel or a failed Save. Discard is an explicit user choice.</summary>
        public bool CanLeave()
        {
            if (!HasPendingChanges) return true;
            int choice = EditorUtility.DisplayDialogComplex("Unsaved localization text", "Save this entry before leaving?", "Save", "Cancel", "Discard");
            if (choice == 1) return false;
            if (choice == 0) return SaveChanges();
            CloseEdit(); return true;
        }
        /// <summary>Reload after an explicit discard, or when no edit is pending. Never silently drops typed text.</summary>
        public bool Reload(bool discard = false)
        {
            if (HasPendingChanges && !discard) { ShowError("Save or discard the current entry before reloading."); return false; }
            CloseEdit();
            try
            {
                data = EditorProjectTextData.Load(config, channelId, setId, reader); ShowError(null); Filter();
                if (selected != null && data.Entries.ContainsKey(selected)) SelectEntry(selected);
                else { selected = null; ShowEmpty(); }
                return true;
            }
            catch (Exception failure) { data = null; visibleRows.Clear(); list.Rebuild(); details.Clear(); ShowError(failure.Message); return false; }
        }
        public void SelectEntry(string entryId)
        {
            if (HasPendingChanges) throw new InvalidOperationException("Save or discard the current edit before selecting another entry.");
            CloseEdit(); selected = entryId;
            try
            {
                // Capture write baselines before reading the displayed values. Later external edits fail Commit.
                try { edit = new LocalizationProjectEdit(config, setId); }
                catch (Exception failure) { editError = failure.Message; }
                data = EditorProjectTextData.Load(config, channelId, setId, reader);
                if (!data.Entries.ContainsKey(entryId)) throw new ArgumentException("This text entry no longer belongs to the selected set.");
                pendingKey = data.Entries[entryId];
                originalDescription = pendingDescription = edit != null ? edit.GetDescription(entryId) : LocalizationProjectNotes.Load(config).TryGetValue(entryId, out var note) ? note : "";
                ShowError(null); BuildDetails(); RestoreSelection();
            }
            catch (Exception failure) { CloseEdit(); selected = null; details.Clear(); ShowError(failure.Message); }
        }
        private void BuildDetails()
        {
            details.Clear();
            var key = new TextField("Key") { name = "entryKey", value = pendingKey }; key.SetEnabled(edit != null);
            key.RegisterValueChangedCallback(e => { pendingKey = e.newValue; UpdateSave(); }); details.Add(key);
            details.Add(new TextField("ID") { name = "entryId", value = selected, isReadOnly = true });
            var description = new TextField("Translator note") { name = "entryDescription", value = pendingDescription, multiline = true }; description.SetEnabled(edit != null);
            description.RegisterValueChangedCallback(e => { if (edit == null) return; pendingDescription = e.newValue; UpdateSave(); }); details.Add(description);
            if (edit == null) details.Add(new HelpBox("Preview only. " + editError, HelpBoxMessageType.Warning));
            foreach (string code in data.Languages)
            {
                var card = new VisualElement(); LanguagePickerStyle.Card(card);
                card.Add(LanguagePickerStyle.Heading(config.Languages.Single(l => l.Code == code).DisplayName + " · " + code));
                bool exists = data.TryGetDirect(selected, code, out var direct);
                var value = new TextField { name = "translation_" + code, value = values.TryGetValue(code, out var pending) ? pending ?? "" : direct?.Value ?? "", multiline = true };
                value.style.minHeight = 50; value.style.whiteSpace = WhiteSpace.Normal; value.SetEnabled(edit != null);
                value.RegisterValueChangedCallback(e => { if (exists && e.newValue == direct.Value) values.Remove(code); else values[code] = e.newValue; UpdateSave(); }); card.Add(value);
                card.Add(new Label(exists ? "Source: " + direct.Source.ProviderId + ":" + direct.Source.Address : "No direct translation")
                    { name = "translationOrigin_" + code, style = { whiteSpace = WhiteSpace.Normal } });
                if ((!exists || string.IsNullOrEmpty(direct.Value)) && data.TryResolve(selected, code, out var fallback))
                    card.Add(new Label("Fallback · " + fallback.Language + ": " + fallback.Value) { name = "translationFallback_" + code, style = { whiteSpace = WhiteSpace.Normal } });
                var sources = config.Sets.Single(s => s.Id == setId).Sources.FirstOrDefault(s => s.Language == code)?.Sources
                    .Where(s => s.Kind == LocalizationSourceKind.TextTable).Select(s => s.Address).ToList() ?? new List<string>();
                if (!exists && sources.Count > 1)
                {
                    var destination = new DropdownField("Write to", new[] { "Choose a source" }.Concat(sources).ToList(), "Choose a source") { name = "translationDestination_" + code };
                    if (destinations.TryGetValue(code, out var chosen)) destination.SetValueWithoutNotify(chosen);
                    destination.RegisterValueChangedCallback(e => { if (sources.Contains(e.newValue)) destinations[code] = e.newValue; else destinations.Remove(code); UpdateSave(); }); card.Add(destination);
                }
                if (exists)
                {
                    var remove = new Button(() => { values[code] = null; value.SetValueWithoutNotify(""); UpdateSave(); }) { text = "Remove direct translation", name = "removeTranslation_" + code };
                    remove.SetEnabled(edit != null); card.Add(remove);
                }
                details.Add(card);
            }
            var actions = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 12 } };
            save = new Button(() => SaveChanges()) { text = "Save entry", name = "saveText" }; LanguagePickerStyle.Primary(save); actions.Add(save);
            actions.Add(new Button(() => Reload(true)) { text = "Discard", name = "discardText" }); details.Add(actions); UpdateSave();
            var management = new Foldout { text = "Move or remove entry", name = "manageText" };
            management.Add(new ProjectEntryMovePicker(config, setId, LocalizationSourceKind.TextTable, "Text", (target, addresses) => MoveEntry(target, addresses)));
            management.Add(new Button(() =>
            {
                if (EditorUtility.DisplayDialog("Remove localization entry", "Remove this key and its translations from the selected set? Shared source keys still used by another registered entry are kept.", "Remove", "Cancel")) RemoveEntry();
            }) { text = "Remove entry", name = "deleteText" }); management.SetEnabled(edit != null); details.Add(management);
        }
        public bool SaveChanges()
        {
            if (!HasPendingChanges) return true;
            try
            {
                if (blockedDraft != null) throw new InvalidOperationException("Restore the missing source or discard the recovered draft before saving.");
                restoredBaseline?.Validate();
                edit.ResetChanges();
                edit.RenameTextEntry(selected, pendingKey);
                edit.SetDescription(selected, pendingDescription);
                foreach (var pair in values)
                    if (pair.Value == null) edit.RemoveTranslation(selected, pair.Key);
                    else edit.SetTranslation(selected, pair.Key, pair.Value, destinations.TryGetValue(pair.Key, out var address) ? address : null);
                LocalizationProjectUndo.Record(edit.Commit(), "Edit localization text"); CloseEdit(); Reload(); changed?.Invoke(); return true;
            }
            catch (Exception failure) { ShowError(failure.Message); return false; }
        }
        public bool AddEntry(string key)
        {
            if (HasPendingChanges) { ShowError("Save or discard the current entry first."); return false; }
            try
            {
                using var registration = new LocalizationProjectEdit(config);
                string id = registration.Draft.Sets.Single(s => s.Id == setId).AddEntry(key).Id;
                // A manually authored key may already exist in several files; do not publish ambiguity.
                EditorProjectTextData.Load(registration.Draft, channelId, setId, reader);
                LocalizationProjectUndo.Record(registration.Commit(), "Register localization text");
                CloseEdit(); selected = id; Reload(); changed?.Invoke(); return true;
            }
            catch (Exception failure) { ShowError(failure.Message); return false; }
        }
        public bool MoveEntry(string targetSetId, IReadOnlyDictionary<string, string> destinationAddresses = null) => ApplyEntryOperation("Move localization text", transaction => transaction.MoveTextEntry(selected, targetSetId, destinationAddresses), targetSetId);
        public bool RemoveEntry() => ApplyEntryOperation("Remove localization text", transaction => transaction.RemoveTextEntry(selected));
        private bool ApplyEntryOperation(string label, Action<LocalizationProjectEdit> operation, string targetSetId = null)
        {
            if (HasPendingChanges) { ShowError("Save or discard the current entry first."); return false; }
            try
            {
                using var transaction = new LocalizationProjectEdit(config, targetSetId == null ? new[] { setId } : new[] { setId, targetSetId });
                operation(transaction); LocalizationProjectUndo.Record(transaction.Commit(), label); CloseEdit(); selected = null; Reload(); changed?.Invoke(); return true;
            }
            catch (Exception failure) { ShowError(failure.Message); return false; }
        }
        private void ShowEmpty()
        {
            details.Clear(); details.Add(new Label("Select a key to view its translations, or add a new key."));
            var unregistered = data.Languages.SelectMany(data.GetUnregistered).GroupBy(c => c.Key).OrderBy(g => g.Key, StringComparer.Ordinal).ToArray();
            if (unregistered.Length == 0) return;
            details.Add(new HelpBox("These keys exist in source files but are not registered in this set. They are ignored by runtime until registered.", HelpBoxMessageType.Info));
            var extras = new ListView(unregistered, 28, () => new Button(), (element, index) =>
            {
                var button = (Button)element; string key = unregistered[index].Key; button.text = "Register " + key;
                button.clickable = new Clickable(() => AddEntry(key));
            }) { name = "unregisteredText" }; extras.style.height = 200; details.Add(extras);
        }
        private void Filter()
        {
            visibleRows.Clear(); if (data != null) visibleRows.AddRange(data.Entries.Where(p => p.Value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0).OrderBy(p => p.Value, StringComparer.Ordinal));
            list.Rebuild(); RestoreSelection();
        }
        private void RestoreSelection() { int index = visibleRows.FindIndex(p => p.Key == selected); list.SetSelectionWithoutNotify(index < 0 ? Array.Empty<int>() : new[] { index }); }
        private void UpdateSave() { save?.SetEnabled(HasPendingChanges); DraftChanged?.Invoke(); }
        private void ShowError(string error) { message.text = error ?? ""; message.style.display = string.IsNullOrEmpty(error) ? DisplayStyle.None : DisplayStyle.Flex; }
        private void OnHistory() { if (HasPendingChanges) ShowError("History changed while this edit was open. Discard and reload before saving."); else Reload(); }
        private void CloseEdit() { edit?.Dispose(); edit = null; values.Clear(); destinations.Clear(); editError = null; restoredBaseline = null; blockedDraft?.Dispose(); blockedDraft = null; }
        public void Dispose() { if (disposed) return; disposed = true; LocalizationProjectUndo.Changed -= OnHistory; CloseEdit(); }
    }
}
