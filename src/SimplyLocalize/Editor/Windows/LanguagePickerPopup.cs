using System;
using System.Linq;
using SimplyLocalize.Editor.Data;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace SimplyLocalize.Editor.Windows
{
    internal sealed class LanguagePickerPopup : EditorWindow
    {
        private Func<string, bool> _isSelected;
        private Action<LanguageDefinition> _add;
        private ListView _results;
        private TextField _search;
        private HelpBox _message;
        private Button _addButton;

        internal static Button CreateButton(EditorWindow owner, Func<string, bool> isSelected, Action<LanguageDefinition> add)
        {
            var button = new Button { name = "openLanguagePicker", text = "+ Add language" };
            LanguagePickerStyle.Primary(button);
            button.clicked += () =>
            {
                var popup = CreateInstance<LanguagePickerPopup>();
                popup._isSelected = isSelected;
                popup._add = add;
                popup.Build();
                Rect anchor = button.worldBound;
                anchor.position += owner.position.position;
                if (float.IsNaN(anchor.x) || float.IsNaN(anchor.y) || float.IsNaN(anchor.width) || float.IsNaN(anchor.height) || anchor.width <= 0 || anchor.height <= 0)
                    anchor = new Rect(owner.position.x + 16, owner.position.y + 60, 180, 28);
                anchor.x = Mathf.Max(owner.position.x, anchor.xMax - 440);
                anchor.width = 440;
                try { popup.ShowAsDropDown(anchor, new Vector2(440, 470)); }
                catch { DestroyImmediate(popup); throw; }
                popup._search.Focus();
            };
            return button;
        }

        private void Build()
        {
            var root = rootVisualElement;
            root.name = "languagePickerPopup";
            root.style.paddingLeft = root.style.paddingRight = 14;
            root.style.paddingTop = root.style.paddingBottom = 12;
            var header = new VisualElement { style = { flexDirection = FlexDirection.Row, justifyContent = Justify.SpaceBetween, alignItems = Align.Center, marginBottom = 10 } };
            header.Add(LanguagePickerStyle.Heading("Add language"));
            header.Add(new Button(Close) { name = "closeLanguagePicker", text = "×", tooltip = "Close" });
            root.Add(header);

            var catalog = new VisualElement { style = { flexGrow = 1 } };
            _search = new TextField("Search") { name = "languageSearch", tooltip = "Name or code: English, Ан, ru, français." };
            _search.labelElement.style.minWidth = 50;
            _search.labelElement.style.width = 50;
            catalog.Add(_search);
            _results = new ListView { name = "languageResults", fixedItemHeight = 38, selectionType = SelectionType.Single };
            _results.style.flexGrow = 1;
            _results.style.marginTop = 8;
            _results.makeItem = () =>
            {
                var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, paddingLeft = 8, paddingRight = 8, height = 38 } };
                row.Add(new Label { name = "languageName", style = { flexGrow = 1, overflow = Overflow.Hidden } });
                var code = new Label { name = "languageCode" };
                LanguagePickerStyle.Badge(code);
                row.Add(code);
                return row;
            };
            _results.bindItem = (row, index) =>
            {
                var entry = LanguageCatalog.Find((string)_results.itemsSource[index]);
                bool selected = _isSelected(entry.Code);
                bool cyrillic = _search.value.Any(c => c >= '\u0400' && c <= '\u04ff');
                string name = cyrillic && !string.IsNullOrEmpty(entry.RussianName) ? entry.RussianName : entry.EnglishName;
                if (name.Length > 0) name = char.ToUpperInvariant(name[0]) + name.Substring(1);
                row.Q<Label>("languageName").text = (selected ? "✓  " : "") + name;
                row.Q<Label>("languageCode").text = entry.Code;
                row.tooltip = entry.EnglishName + " · " + entry.RussianName + " · " + entry.NativeName;
            };
            catalog.Add(_results);
            var empty = new Label("No languages found") { name = "noLanguageMatches", style = { marginTop = 8 } };
            catalog.Add(empty);
            _addButton = new Button(AddResult) { name = "addSelectedLanguage", text = "Add language" };
            LanguagePickerStyle.Primary(_addButton);
            _addButton.style.marginTop = 8;
            catalog.Add(_addButton);
            void Search()
            {
                var matches = LanguageCatalog.Search(_search.value);
                _results.itemsSource = matches;
                _results.Rebuild();
                _results.selectedIndex = matches.Count > 0 ? 0 : -1;
                if (matches.Count > 0) _results.ScrollToItem(0);
                empty.style.display = matches.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
                UpdateButton();
            }
            _search.RegisterValueChangedCallback(_ => Search());
            _results.RegisterSelectionChanged(_ => UpdateButton());
            _results.RegisterItemsChosen(_ => AddResult());
            _search.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.DownArrow && _results.itemsSource.Count > 0) { _results.Focus(); evt.StopPropagation(); }
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter) { AddResult(); evt.StopPropagation(); }
            });
            root.RegisterCallback<KeyDownEvent>(evt => { if (evt.keyCode == KeyCode.Escape) { Close(); evt.StopPropagation(); } });
            root.Add(catalog);

            var custom = new VisualElement { name = "customLanguage", style = { display = DisplayStyle.None, flexGrow = 1 } };
            custom.Add(new Button(() => { custom.style.display = DisplayStyle.None; catalog.style.display = DisplayStyle.Flex; _message.style.display = DisplayStyle.None; _search.Focus(); }) { name = "backToLanguages", text = "‹ Languages" });
            var customCode = new TextField("Code") { name = "customLanguageCode", tooltip = "Examples: pt-BR, zh-Hans, x-elvish. Standard codes are normalized automatically." };
            var customName = new TextField("Display name") { name = "customLanguageName" };
            customCode.style.marginTop = 20;
            custom.Add(customCode);
            custom.Add(customName);
            custom.Add(new Label("Examples: pt-BR, zh-Hans, x-elvish") { style = { fontSize = 11, marginTop = 8, marginBottom = 16 } });
            var customAdd = new Button(() =>
            {
                try
                {
                    var entry = LanguageCatalog.Custom(customCode.value, customName.value);
                    customCode.SetValueWithoutNotify(entry.Code);
                    AddEntry(entry);
                }
                catch (Exception error) { ShowError(error); }
            }) { name = "addCustomLanguage", text = "Add language" };
            LanguagePickerStyle.Primary(customAdd);
            custom.Add(customAdd);
            root.Add(custom);
            var customButton = new Button(() => { catalog.style.display = DisplayStyle.None; custom.style.display = DisplayStyle.Flex; _message.style.display = DisplayStyle.None; customCode.Focus(); }) { name = "openCustomLanguage", text = "Add a custom language" };
            customButton.style.marginTop = 12;
            catalog.Add(customButton);
            _message = new HelpBox("", HelpBoxMessageType.Error) { name = "languagePickerMessage", style = { display = DisplayStyle.None, marginTop = 8 } };
            root.Add(_message);
            Search();
        }

        private void UpdateButton()
        {
            string code = _results.selectedItem as string;
            bool selected = code != null && _isSelected(code);
            _addButton.text = selected ? "Already added" : "Add language";
            _addButton.SetEnabled(code != null && !selected);
        }

        private void AddResult()
        {
            if (_results.selectedItem is string code && !_isSelected(code)) AddEntry(LanguageCatalog.Find(code));
        }

        private void AddEntry(LanguageDefinition entry)
        {
            try
            {
                if (!_isSelected(entry.Code)) _add(entry);
                _message.messageType = HelpBoxMessageType.Info;
                _message.text = entry.Label + " added";
                _message.style.display = DisplayStyle.Flex;
                _results.RefreshItems();
                UpdateButton();
            }
            catch (Exception error) { ShowError(error); }
        }

        private void ShowError(Exception error)
        {
            _message.messageType = HelpBoxMessageType.Error;
            _message.text = error.Message;
            _message.style.display = DisplayStyle.Flex;
        }
    }

    internal static class LanguagePickerStyle
    {
        internal static void Primary(Button button)
        {
            button.style.backgroundColor = new Color(0.17f, 0.39f, 0.66f);
            button.style.color = Color.white;
            button.style.height = 28;
            button.style.paddingLeft = button.style.paddingRight = 12;
            button.style.borderTopWidth = button.style.borderBottomWidth = button.style.borderLeftWidth = button.style.borderRightWidth = 0;
            button.style.borderTopLeftRadius = button.style.borderTopRightRadius = button.style.borderBottomLeftRadius = button.style.borderBottomRightRadius = 4;
        }

        internal static Label Heading(string text) => new Label(text) { style = { fontSize = 13, unityFontStyleAndWeight = FontStyle.Bold } };

        internal static void Badge(VisualElement element)
        {
            element.style.backgroundColor = EditorGUIUtility.isProSkin ? new Color(0.18f, 0.28f, 0.39f) : new Color(0.81f, 0.88f, 0.96f);
            element.style.color = EditorGUIUtility.isProSkin ? new Color(0.75f, 0.86f, 1f) : new Color(0.14f, 0.28f, 0.46f);
            element.style.paddingLeft = element.style.paddingRight = 8;
            element.style.paddingTop = element.style.paddingBottom = 4;
            element.style.borderTopLeftRadius = element.style.borderTopRightRadius = element.style.borderBottomLeftRadius = element.style.borderBottomRightRadius = 4;
        }

        internal static void Card(VisualElement element)
        {
            element.style.backgroundColor = EditorGUIUtility.isProSkin ? new Color(0.19f, 0.19f, 0.19f) : new Color(0.89f, 0.89f, 0.89f);
            element.style.paddingTop = element.style.paddingBottom = 16;
            element.style.paddingLeft = element.style.paddingRight = 16;
            element.style.marginTop = 12;
            element.style.borderTopLeftRadius = element.style.borderTopRightRadius = element.style.borderBottomLeftRadius = element.style.borderBottomRightRadius = 6;
        }
    }
}
