using System;
using System.Linq;
using SimplyLocalize.Editor.Data;
using SimplyLocalize.Pluralization;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace SimplyLocalize.Editor.Windows
{
    /// <summary>Project language catalog and the selected channel's language policy. No game runtime mutation.</summary>
    public sealed class ProjectLanguagesView : ScrollView
    {
        private readonly LocalizationProjectConfig config;
        private readonly EditorWindow owner;
        private readonly string channelId;
        private readonly Action changed;
        private readonly Action<string> editAppearance;
        private readonly LocalizationPluralRules pluralRules;
        private string error;
        public ProjectLanguagesView(LocalizationProjectConfig config, EditorWindow owner, string channelId = null,
            Action changed = null, Action<string> editAppearance = null, LocalizationPluralRules pluralRules = null)
        {
            this.config = config != null ? config : throw new ArgumentNullException(nameof(config));
            this.owner = owner != null ? owner : throw new ArgumentNullException(nameof(owner));
            this.channelId = channelId ?? config.MainChannelId; this.changed = changed; this.editAppearance = editAppearance;
            this.pluralRules = pluralRules ?? new LocalizationPluralRules();
            name = "projectLanguages"; style.flexGrow = 1; style.paddingTop = 12; style.paddingLeft = style.paddingRight = 16;
            RegisterCallback<AttachToPanelEvent>(_ => LocalizationProjectUndo.Changed += OnHistoryChanged);
            RegisterCallback<DetachFromPanelEvent>(_ => LocalizationProjectUndo.Changed -= OnHistoryChanged);
            Refresh();
        }
        public void Refresh()
        {
            Clear(); var channel = config.Channels.SingleOrDefault(c => c.Id == channelId);
            if (channel == null) { Add(new HelpBox("The selected channel no longer exists. Select another channel.", HelpBoxMessageType.Warning)); return; }
            var heading = Row(); heading.style.justifyContent = Justify.SpaceBetween;
            heading.Add(LanguagePickerStyle.Heading("Languages"));
            heading.Add(LanguagePickerPopup.CreateButton(owner, code => config.Languages.Any(l => l.Code == code), language =>
            {
                var change = LocalizationProjectAuthoring.AddLanguage(config, language.Code, language.NativeName);
                if (change != null) LocalizationProjectUndo.Record(change, "Add localization language");
                error = null; Refresh(); changed?.Invoke();
            }));
            Add(heading);
            Add(new Label("Project languages are shared. Availability and defaults below belong to " + channel.Name + ".") { style = { whiteSpace = WhiteSpace.Normal, marginBottom = 12 } });
            if (!string.IsNullOrEmpty(error)) Add(new HelpBox(error, HelpBoxMessageType.Error) { name = "languageMessage" });
            var defaults = new VisualElement(); LanguagePickerStyle.Card(defaults);
            defaults.Add(LanguagePickerStyle.Heading(channel.Name + " defaults"));
            defaults.Add(Choice("Default language", "defaultLanguage", channel.Languages.ToArray(), channel.DefaultLanguage,
                value => Save(d => Channel(d).DefaultLanguage = value, "Change default language")));
            defaults.Add(Choice("Fallback language", "fallbackLanguage", new[] { "" }.Concat(channel.Languages).ToArray(), channel.FallbackLanguage ?? "",
                value => Save(d => Channel(d).FallbackLanguage = value, "Change fallback language")));
            if (channel.Mode == LocalizationChannelMode.FollowMain)
                defaults.Add(new HelpBox("This channel follows Main. Its default is used when Main selects a language unavailable here.", HelpBoxMessageType.Info));
            Add(defaults);
            foreach (var language in config.Languages)
            {
                string code = language.Code; bool enabled = channel.Languages.Contains(code);
                var card = new VisualElement { name = "language_" + code }; LanguagePickerStyle.Card(card);
                var header = Row(); header.style.justifyContent = Justify.SpaceBetween;
                header.Add(LanguagePickerStyle.Heading(language.DisplayName + "  ·  " + code));
                var available = new Toggle("Available in " + channel.Name) { name = "languageAvailable_" + code, value = enabled };
                available.RegisterValueChangedCallback(e => Save(d =>
                {
                    var target = Channel(d);
                    if (!e.newValue && (target.DefaultLanguage == code || target.FallbackLanguage == code || target.Fallbacks.Any(f => f.Language == code || f.Fallback == code)))
                        throw new InvalidOperationException("Change this channel's defaults and fallback rules before disabling " + code + ".");
                    target.SetLanguages(e.newValue ? target.Languages.Concat(new[] { code }) : target.Languages.Where(l => l != code));
                }, "Change channel languages"));
                header.Add(available); card.Add(header);
                var display = new TextField("Display name") { name = "languageName_" + code, value = language.DisplayName, isDelayed = true };
                display.RegisterValueChangedCallback(e => Save(d => d.Languages.Single(l => l.Code == code).DisplayName = e.newValue, "Rename language")); card.Add(display);
                var system = new EnumField("System language", language.SystemLanguage) { name = "systemLanguage_" + code };
                system.RegisterValueChangedCallback(e => Save(d => d.Languages.Single(l => l.Code == code).SystemLanguage = (SystemLanguage)e.newValue, "Change system language mapping")); card.Add(system);
                if (enabled)
                {
                    string fallback = channel.Fallbacks.FirstOrDefault(f => f.Language == code)?.Fallback ?? "";
                    card.Add(Choice("Fallback override", "fallbackOverride_" + code, new[] { "" }.Concat(channel.Languages.Where(l => l != code)).ToArray(), fallback,
                        value => Save(d => Channel(d).SetFallback(code, value), "Change language fallback")));
                }
                var info = pluralRules.GetRuleInfo(code);
                string support = info.Support == PluralRuleSupport.Default ? "General rule · review this language" : info.Support == PluralRuleSupport.Custom ? "Custom rule" : "Built-in rule";
                card.Add(new Label("Plural: " + support + " (" + info.MatchedLanguage + ")") { name = "pluralStatus_" + code });
                if (info.Support == PluralRuleSupport.Default)
                    card.Add(new HelpBox("No dedicated plural rule is registered. The language is valid, but plural text needs review or a custom rule.", HelpBoxMessageType.Warning));
                int sources = config.Sets.Where(s => channel.SetIds.Contains(s.Id)).SelectMany(s => s.Sources)
                    .Where(s => s.Language == code).Sum(s => s.Sources.Count);
                card.Add(new Label(sources == 0 ? "No sources configured for this channel yet." : sources + " source(s) configured in this channel.") { name = "languageSources_" + code });
                var actions = Row();
                if (editAppearance != null) actions.Add(new Button(() => editAppearance(code)) { name = "editAppearance_" + code, text = "Edit appearance" });
                actions.Add(new Button(() => Save(d => RemoveLanguage(d, code), "Remove localization language"))
                    { name = "removeLanguage_" + code, text = "Remove language", tooltip = "Removes definitions and source bindings. Translation files and assets stay on disk." });
                card.Add(actions); Add(card);
            }
        }
        private void OnHistoryChanged() { if (panel != null) Refresh(); }
        private LocalizationChannelDefinition Channel(LocalizationProjectConfig draft) => draft.Channels.Single(c => c.Id == channelId);
        private DropdownField Choice(string label, string name, string[] options, string selected, Action<string> change)
        {
            var field = new DropdownField(label, options.ToList(), selected) { name = name };
            string Format(string code) => string.IsNullOrEmpty(code) ? (name.StartsWith("fallbackOverride_", StringComparison.Ordinal) ? "Use channel fallback" : "(none)")
                : config.Languages.FirstOrDefault(l => l.Code == code)?.DisplayName + " (" + code + ")";
            field.formatListItemCallback = Format; field.formatSelectedValueCallback = Format;
            field.RegisterValueChangedCallback(e => change(e.newValue)); return field;
        }
        private void Save(Action<LocalizationProjectConfig> mutate, string label)
        {
            try
            {
                using var edit = new LocalizationProjectEdit(config); mutate(edit.Draft);
                LocalizationProjectUndo.Record(edit.Commit(), label); error = null;
            }
            catch (Exception failure) { error = failure.Message; }
            Refresh(); changed?.Invoke();
        }
        private static void RemoveLanguage(LocalizationProjectConfig draft, string code)
        {
            if (draft.Channels.Any(c => c.DefaultLanguage == code || c.FallbackLanguage == code || c.Fallbacks.Any(f => f.Language == code || f.Fallback == code)))
                throw new InvalidOperationException("Choose replacement defaults and fallback rules before removing " + code + ".");
            foreach (var channel in draft.Channels) channel.SetLanguages(channel.Languages.Where(l => l != code));
            foreach (var set in draft.Sets) set.RemoveSources(code);
            draft.RemoveLanguage(code);
        }
        private static VisualElement Row() => new() { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap, alignItems = Align.Center, marginBottom = 8 } };
    }
}
