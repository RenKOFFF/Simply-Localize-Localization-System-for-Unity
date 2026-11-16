using System;
using System.Collections.Generic;
using System.Linq;
using SimplyLocalize.Editor.Data;
using UnityEngine.UIElements;

namespace SimplyLocalize.Editor.Windows
{
    internal sealed class SetupLanguagePicker : VisualElement
    {
        private readonly List<LanguageDefinition> _selected = new List<LanguageDefinition>();
        private readonly VisualElement _chosen = new VisualElement { name = "selectedLanguages" };
        private readonly DropdownField _default = new DropdownField("Default language") { name = "setupDefault" };
        private readonly DropdownField _fallback = new DropdownField("Fallback language") { name = "setupFallback" };
        private readonly ListView _results;
        private readonly HelpBox _message = new HelpBox("", HelpBoxMessageType.Error) { name = "languagePickerMessage" };
        private readonly Label _count = new Label();
        internal event Action Changed;
        internal IReadOnlyList<LanguageDefinition> Selected => _selected;
        internal string DefaultCode => _default.value;
        internal string FallbackCode => string.IsNullOrEmpty(_fallback.value) ? null : _fallback.value;

        internal SetupLanguagePicker()
        {
            name = "setupLanguagePicker";
            var search = new TextField("Find a language") { name = "languageSearch", tooltip = "Search by code, English, Russian or native name: en, Ан, ru, Рум, français." };
            search.style.marginTop = 12;
            Add(search);
            _results = new ListView { name = "languageResults", fixedItemHeight = 40, selectionType = SelectionType.Single };
            _results.style.height = 165;
            _results.makeItem = () => new Label { style = { whiteSpace = WhiteSpace.Normal, paddingLeft = 8, paddingTop = 3 } };
            _results.bindItem = (element, index) =>
            {
                var entry = LanguageCatalog.Find((string)_results.itemsSource[index]);
                var label = (Label)element;
                bool selected = _selected.Any(e => e.Code == entry.Code);
                label.text = (selected ? "✓ " : "") + entry.Label + "\n" + entry.RussianName + " · " + entry.NativeName;
                label.tooltip = label.text;
            };
            Add(_results);
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
            var add = new Button(() => AddResult()) { name = "addSelectedLanguage", text = "Add selected language" };
            row.Add(add);
            _count.style.marginLeft = 12;
            row.Add(_count);
            Add(row);
            void Search()
            {
                var matches = LanguageCatalog.Search(search.value);
                _results.itemsSource = matches;
                _results.Rebuild();
                _results.selectedIndex = matches.Count > 0 ? 0 : -1;
                if (matches.Count > 0) _results.ScrollToItem(0);
                add.SetEnabled(matches.Count > 0);
                _count.text = matches.Count + " matches";
            }
            search.RegisterValueChangedCallback(_ => Search());
            search.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode != UnityEngine.KeyCode.Return && evt.keyCode != UnityEngine.KeyCode.KeypadEnter) return;
                AddResult();
                evt.StopPropagation();
            });
            Add(new Label("Selected languages") { style = { marginTop = 12, unityFontStyleAndWeight = UnityEngine.FontStyle.Bold } });
            _chosen.style.flexDirection = FlexDirection.Row;
            _chosen.style.flexWrap = Wrap.Wrap;
            Add(_chosen);
            Add(_default);
            Add(_fallback);
            _default.formatListItemCallback = FormatChoice;
            _default.formatSelectedValueCallback = FormatChoice;
            _fallback.formatListItemCallback = FormatChoice;
            _fallback.formatSelectedValueCallback = FormatChoice;
            _fallback.tooltip = "Used when a translation is missing. (none) disables the global fallback.";

            var custom = new Foldout { name = "customLanguage", text = "Language not listed? Add a custom language", value = false };
            var customCode = new TextField("Language code") { name = "customLanguageCode", tooltip = "Language[-Script][-REGION][-variant], or x-private-name. Example: pt-BR, zh-Hans, x-elvish." };
            var customName = new TextField("Display name") { name = "customLanguageName" };
            custom.Add(customCode);
            custom.Add(customName);
            custom.Add(new Label("Codes use Latin letters and hyphens: en, pt-BR, zh-Hans. Unregistered/private languages: x-my-language.") { style = { whiteSpace = WhiteSpace.Normal } });
            custom.Add(new Button(() =>
            {
                try
                {
                    var entry = LanguageCatalog.Custom(customCode.value, customName.value);
                    customCode.SetValueWithoutNotify(entry.Code);
                    Select(entry);
                }
                catch (Exception error) { _message.text = error.Message; _message.style.display = DisplayStyle.Flex; }
            }) { name = "addCustomLanguage", text = "Add language" });
            Add(custom);
            _message.style.display = DisplayStyle.None;
            Add(_message);
            Select(LanguageCatalog.Find("en"));
            Select(LanguageCatalog.Find("ru"));
            _fallback.SetValueWithoutNotify("en");
            Search();
        }

        private string FormatChoice(string code) => string.IsNullOrEmpty(code) ? "(none)" : _selected.FirstOrDefault(e => e.Code == code)?.Label ?? code;

        private void AddResult()
        {
            if (_results.selectedItem is string code) Select(LanguageCatalog.Find(code));
        }

        private void Select(LanguageDefinition entry)
        {
            if (_selected.All(e => !string.Equals(e.Code, entry.Code, StringComparison.OrdinalIgnoreCase))) _selected.Add(entry);
            _message.style.display = DisplayStyle.None;
            RefreshSelection();
        }

        private void RefreshSelection()
        {
            _chosen.Clear();
            foreach (var entry in _selected)
            {
                var remove = new Button(() => { _selected.Remove(entry); RefreshSelection(); })
                {
                    name = "removeLanguage_" + entry.Code,
                    text = entry.Label + "  ×",
                    tooltip = "Remove from setup selection"
                };
                _chosen.Add(remove);
            }
            string previousDefault = _default.value;
            string previousFallback = _fallback.value;
            _default.choices = _selected.Select(e => e.Code).ToList();
            _default.SetValueWithoutNotify(_default.choices.Contains(previousDefault) ? previousDefault : _default.choices.FirstOrDefault() ?? "");
            _default.SetEnabled(_selected.Count > 0);
            _fallback.choices = new[] { "" }.Concat(_selected.Select(e => e.Code)).ToList();
            _fallback.SetValueWithoutNotify(_fallback.choices.Contains(previousFallback ?? "") ? previousFallback ?? "" : "");
            _results.RefreshItems();
            Changed?.Invoke();
        }
    }
}
