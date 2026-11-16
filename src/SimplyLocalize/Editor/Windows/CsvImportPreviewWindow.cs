using System;
using System.Linq;
using SimplyLocalize.Editor.Data;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace SimplyLocalize.Editor.Windows
{
    public sealed class CsvImportPreviewWindow : EditorWindow
    {
        private EditorLocalizationData _data;
        private LocalizationConfig _config;
        private CsvImportPlan _plan;
        private CsvEmptyCellPolicy _policy;

        internal static void Open(EditorLocalizationData data, LocalizationConfig config, CsvImportPlan plan, CsvEmptyCellPolicy policy)
        {
            var window = CreateInstance<CsvImportPreviewWindow>();
            window._data = data; window._config = config; window._plan = plan; window._policy = policy;
            window.titleContent = new GUIContent("CSV import preview");
            window.minSize = new Vector2(640, 440);
            window.ShowUtility();
            window.CreateGUI();
        }

        public void CreateGUI()
        {
            var root = rootVisualElement;
            root.Clear(); root.style.paddingLeft = root.style.paddingRight = 12;
            root.style.paddingTop = root.style.paddingBottom = 12;
            if (_plan == null)
            {
                root.Add(new Label("The preview expired after a script reload. Reopen CSV import to validate it again."));
                return;
            }
            var summary = new Label($"Rows: {_plan.RowCount}   Changes: {_plan.Changes.Count}   Empty cells: {_policy}   Skipped: {_plan.SkippedEmptyCells}\n"
                + (_plan.NewLanguages.Count == 0 ? "No new languages." : "New profiles: " + string.Join(", ", _plan.NewLanguages)));
            summary.enableRichText = false; summary.style.whiteSpace = WhiteSpace.Normal;
            root.Add(summary);
            root.Add(new Label("Import also saves current pending editor changes. Cancel leaves files and data untouched."));
            var details = new TextField("Selected change") { multiline = true, isReadOnly = true };
            details.style.height = 130;
            var changes = _plan.Changes.ToList();
            var list = new ListView { itemsSource = changes, fixedItemHeight = 64 };
            list.style.flexGrow = 1;
            list.bindItem = (element, index) =>
            {
                var button = (Button)element;
                button.userData = changes[index];
                button.text = Describe(changes[index]);
                button.enableRichText = false;
                button.tooltip = button.text;
                button.style.unityTextAlign = TextAnchor.MiddleLeft;
                button.style.whiteSpace = WhiteSpace.Normal;
            };
            list.makeItem = () =>
            {
                var button = new Button();
                button.clicked += () => details.SetValueWithoutNotify(Describe((CsvImportChange)button.userData));
                return button;
            };
            root.Add(list); root.Add(details);
            var status = new Label(); status.enableRichText = false; status.style.whiteSpace = WhiteSpace.Normal;
            root.Add(status);
            var buttons = new VisualElement(); buttons.style.flexDirection = FlexDirection.Row;
            var apply = new Button(); apply.text = "Apply and save";
            apply.SetEnabled(changes.Count > 0);
            apply.clicked += () =>
            {
                apply.SetEnabled(false);
                try
                {
                    _plan.ApplyAndSave(_data, _config);
                    _data.FlushPendingAssetRefresh();
                    EditorDataCache.Invalidate();
                    EditorUtility.DisplayDialog("Imported", $"Imported {_plan.RowCount} rows; applied {changes.Count} changes.", "OK");
                    Close();
                }
                catch (Exception error)
                {
                    status.text = "Import failed: " + error.Message + "\nClose and create a new preview after resolving the error.";
                }
            };
            buttons.Add(apply);
            buttons.Add(new Button(Close) { text = "Cancel" });
            root.Add(buttons);
        }

        private static string Describe(CsvImportChange change) =>
            $"{change.Kind}: {change.Key ?? "(language)"} {change.Language}\nBefore: {change.Before ?? "(missing)"}\nAfter: {change.After ?? "(missing)"}";
    }
}
