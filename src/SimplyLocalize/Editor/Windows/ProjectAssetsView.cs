using System;
using System.Collections.Generic;
using System.Linq;
using SimplyLocalize.Editor.AssetPreviews;
using SimplyLocalize.Editor.Data;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace SimplyLocalize.Editor.Windows
{
    /// <summary>Asset entry authoring for one channel/set. Hosts must call CanLeave before replacing a pending view.</summary>
    public sealed class ProjectAssetsView : VisualElement, IDisposable
    {
        private readonly LocalizationProjectConfig config;
        private readonly string channelId, setId;
        private readonly ILocalizationEditorAssetReader reader;
        private readonly Action changed;
        private EditorProjectAssetData data;
        private LocalizationProjectAssetEdit edit;
        private readonly Dictionary<string, Object> values = new();
        private readonly HashSet<string> removed = new();
        private readonly Dictionary<string, string> destinations = new();
        private readonly List<KeyValuePair<string, string>> visibleRows = new();
        private readonly List<Action> previews = new();
        private ListView list;
        private ScrollView details;
        private HelpBox message;
        private Button save;
        private string selected, pendingKey, query = "", editError;
        private Type previewType = typeof(Object);
        private bool disposed;
        private LocalizationDraftBaseline restoredBaseline;
        private ProjectViewDraft blockedDraft;
        public event Action DraftChanged;
        public string SelectedEntryId => selected;
        public bool HasPendingChanges => blockedDraft != null || edit != null && (values.Count > 0 || removed.Count > 0 || pendingKey != data.Entries[selected]);
        public ProjectViewDraft CaptureDraft()
        {
            if (blockedDraft != null) return blockedDraft.Copy();
            if (!HasPendingChanges) return null;
            return new ProjectViewDraft { Active = true, Kind = ProjectViewDraft.DraftKind.Asset, Config = config, Channel = channelId, Set = setId, Entry = selected,
                Key = pendingKey, Baseline = restoredBaseline ?? edit.CaptureBaseline(),
                Values = values.Keys.Concat(removed).Distinct().Select(code => new ProjectViewDraft.Value { Language = code, Removed = removed.Contains(code),
                    Asset = values.TryGetValue(code, out var value) ? value : null, HadAsset = values.TryGetValue(code, out value) && !ReferenceEquals(value, null),
                    Destination = destinations.TryGetValue(code, out var address) ? address : null }).ToList() };
        }
        public void RestoreDraft(ProjectViewDraft snapshot)
        {
            snapshot.ValidateRoute(config, ProjectViewDraft.DraftKind.Asset, channelId, setId);
            if (HasPendingChanges) throw new InvalidOperationException("Save or discard the existing draft first.");
            SelectEntry(snapshot.Entry); string reason = null;
            try { snapshot.ValidateValues(); } catch (Exception failure) { reason = failure.Message; }
            if (reason != null || edit == null || data == null || snapshot.Values.Any(v => !data.Languages.Contains(v.Language)))
            {
                CloseEdit(); blockedDraft = snapshot.Copy(); details.Clear();
                details.Add(new ProjectDraftRecoveryView(blockedDraft, reason ?? "The entry or its source is no longer available for editing.", RetryDraft, () => Reload(true)));
                DraftChanged?.Invoke(); return;
            }
            restoredBaseline = snapshot.Baseline; pendingKey = snapshot.Key;
            foreach (var item in snapshot.Values)
            {
                if (item.Removed) removed.Add(item.Language); else values[item.Language] = item.Asset;
                if (!string.IsNullOrEmpty(item.Destination)) destinations[item.Language] = item.Destination;
            }
            BuildDetails(); UpdateSave();
            try { restoredBaseline.Validate(); } catch (Exception failure) { ShowError(failure.Message); }
        }
        private void RetryDraft() { using var snapshot = CaptureDraft(); CloseEdit(); Reload(); RestoreDraft(snapshot); }
        public ProjectAssetsView(LocalizationProjectConfig config, string channelId = null, string setId = null,
            Action changed = null, ILocalizationEditorAssetReader reader = null)
        {
            this.config = config != null ? config : throw new ArgumentNullException(nameof(config));
            this.channelId = channelId ?? config.MainChannelId;
            this.setId = setId ?? config.Channels.Single(c => c.Id == this.channelId).DefaultSetId;
            this.changed = changed; this.reader = reader; name = "projectAssets"; style.flexGrow = 1;
            RegisterCallback<AttachToPanelEvent>(_ => LocalizationProjectUndo.Changed += OnHistory);
            RegisterCallback<DetachFromPanelEvent>(_ => Dispose()); Build(); Reload();
        }
        private void Build()
        {
            var toolbar = new Toolbar(); var search = new ToolbarSearchField { name = "assetSearch" }; search.style.flexGrow = 1;
            search.RegisterValueChangedCallback(e => { query = e.newValue ?? ""; Filter(); }); toolbar.Add(search);
            toolbar.Add(new ToolbarButton(() => { if (CanLeave()) Reload(); }) { text = "Reload from disk" }); Add(toolbar);
            message = new HelpBox("", HelpBoxMessageType.Error) { name = "assetMessage" }; message.style.display = DisplayStyle.None; Add(message);
            var split = new TwoPaneSplitView(0, 240, TwoPaneSplitViewOrientation.Horizontal); split.style.flexGrow = 1; Add(split);
            var left = new VisualElement { style = { minWidth = 180 } };
            list = new ListView(visibleRows, 25, () => new Label(), (element, index) =>
            { var label = (Label)element; label.text = visibleRows[index].Value; label.tooltip = visibleRows[index].Key; })
            { name = "assetEntries", selectionType = SelectionType.Single, virtualizationMethod = CollectionVirtualizationMethod.FixedHeight };
            list.style.flexGrow = 1; list.selectionChanged += items =>
            {
                if (items.FirstOrDefault() is KeyValuePair<string, string> item && item.Key != selected)
                { if (CanLeave()) SelectEntry(item.Key); else RestoreSelection(); }
            };
            left.Add(list); var key = new TextField { name = "newAssetKey" }; left.Add(key);
            left.Add(new Button(() => AddEntry(key.value)) { text = "Add key", name = "addAssetKey" }); split.Add(left);
            details = new ScrollView { name = "assetDetails", style = { flexGrow = 1, minWidth = 280, paddingLeft = 16, paddingRight = 16, paddingTop = 12 } }; split.Add(details);
        }
        public bool CanLeave()
        {
            if (!HasPendingChanges) return true;
            int choice = EditorUtility.DisplayDialogComplex("Unsaved localized assets", "Save this entry before leaving?", "Save", "Cancel", "Discard");
            if (choice == 1) return false; if (choice == 0) return SaveChanges(); CloseEdit(); return true;
        }
        public bool Reload(bool discard = false)
        {
            if (HasPendingChanges && !discard) { ShowError("Save or discard the current entry before reloading."); return false; }
            CloseEdit();
            try
            {
                data = EditorProjectAssetData.Load(config, channelId, setId, reader); ShowError(null); Filter();
                if (selected != null && data.Entries.ContainsKey(selected)) SelectEntry(selected);
                else { selected = null; ShowEmpty(); }
                return true;
            }
            catch (Exception failure) { data = null; visibleRows.Clear(); list.Rebuild(); details.Clear(); ShowError(failure.Message); return false; }
        }
        public void SelectEntry(string entryId)
        {
            if (HasPendingChanges) throw new InvalidOperationException("Save or discard the current entry first.");
            CloseEdit(); selected = entryId;
            try
            {
                // Capture every native source before displaying values, including languages outside this channel.
                try
                {
                    edit = new LocalizationProjectAssetEdit(config);
                    foreach (var source in config.Sets.Single(s => s.Id == setId).Sources.SelectMany(s => s.Sources).Where(s => s.Kind == LocalizationSourceKind.AssetTable))
                        edit.EditSource<LocalizationAssetTable>(source);
                }
                catch (Exception failure) { CloseEdit(); editError = failure.Message; }
                data = EditorProjectAssetData.Load(config, channelId, setId, reader);
                if (!data.Entries.ContainsKey(entryId)) throw new ArgumentException("This asset entry no longer belongs to the selected set.");
                pendingKey = data.Entries[entryId]; ShowError(null); BuildDetails(); RestoreSelection();
            }
            catch (Exception failure) { CloseEdit(); selected = null; details.Clear(); ShowError(failure.Message); }
        }
        private void BuildDetails()
        {
            details.Clear(); previews.Clear();
            var key = new TextField("Key") { name = "assetKey", value = pendingKey }; key.SetEnabled(edit != null);
            key.RegisterValueChangedCallback(e => { if (edit == null) return; pendingKey = e.newValue; UpdateSave(); }); details.Add(key);
            details.Add(new TextField("ID") { name = "assetId", value = selected, isReadOnly = true });
            if (edit == null) details.Add(new HelpBox("Preview only. " + editError, HelpBoxMessageType.Warning));
            var types = new[] { typeof(Object), typeof(Texture), typeof(Sprite), typeof(AudioClip), typeof(TMPro.TMP_FontAsset), typeof(Font), typeof(Material), typeof(GameObject) };
            var type = new DropdownField("Preview as", types.Select(t => t.Name).ToList(), previewType.Name) { name = "assetPreviewType" };
            type.RegisterValueChangedCallback(e => { previewType = types.Single(t => t.Name == e.newValue); foreach (var refresh in previews) refresh(); }); details.Add(type);
            details.Add(new Label("Preview uses saved values and the channel's fallback. Save to preview your changes.") { style = { whiteSpace = WhiteSpace.Normal } });
            foreach (string code in data.Languages) AddLanguage(code);
            var actions = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 12 } };
            save = new Button(() => SaveChanges()) { text = "Save entry", name = "saveAsset" }; LanguagePickerStyle.Primary(save); actions.Add(save);
            actions.Add(new Button(() => Reload(true)) { text = "Discard", name = "discardAsset" }); details.Add(actions); UpdateSave();
            var management = new Foldout { text = "Move or remove entry", name = "manageAsset" }; management.SetEnabled(edit != null);
            management.Add(new ProjectEntryMovePicker(config, setId, LocalizationSourceKind.AssetTable, "Asset", (target, addresses) => MoveEntry(target, addresses)));
            management.Add(new Button(() =>
            {
                if (EditorUtility.DisplayDialog("Remove localization entry", "Remove this key and its asset bindings? Asset files and keys still used by other registered entries are kept.", "Remove", "Cancel")) RemoveEntry();
            }) { text = "Remove entry", name = "deleteAsset" }); details.Add(management);
        }
        private void AddLanguage(string code)
        {
            var card = new VisualElement(); LanguagePickerStyle.Card(card);
            card.Add(LanguagePickerStyle.Heading(config.Languages.Single(l => l.Code == code).DisplayName + " · " + code));
            bool exists = data.TryGetDirect(selected, code, out var direct);
            var field = new ObjectField("Direct asset") { name = "asset_" + code, objectType = typeof(Object), allowSceneObjects = false,
                value = removed.Contains(code) ? null : values.TryGetValue(code, out var pending) ? pending : direct?.Asset }; field.SetEnabled(edit != null);
            void SetValue(Object value)
            {
                if (edit == null) return; removed.Remove(code);
                if (exists && value == direct.Asset) values.Remove(code); else values[code] = value;
                field.SetValueWithoutNotify(value); UpdateSave();
            }
            field.RegisterValueChangedCallback(e => SetValue(e.newValue)); card.Add(field);
            card.Add(new Label(exists ? "Source: " + direct.Source.ProviderId + ":" + direct.Source.Address : "No direct entry")
                { name = "assetOrigin_" + code, style = { whiteSpace = WhiteSpace.Normal } });
            if (exists && config.Sets.SelectMany(s => s.Sources).SelectMany(s => s.Sources).Count(s => s.Kind == direct.Source.Kind && s.ProviderId == direct.Source.ProviderId
                && string.Equals(s.Address, direct.Source.Address, s.ProviderId == "resources" ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) > 1)
                card.Add(new HelpBox("Shared source: editing this value also affects other connections to this table.", HelpBoxMessageType.Info));
            var sources = config.Sets.Single(s => s.Id == setId).Sources.FirstOrDefault(s => s.Language == code)?.Sources.Where(s => s.Kind == LocalizationSourceKind.AssetTable).ToArray() ?? Array.Empty<LocalizationSourceReference>();
            if (!exists && sources.Length > 1)
            {
                var addresses = sources.Select(s => s.Address).ToList();
                var destination = new DropdownField("Write to", new[] { "Choose a source" }.Concat(addresses).ToList(), "Choose a source") { name = "assetDestination_" + code };
                if (destinations.TryGetValue(code, out var chosen)) destination.SetValueWithoutNotify(chosen);
                destination.RegisterValueChangedCallback(e => { if (addresses.Contains(e.newValue)) destinations[code] = e.newValue; else destinations.Remove(code); UpdateSave(); }); card.Add(destination);
            }
            var empty = new Button(() => SetValue(null)) { text = "Store empty value", name = "emptyAsset_" + code }; empty.SetEnabled(edit != null); card.Add(empty);
            if (exists)
            {
                var remove = new Button(() => { if (edit == null) return; values.Remove(code); removed.Add(code); field.SetValueWithoutNotify(null); UpdateSave(); })
                    { text = "Remove direct entry", name = "removeAsset_" + code }; remove.SetEnabled(edit != null); card.Add(remove);
            }
            if (sources.Length == 0) card.Add(new Button(() => { if (CanLeave()) CreateTable(code); }) { text = "Create asset table", name = "createAssetTable_" + code });
            var resolved = new Label { name = "assetResolved_" + code, style = { whiteSpace = WhiteSpace.Normal } }; card.Add(resolved);
            Object previewAsset = null;
            var preview = new IMGUIContainer(() =>
            {
                if (previewAsset != null) AssetPreviewRegistry.GetRendererFor(previewAsset).DrawPreview(GUILayoutUtility.GetRect(100, 120, GUILayout.ExpandWidth(true)), previewAsset);
            }) { name = "assetPreview_" + code }; card.Add(preview);
            void Refresh()
            {
                bool found = data.TryResolve(selected, code, out var cell, previewType); previewAsset = found ? cell.Asset : null;
                resolved.text = found ? (cell.Language == code ? "Direct" : "Fallback") + " · " + cell.Language + " · " + cell.Asset.name + " (" + cell.Asset.GetType().Name + ")" : "No matching asset for " + previewType.Name;
                preview.style.display = found ? DisplayStyle.Flex : DisplayStyle.None; preview.MarkDirtyRepaint();
            }
            previews.Add(Refresh); Refresh(); details.Add(card);
        }
        public bool SaveChanges()
        {
            if (!HasPendingChanges) return true;
            try
            {
                if (blockedDraft != null) throw new InvalidOperationException("Restore the missing source or discard the recovered draft before saving.");
                restoredBaseline?.Validate();
                if (values.Values.Any(value => !ReferenceEquals(value, null) && value == null)) throw new InvalidOperationException("An assigned asset was deleted. Choose a replacement before saving.");
                edit.ResetChanges(); edit.RenameAssetEntry(selected, pendingKey);
                foreach (string code in removed) edit.RemoveAsset(selected, code);
                foreach (var pair in values) edit.SetAsset(selected, pair.Key, pair.Value, destinations.TryGetValue(pair.Key, out var address) ? address : null);
                LocalizationProjectUndo.Record(edit.Commit(), "Edit localized asset"); CloseEdit(); Reload(); changed?.Invoke(); return true;
            }
            catch (Exception failure) { ShowError(failure.Message); return false; }
        }
        public bool AddEntry(string key)
        {
            if (HasPendingChanges) { ShowError("Save or discard the current entry first."); return false; }
            try
            {
                using var registration = new LocalizationProjectEdit(config);
                string id = registration.Draft.Sets.Single(s => s.Id == setId).AddEntry(key, LocalizationEntryKind.Asset).Id;
                EditorProjectAssetData.Load(registration.Draft, channelId, setId, reader);
                LocalizationProjectUndo.Record(registration.Commit(), "Register localized asset"); selected = id; Reload(); changed?.Invoke(); return true;
            }
            catch (Exception failure) { ShowError(failure.Message); return false; }
        }
        public bool CreateTable(string language)
        {
            if (HasPendingChanges) { ShowError("Save or discard the current entry first."); return false; }
            try { LocalizationProjectUndo.Record(LocalizationProjectAuthoring.CreateAssetTable(config, setId, language), "Create asset table"); CloseEdit(); Reload(); changed?.Invoke(); return true; }
            catch (Exception failure) { ShowError(failure.Message); return false; }
        }
        public bool MoveEntry(string targetSetId, IReadOnlyDictionary<string, string> destinations = null) => ApplyEntryOperation("Move localized asset", e => e.MoveAssetEntry(selected, targetSetId, destinations));
        public bool RemoveEntry() => ApplyEntryOperation("Remove localized asset", e => e.RemoveAssetEntry(selected));
        private bool ApplyEntryOperation(string label, Action<LocalizationProjectAssetEdit> operation)
        {
            if (HasPendingChanges) { ShowError("Save or discard the current entry first."); return false; }
            try
            {
                using var transaction = new LocalizationProjectAssetEdit(config); operation(transaction);
                LocalizationProjectUndo.Record(transaction.Commit(), label); CloseEdit(); selected = null; Reload(); changed?.Invoke(); return true;
            }
            catch (Exception failure) { ShowError(failure.Message); return false; }
        }
        private void ShowEmpty()
        {
            details.Clear(); details.Add(new Label("Select an asset key, or add a new key."));
            var extras = data.Languages.SelectMany(data.GetUnregistered).GroupBy(c => c.Key).OrderBy(g => g.Key, StringComparer.Ordinal).ToArray();
            if (extras.Length == 0) return;
            details.Add(new HelpBox("These source keys are not registered in this set. Runtime ignores them until registered.", HelpBoxMessageType.Info));
            var unregistered = new ListView(extras, 28, () => new Button(), (element, index) =>
            { string key = extras[index].Key; var button = (Button)element; button.text = "Register " + key; button.clickable = new Clickable(() => AddEntry(key)); }) { name = "unregisteredAssets" };
            unregistered.style.height = 200; details.Add(unregistered);
        }
        private void Filter()
        { visibleRows.Clear(); if (data != null) visibleRows.AddRange(data.Entries.Where(p => p.Value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0).OrderBy(p => p.Value, StringComparer.Ordinal)); list.Rebuild(); RestoreSelection(); }
        private void RestoreSelection() { int index = visibleRows.FindIndex(p => p.Key == selected); list.SetSelectionWithoutNotify(index < 0 ? Array.Empty<int>() : new[] { index }); }
        private void UpdateSave() { save?.SetEnabled(HasPendingChanges); DraftChanged?.Invoke(); }
        private void ShowError(string error) { message.text = error ?? ""; message.style.display = string.IsNullOrEmpty(error) ? DisplayStyle.None : DisplayStyle.Flex; }
        private void OnHistory() { if (HasPendingChanges) ShowError("History changed. Discard and reload before saving."); else Reload(); }
        private void CloseEdit() { edit?.Dispose(); edit = null; values.Clear(); removed.Clear(); destinations.Clear(); previews.Clear(); editError = null; restoredBaseline = null; blockedDraft?.Dispose(); blockedDraft = null; }
        public void Dispose() { if (disposed) return; disposed = true; LocalizationProjectUndo.Changed -= OnHistory; CloseEdit(); }
    }
}
