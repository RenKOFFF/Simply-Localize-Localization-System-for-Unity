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
        internal event Action Changed;
        internal IReadOnlyList<LanguageDefinition> Selected => _selected;
        internal string DefaultCode => _default.value;
        internal string FallbackCode => string.IsNullOrEmpty(_fallback.value) ? null : _fallback.value;

        internal SetupLanguagePicker(UnityEditor.EditorWindow window)
        {
            name = "setupLanguagePicker";
            var languages = new VisualElement();
            LanguagePickerStyle.Card(languages);
            var header = new VisualElement { style = { flexDirection = FlexDirection.Row, justifyContent = Justify.SpaceBetween, alignItems = Align.Center, marginBottom = 12 } };
            header.Add(LanguagePickerStyle.Heading("Languages"));
            header.Add(LanguagePickerPopup.CreateButton(window, code => _selected.Any(e => string.Equals(e.Code, code, StringComparison.OrdinalIgnoreCase)), Select));
            languages.Add(header);
            _chosen.style.flexDirection = FlexDirection.Row;
            _chosen.style.flexWrap = Wrap.Wrap;
            languages.Add(_chosen);
            Add(languages);
            var defaults = new VisualElement();
            LanguagePickerStyle.Card(defaults);
            var title = LanguagePickerStyle.Heading("Language defaults");
            title.style.marginBottom = 12;
            defaults.Add(title);
            defaults.Add(_default);
            defaults.Add(_fallback);
            _default.tooltip = "Language used when the game starts.";
            _fallback.tooltip = "Used when a translation is missing. (none) disables global fallback.";
            _default.formatListItemCallback = FormatChoice;
            _default.formatSelectedValueCallback = FormatChoice;
            _fallback.formatListItemCallback = FormatChoice;
            _fallback.formatSelectedValueCallback = FormatChoice;
            Add(defaults);
            Select(LanguageCatalog.Find("en"));
            Select(LanguageCatalog.Find("ru"));
            _fallback.SetValueWithoutNotify("en");
        }

        private string FormatChoice(string code) => string.IsNullOrEmpty(code) ? "(none)" : _selected.FirstOrDefault(e => e.Code == code)?.Label ?? code;

        private void Select(LanguageDefinition entry)
        {
            if (_selected.All(e => !string.Equals(e.Code, entry.Code, StringComparison.OrdinalIgnoreCase))) _selected.Add(entry);
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
                LanguagePickerStyle.Badge(remove);
                remove.style.marginRight = 6;
                remove.style.marginBottom = 6;
                _chosen.Add(remove);
            }
            if (_selected.Count == 0) _chosen.Add(new Label("No languages selected"));
            string previousDefault = _default.value;
            string previousFallback = _fallback.value;
            _default.choices = _selected.Select(e => e.Code).ToList();
            _default.SetValueWithoutNotify(_default.choices.Contains(previousDefault) ? previousDefault : _default.choices.FirstOrDefault() ?? "");
            _default.SetEnabled(_selected.Count > 0);
            _fallback.choices = new[] { "" }.Concat(_selected.Select(e => e.Code)).ToList();
            _fallback.SetValueWithoutNotify(_fallback.choices.Contains(previousFallback ?? "") ? previousFallback ?? "" : "");
            Changed?.Invoke();
        }
    }
}
