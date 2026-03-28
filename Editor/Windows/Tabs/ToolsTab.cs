using System.Linq;
using SimplyLocalize.Editor.Data;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace SimplyLocalize.Editor.Windows.Tabs
{
    public class ToolsTab : IEditorTab
    {
        private readonly EditorLocalizationData _data;
        private readonly LocalizationConfig _config;

        public ToolsTab(EditorLocalizationData data, LocalizationConfig config)
        {
            _data = data;
            _config = config;
        }

        public void Build(VisualElement container)
        {
            var root = new VisualElement();
            root.style.paddingTop = 12;
            root.style.paddingLeft = 16;
            root.style.paddingRight = 16;

            // CSV section
            var csvTitle = new Label("CSV Import / Export");
            csvTitle.style.fontSize = 14;
            csvTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            csvTitle.style.marginBottom = 8;
            root.Add(csvTitle);

            var csvDesc = new Label(
                "Export all translations to a CSV file for editing in Google Sheets, Excel, etc.\n" +
                "Import merges CSV data back into your JSON files.");
            csvDesc.style.fontSize = 12;
            csvDesc.style.color = new Color(0.5f, 0.5f, 0.5f);
            csvDesc.style.whiteSpace = WhiteSpace.Normal;
            csvDesc.style.marginBottom = 12;
            root.Add(csvDesc);

            var btnRow = new VisualElement();
            btnRow.style.flexDirection = FlexDirection.Row;

            var exportBtn = new Button(() => OnExportCsv());
            exportBtn.text = "Export to CSV";
            exportBtn.style.fontSize = 12;
            exportBtn.style.marginRight = 8;
            btnRow.Add(exportBtn);

            var importBtn = new Button(() => OnImportCsv());
            importBtn.text = "Import from CSV";
            importBtn.style.fontSize = 12;
            btnRow.Add(importBtn);

            root.Add(btnRow);

            // Separator
            var sep = new VisualElement();
            sep.style.height = 1;
            sep.style.backgroundColor = new Color(0, 0, 0, 0.1f);
            sep.style.marginTop = 20;
            sep.style.marginBottom = 20;
            root.Add(sep);

            // Bulk operations
            var bulkTitle = new Label("Bulk operations");
            bulkTitle.style.fontSize = 14;
            bulkTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            bulkTitle.style.marginBottom = 8;
            root.Add(bulkTitle);

            var saveAllBtn = new Button(() =>
            {
                _data.SaveAll();
                AssetDatabase.Refresh();
                EditorUtility.DisplayDialog("Saved", "All translation files have been saved.", "OK");
            });
            saveAllBtn.text = "Save all files";
            saveAllBtn.style.fontSize = 12;
            saveAllBtn.style.marginBottom = 4;
            root.Add(saveAllBtn);

            var reloadBtn = new Button(() =>
            {
                _data.Reload(_config);
                EditorUtility.DisplayDialog("Reloaded", "All data reloaded from disk.", "OK");
            });
            reloadBtn.text = "Reload from disk";
            reloadBtn.style.fontSize = 12;
            root.Add(reloadBtn);

            container.Add(root);
        }

        private void OnExportCsv()
        {
            string path = EditorUtility.SaveFilePanel("Export CSV", "", "translations.csv", "csv");
            if (string.IsNullOrEmpty(path)) return;

            var codes = _config.languages
                .Where(p => p != null)
                .Select(p => p.Code)
                .ToList();

            CsvExporter.ExportToFile(_data, codes, path);
            EditorUtility.DisplayDialog("Exported",
                $"Exported {_data.KeyCount} keys to:\n{path}", "OK");
        }

        private void OnImportCsv()
        {
            string path = EditorUtility.OpenFilePanel("Import CSV", "", "csv");
            if (string.IsNullOrEmpty(path)) return;

            int updated = CsvExporter.ImportFromFile(_data, path);
            _data.SaveAll();
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog("Imported",
                $"Imported/updated {updated} keys from CSV.", "OK");
        }
    }
}
