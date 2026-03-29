using System.Collections.Generic;
using System.IO;
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

            var csvTitle = new Label("CSV Import / Export");
            csvTitle.style.fontSize = 14;
            csvTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            csvTitle.style.marginBottom = 8;
            root.Add(csvTitle);

            var csvDesc = new Label(
                "Export all translations to a CSV file for editing in Google Sheets, Excel, etc.\n" +
                "Import merges CSV data back into your JSON files.\n" +
                "New languages found in CSV will get profiles created automatically.");
            csvDesc.style.fontSize = 12;
            csvDesc.style.color = new Color(0.5f, 0.5f, 0.5f);
            csvDesc.style.whiteSpace = WhiteSpace.Normal;
            csvDesc.style.marginBottom = 12;
            root.Add(csvDesc);

            var btnRow = new VisualElement();
            btnRow.style.flexDirection = FlexDirection.Row;

            var exportBtn = new Button(OnExportCsv);
            exportBtn.text = "Export to CSV";
            exportBtn.style.fontSize = 12;
            exportBtn.style.marginRight = 8;
            btnRow.Add(exportBtn);

            var importBtn = new Button(OnImportCsv);
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

            string csv = File.ReadAllText(path, System.Text.Encoding.UTF8);

            // Parse header to detect new languages
            var newLanguages = DetectNewLanguages(csv);

            if (newLanguages.Count > 0)
            {
                string langList = string.Join(", ", newLanguages);
                bool proceed = EditorUtility.DisplayDialog(
                    "New languages detected",
                    $"The CSV contains languages not in your config:\n{langList}\n\n" +
                    "Profiles will be created automatically. " +
                    "Please review them after import (set fonts, SystemLanguage, etc.).",
                    "Import & create profiles", "Cancel");

                if (!proceed) return;

                // Create profiles for new languages
                foreach (var langCode in newLanguages)
                    CreateProfileForLanguage(langCode);
            }

            int updated = CsvExporter.ImportFromFile(_data, path);

            // Save all — including any new files that were auto-created
            _data.SaveAll();
            AssetDatabase.Refresh();

            string message = $"Imported/updated {updated} keys.";

            if (newLanguages.Count > 0)
                message += $"\nCreated {newLanguages.Count} new language profile(s): {string.Join(", ", newLanguages)}";

            EditorUtility.DisplayDialog("Imported", message, "OK");
        }

        private List<string> DetectNewLanguages(string csv)
        {
            var newLangs = new List<string>();

            // Read first line to get headers
            int lineEnd = csv.IndexOf('\n');
            if (lineEnd < 0) return newLangs;

            string headerLine = csv.Substring(0, lineEnd).TrimEnd('\r');
            string[] headers = headerLine.Split(',');

            // headers[0] = Key, headers[1] = File, headers[2..] = language codes
            var existingCodes = new HashSet<string>(
                _config.languages.Where(p => p != null).Select(p => p.Code));

            for (int i = 2; i < headers.Length; i++)
            {
                string code = headers[i].Trim().Trim('"');

                if (!string.IsNullOrEmpty(code) && !existingCodes.Contains(code))
                    newLangs.Add(code);
            }

            return newLangs;
        }

        private void CreateProfileForLanguage(string langCode)
        {
            var profile = ScriptableObject.CreateInstance<LanguageProfile>();
            profile.languageCode = langCode;
            profile.displayName = langCode;
            profile.hasText = true;

            string configPath = AssetDatabase.GetAssetPath(_config);
            string configDir = Path.GetDirectoryName(configPath);
            string profilePath = Path.Combine(configDir ?? "Assets", $"Profile_{langCode}.asset");
            profilePath = AssetDatabase.GenerateUniqueAssetPath(profilePath);

            AssetDatabase.CreateAsset(profile, profilePath);

            Undo.RecordObject(_config, "Add language from CSV");
            _config.languages.Add(profile);
            EditorUtility.SetDirty(_config);

            // Create folder structure
            if (!string.IsNullOrEmpty(_data.BasePath))
            {
                string textDir = Path.Combine(_data.BasePath, langCode, "text");
                Directory.CreateDirectory(textDir);

                // Create empty JSON for all existing source files
                foreach (var fileName in _data.SourceFiles)
                {
                    string jsonPath = Path.Combine(textDir, fileName + ".json");
                    if (!File.Exists(jsonPath))
                        File.WriteAllText(jsonPath, "{\n  \"translations\": {}\n}");
                }

                // If no source files exist yet, create global.json
                if (_data.SourceFiles.Count == 0)
                {
                    string jsonPath = Path.Combine(textDir, "global.json");
                    File.WriteAllText(jsonPath, "{\n  \"translations\": {}\n}");
                }
            }

            AssetDatabase.SaveAssets();
        }
    }
}
