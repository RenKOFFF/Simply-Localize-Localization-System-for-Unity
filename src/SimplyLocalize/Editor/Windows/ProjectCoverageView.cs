using System;
using System.Collections.Generic;
using SimplyLocalize.Editor.Data;
using SimplyLocalize.Editor.Utilities;
using SimplyLocalize.Pluralization;
using UnityEngine.UIElements;

namespace SimplyLocalize.Editor.Windows
{
    public sealed class ProjectCoverageView : ScrollView, IDisposable
    {
        private readonly LocalizationProjectConfig config;
        private readonly string channelId, setId;
        private readonly ILocalizationEditorTextReader textReader;
        private readonly ILocalizationEditorAssetReader assetReader;
        private readonly LocalizationPluralRules pluralRules;
        private readonly VisualElement rows;
        private readonly ListView warnings;
        private readonly HelpBox status;
        public CoverageReport Report { get; private set; }
        public ProjectCoverageView(LocalizationProjectConfig config, string channelId = null, string setId = null,
            ILocalizationEditorTextReader textReader = null, ILocalizationEditorAssetReader assetReader = null, LocalizationPluralRules pluralRules = null)
        {
            this.config = config; this.channelId = channelId; this.setId = setId; this.textReader = textReader; this.assetReader = assetReader; this.pluralRules = pluralRules;
            name = "projectCoverage"; style.flexGrow = 1; style.paddingLeft = style.paddingRight = 16; style.paddingTop = 12;
            Add(new Button(Reload) { text = "Check saved data", name = "refreshCoverage" });
            status = new HelpBox("", HelpBoxMessageType.Info) { name = "coverageStatus" }; Add(status);
            rows = new VisualElement { name = "coverageRows" }; Add(rows);
            warnings = new ListView { name = "coverageWarnings", virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight, fixedItemHeight = 36,
                makeItem = () => new Label { style = { whiteSpace = WhiteSpace.Normal, paddingTop = 6, paddingBottom = 6 } },
                bindItem = (element, index) => ((Label)element).text = Report.Warnings[index].Level + " · " + Report.Warnings[index].Message };
            warnings.style.height = 220; Add(warnings);
            foreach (var child in contentContainer.Children()) child.style.flexShrink = 0;
            RegisterCallback<AttachToPanelEvent>(_ => LocalizationProjectUndo.Changed += Reload);
            RegisterCallback<DetachFromPanelEvent>(_ => Dispose()); Reload();
        }
        public void Reload()
        {
            Report = ProjectCoverageAnalyzer.Analyze(config, channelId, setId, textReader, assetReader, pluralRules);
            status.text = Report.IsComplete ? "Saved data checked. Direct and fallback coverage are separate; whitespace counts as a runtime value. Unsaved edits are not included."
                : "Check has errors. Unreadable sections have no totals; resolve the errors below before relying on coverage.";
            status.messageType = Report.IsComplete ? HelpBoxMessageType.Info : HelpBoxMessageType.Error;
            rows.Clear(); AddRows("Text", Report.TextCoverage); AddRows("Assets · any type", Report.AssetCoverage);
            warnings.itemsSource = Report.Warnings; warnings.Rebuild();
        }
        private void AddRows(string title, IEnumerable<LanguageCoverage> coverages)
        {
            rows.Add(LanguagePickerStyle.Heading(title));
            foreach (var row in coverages)
            {
                var card = new VisualElement(); LanguagePickerStyle.Card(card);
                card.Add(new Label(row.DisplayName + " · " + row.LanguageCode));
                card.Add(new Label(row.TotalCount == 0 ? "No registered entries" : "Direct " + row.TranslatedCount + "/" + row.TotalCount + " · With fallback " + row.AvailableCount + "/" + row.TotalCount)
                    { name = "coverage_" + title + "_" + row.LanguageCode, style = { whiteSpace = WhiteSpace.Normal } });
                card.Add(new Label("Present " + row.PresentCount + " · Empty " + row.EmptyKeys.Count + " · Whitespace " + row.WhitespaceKeys.Count + " · Unavailable " + row.UnavailableKeys.Count)
                    { style = { whiteSpace = WhiteSpace.Normal } }); rows.Add(card);
            }
        }
        public void Dispose() => LocalizationProjectUndo.Changed -= Reload;
    }
}
