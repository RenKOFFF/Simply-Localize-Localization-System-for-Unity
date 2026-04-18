using System.Collections.Generic;
using SimplyLocalize.Editor.Data;
using SimplyLocalize.Editor.Utilities;
using UnityEngine;
using UnityEngine.UIElements;

namespace SimplyLocalize.Editor.Windows.Tabs
{
    public class CoverageTab : IEditorTab
    {
        private readonly EditorLocalizationData _data;
        private readonly LocalizationConfig _config;

        public CoverageTab(EditorLocalizationData data, LocalizationConfig config)
        {
            _data = data;
            _config = config;
        }

        public void Build(VisualElement container)
        {
            var root = new ScrollView();
            root.style.paddingTop = 12;
            root.style.paddingLeft = 16;
            root.style.paddingRight = 16;

            if (_config == null)
            {
                root.Add(new Label("No config assigned."));
                container.Add(root);
                return;
            }

            var coverages = CoverageAnalyzer.ComputeCoverage(_data, _config);
            var warnings = CoverageAnalyzer.ComputeWarnings(_data, _config);

            // Summary cards
            var cards = new VisualElement();
            cards.style.flexDirection = FlexDirection.Row;
            cards.style.marginBottom = 16;

            cards.Add(MakeMetricCard("Total keys", _data.KeyCount.ToString()));
            cards.Add(MakeMetricCard("Languages", _config.languages.Count.ToString()));

            int totalMissing = 0;
            foreach (var c in coverages) totalMissing += c.MissingKeys.Count;
            cards.Add(MakeMetricCard("Missing", totalMissing.ToString(),
                totalMissing > 0 ? new Color(0.8f, 0.3f, 0.3f) : (Color?)null));

            root.Add(cards);

            // Coverage bars
            var sectionTitle = new Label("Text coverage");
            sectionTitle.style.fontSize = 13;
            sectionTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            sectionTitle.style.marginBottom = 8;
            root.Add(sectionTitle);

            foreach (var coverage in coverages)
            {
                root.Add(BuildCoverageBar(coverage));
            }

            // Warnings
            if (warnings.Count > 0)
            {
                var warnTitle = new Label($"Warnings ({warnings.Count})");
                warnTitle.style.fontSize = 13;
                warnTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
                warnTitle.style.marginTop = 16;
                warnTitle.style.marginBottom = 8;
                root.Add(warnTitle);

                foreach (var warning in warnings)
                {
                    root.Add(BuildWarningRow(warning));
                }
            }

            container.Add(root);
        }

        private VisualElement BuildCoverageBar(LanguageCoverage coverage)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 6;

            var nameLabel = new Label(coverage.DisplayName);
            nameLabel.style.width = 100;
            nameLabel.style.fontSize = 12;
            row.Add(nameLabel);

            // Progress bar background
            var barBg = new VisualElement();
            barBg.style.flexGrow = 1;
            barBg.style.height = 8;
            barBg.style.backgroundColor = new Color(0, 0, 0, 0.06f);
            barBg.style.borderTopLeftRadius = 4;
            barBg.style.borderTopRightRadius = 4;
            barBg.style.borderBottomLeftRadius = 4;
            barBg.style.borderBottomRightRadius = 4;

            var barFill = new VisualElement();
            float pct = coverage.Percentage;
            barFill.style.width = new Length(pct * 100, LengthUnit.Percent);
            barFill.style.height = 8;
            barFill.style.borderTopLeftRadius = 4;
            barFill.style.borderTopRightRadius = 4;
            barFill.style.borderBottomLeftRadius = 4;
            barFill.style.borderBottomRightRadius = 4;

            if (pct >= 1f)
                barFill.style.backgroundColor = new Color(0.25f, 0.65f, 0.3f);
            else if (pct >= 0.7f)
                barFill.style.backgroundColor = new Color(0.85f, 0.65f, 0.15f);
            else
                barFill.style.backgroundColor = new Color(0.85f, 0.3f, 0.3f);

            barBg.Add(barFill);
            row.Add(barBg);

            var countLabel = new Label($"{coverage.TranslatedCount}/{coverage.TotalCount}");
            countLabel.style.width = 60;
            countLabel.style.fontSize = 11;
            countLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            countLabel.style.color = pct >= 1f
                ? new Color(0.25f, 0.6f, 0.25f)
                : new Color(0.8f, 0.5f, 0.15f);
            row.Add(countLabel);

            return row;
        }

        private VisualElement BuildWarningRow(CoverageWarning warning)
        {
            var row = new VisualElement();
            row.style.paddingTop = 6;
            row.style.paddingBottom = 6;
            row.style.paddingLeft = 10;
            row.style.paddingRight = 10;
            row.style.marginBottom = 4;
            row.style.borderTopLeftRadius = 4;
            row.style.borderTopRightRadius = 4;
            row.style.borderBottomLeftRadius = 4;
            row.style.borderBottomRightRadius = 4;

            if (warning.Level == CoverageWarning.Severity.Error)
            {
                row.style.backgroundColor = new Color(0.9f, 0.3f, 0.3f, 0.1f);
            }
            else if (warning.Level == CoverageWarning.Severity.Warning)
            {
                row.style.backgroundColor = new Color(0.9f, 0.7f, 0.2f, 0.1f);
            }
            else
            {
                row.style.backgroundColor = new Color(0, 0, 0, 0.04f);
            }

            var label = new Label(warning.Message);
            label.style.fontSize = 11;
            label.style.whiteSpace = WhiteSpace.Normal;
            row.Add(label);

            return row;
        }

        private VisualElement MakeMetricCard(string title, string value, Color? valueColor = null)
        {
            var card = new VisualElement();
            card.style.backgroundColor = new Color(0, 0, 0, 0.04f);
            card.style.borderTopLeftRadius = 6;
            card.style.borderTopRightRadius = 6;
            card.style.borderBottomLeftRadius = 6;
            card.style.borderBottomRightRadius = 6;
            card.style.paddingTop = 10;
            card.style.paddingBottom = 10;
            card.style.paddingLeft = 14;
            card.style.paddingRight = 14;
            card.style.marginRight = 8;
            card.style.minWidth = 100;

            var titleLabel = new Label(title);
            titleLabel.style.fontSize = 11;
            titleLabel.style.color = new Color(0.5f, 0.5f, 0.5f);
            titleLabel.style.marginBottom = 2;
            card.Add(titleLabel);

            var valueLabel = new Label(value);
            valueLabel.style.fontSize = 20;
            valueLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            if (valueColor.HasValue) valueLabel.style.color = valueColor.Value;
            card.Add(valueLabel);

            return card;
        }
    }
}
