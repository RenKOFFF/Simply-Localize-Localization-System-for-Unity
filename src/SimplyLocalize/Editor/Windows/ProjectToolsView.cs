using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using SimplyLocalize.Editor.Data;
using UnityEditor;
using UnityEngine.UIElements;

namespace SimplyLocalize.Editor.Windows
{
    /// <summary>CSV preview/apply on saved project data. Hosts must resolve pending edits on other pages before entering.</summary>
    public sealed class ProjectToolsView : VisualElement, IDisposable
    {
        private readonly LocalizationProjectConfig config;
        private readonly string channelId, setId;
        private readonly Action changed;
        private ProjectCsvImportPlan plan;
        private readonly Toggle clearEmpty, includeSources;
        private readonly Label summary;
        private readonly HelpBox status, shared;
        private readonly ListView changes;
        private readonly TextField detail;
        private readonly Button apply;
        public ProjectCsvImportPlan Preview => plan;
        public bool HasPendingChanges => plan != null && !plan.IsApplied && plan.Changes.Count > 0;
        public ProjectToolsView(LocalizationProjectConfig config, string channelId = null, string setId = null, Action changed = null)
        {
            this.config = config != null ? config : throw new ArgumentNullException(nameof(config));
            this.channelId = channelId ?? config.MainChannelId; this.setId = setId ?? config.Channels.Single(c => c.Id == this.channelId).DefaultSetId; this.changed = changed;
            name = "projectTools"; style.flexGrow = 1; style.paddingLeft = style.paddingRight = 16; style.paddingTop = 12;
            Add(LanguagePickerStyle.Heading("CSV import / export"));
            Add(new Label("Export saved translations and translator notes. Preview imports before applying. IDs preserve references when keys are renamed or moved.") { style = { whiteSpace = WhiteSpace.Normal } });
            clearEmpty = new Toggle("Clear values for empty CSV cells") { name = "csvClearEmpty" };
            clearEmpty.RegisterValueChangedCallback(_ => { if (plan != null) { ClearPreview(); ShowError("Empty-cell policy changed. Prepare a new preview."); } }); Add(clearEmpty);
            includeSources = new Toggle("Include source-address columns in export") { name = "csvIncludeSources", tooltip = "Useful for explicit destinations when a language has multiple text tables." }; Add(includeSources);
            var actions = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            actions.Add(new Button(() => { string path = EditorUtility.SaveFilePanel("Export localization CSV", "", "translations.csv", "csv"); if (path.Length > 0) ExportFile(path); }) { text = "Export CSV", name = "csvExport" });
            actions.Add(new Button(() =>
            {
                string path = EditorUtility.OpenFilePanel("Preview localization CSV", "", "csv"); if (path.Length == 0) return;
                try { PreviewCsv(File.ReadAllText(path, new UTF8Encoding(false, true))); }
                catch (Exception failure) { ClearPreview(); ShowError(failure.Message); }
            }) { text = "Preview CSV", name = "csvImport" }); Add(actions);
            status = new HelpBox("", HelpBoxMessageType.Error) { name = "csvStatus" }; status.style.display = DisplayStyle.None; Add(status);
            summary = new Label("No import preview.") { name = "csvSummary", style = { whiteSpace = WhiteSpace.Normal, marginTop = 12 } }; Add(summary);
            shared = new HelpBox("", HelpBoxMessageType.Warning) { name = "csvSharedSources" }; shared.style.display = DisplayStyle.None; Add(shared);
            detail = new TextField("Selected change") { name = "csvChangeDetail", multiline = true, isReadOnly = true }; detail.style.height = 110;
            changes = new ListView { name = "csvChanges", fixedItemHeight = 28, virtualizationMethod = CollectionVirtualizationMethod.FixedHeight,
                makeItem = () => new Label { enableRichText = false }, bindItem = (element, index) =>
                { var change = plan.Changes[index]; ((Label)element).text = change.Kind + " · " + (change.Key ?? "") + " · " + (change.Language ?? ""); } };
            changes.style.flexGrow = 1; changes.style.minHeight = 100;
            changes.selectionChanged += selection => { if (selection.FirstOrDefault() is ProjectCsvChange change) detail.SetValueWithoutNotify(Describe(change)); }; Add(changes); Add(detail);
            var bottom = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            apply = new Button(() => ApplyPreview()) { text = "Apply and save", name = "csvApply" }; LanguagePickerStyle.Primary(apply); apply.SetEnabled(false); bottom.Add(apply);
            bottom.Add(new Button(ClearPreview) { text = "Cancel preview", name = "csvCancel" }); Add(bottom);
            RegisterCallback<DetachFromPanelEvent>(_ => Dispose());
        }
        public bool PreviewCsv(string csv)
        {
            ClearPreview();
            try
            {
                plan = ProjectCsv.Preview(config, csv, channelId, setId, clearEmpty.value ? CsvEmptyCellPolicy.Clear : CsvEmptyCellPolicy.Skip);
                summary.text = "Rows: " + plan.RowCount + " · Changes: " + plan.Changes.Count + " · Empty cells skipped: " + plan.SkippedEmptyCells
                    + (plan.NewLanguages.Count > 0 ? "\nNew languages: " + string.Join(", ", plan.NewLanguages) : "");
                shared.text = string.Join("\n", plan.Warnings); shared.style.display = plan.Warnings.Count == 0 ? DisplayStyle.None : DisplayStyle.Flex;
                changes.itemsSource = plan.Changes.ToList(); changes.Rebuild(); apply.SetEnabled(plan.Changes.Count > 0); ShowError(null); return true;
            }
            catch (Exception failure) { ShowError(failure.Message); return false; }
        }
        public bool ApplyPreview()
        {
            if (plan == null) { ShowError("Prepare an import preview first."); return false; }
            try
            {
                LocalizationProjectUndo.Record(plan.Apply(), "Import localization CSV"); ClearPreview(); summary.text = "Import saved. Undo restores translations, notes and configuration together."; changed?.Invoke(); return true;
            }
            catch (Exception failure) { ShowError(failure.Message + " Resolve the conflict and prepare a new preview."); apply.SetEnabled(false); return false; }
        }
        public bool ExportFile(string path)
        {
            try
            {
                string csv = ProjectCsv.Export(config, channelId, setId, includeSources.value);
                using var transaction = new EditorFileTransaction(); transaction.Add(path, EditorFileTransaction.Read(path), new UTF8Encoding(false, true).GetBytes(csv)); transaction.Execute(); ShowError(null); return true;
            }
            catch (Exception failure) { ShowError(failure.Message); return false; }
        }
        public bool CanLeave()
        {
            if (!HasPendingChanges) return true;
            if (!EditorUtility.DisplayDialog("Unapplied CSV preview", "Discard this preview? No import files have been changed.", "Discard", "Cancel")) return false;
            ClearPreview(); return true;
        }
        public void ClearPreview()
        {
            plan?.Dispose(); plan = null; changes.itemsSource = new List<ProjectCsvChange>(); changes.Rebuild(); detail.SetValueWithoutNotify("");
            summary.text = "No import preview."; shared.style.display = DisplayStyle.None; apply.SetEnabled(false); ShowError(null);
        }
        private string Describe(ProjectCsvChange change)
        {
            string set = config.Sets.FirstOrDefault(s => s.Id == change.SetId)?.Name;
            return change.Kind + " · " + (set ?? "") + " · " + (change.Key ?? "") + " · " + (change.Language ?? "") + "\nBefore: " + (change.Before ?? "(missing)")
                + "\nAfter: " + (change.After ?? "(missing)") + (change.EntryId == null ? "" : "\nEntry ID: " + change.EntryId);
        }
        private void ShowError(string error) { status.text = error ?? ""; status.style.display = string.IsNullOrEmpty(error) ? DisplayStyle.None : DisplayStyle.Flex; }
        public void Dispose() => ClearPreview();
    }
}
