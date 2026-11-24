using System;
using System.IO;
using System.Linq;
using SimplyLocalize.Editor.Data;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace SimplyLocalize.Editor.Windows
{
    /// <summary>Startup, channel and content routing authoring. All policy edits target definitions, never a running game.</summary>
    public sealed class ProjectSettingsView : ScrollView
    {
        private readonly LocalizationProjectConfig config;
        private readonly Action changed;
        private readonly Action<string> editLanguages;
        private string error;
        private string expandedChannel, expandedSet;
        public ProjectSettingsView(LocalizationProjectConfig config, Action changed = null, Action<string> editLanguages = null)
        {
            this.config = config != null ? config : throw new ArgumentNullException(nameof(config));
            this.changed = changed; this.editLanguages = editLanguages;
            expandedChannel = config.MainChannelId; expandedSet = config.MainChannel.DefaultSetId;
            name = "projectSettings"; style.flexGrow = 1; style.paddingTop = 12; style.paddingLeft = style.paddingRight = 16;
            RegisterCallback<AttachToPanelEvent>(_ => LocalizationProjectUndo.Changed += Refresh);
            RegisterCallback<DetachFromPanelEvent>(_ => LocalizationProjectUndo.Changed -= Refresh);
            Refresh();
        }
        public void Refresh()
        {
            Clear(); Add(LanguagePickerStyle.Heading("Project settings"));
            if (!string.IsNullOrEmpty(error)) Add(new HelpBox(error, HelpBoxMessageType.Error) { name = "settingsMessage" });
            var startup = Card("Startup"); string path = AssetDatabase.GetAssetPath(config);
            startup.Add(new Label("Config: " + path) { style = { whiteSpace = WhiteSpace.Normal } });
            if (path != LocalizationProjectAuthoring.ConfigAssetPath)
            {
                startup.Add(new Label("Use an explicit bootstrap, or move this config to the automatic startup path.") { style = { whiteSpace = WhiteSpace.Normal } });
                startup.Add(new Button(() => Run(() => LocalizationProjectAuthoring.UseForAutomaticStartup(config)))
                    { name = "useForAutomaticStartup", text = "Move config and enable automatic startup", tooltip = "Keeps the config GUID and source files. This is an asset move, outside the localization edit history." });
            }
            startup.Add(Toggle("Auto-initialize", "autoInitialize", config.AutoInitialize, value => Save(d => d.AutoInitialize = value, "Change localization startup")));
            startup.Add(Toggle("Auto-detect device language", "autoDetectLanguage", config.AutoDetectLanguage, value => Save(d => d.AutoDetectLanguage = value, "Change language detection")));
            Add(startup);
            var editor = Card("Editor tools");
            editor.Add(new Label("Local Editor preferences; these do not change the project config.") { style = { whiteSpace = WhiteSpace.Normal } });
            var dropdown = new Toggle("Show language dropdown in Play Mode")
            { name = "showGameViewLanguage", value = EditorPrefs.GetBool(GameViewLanguageDropdown.EnabledPrefKey, true) };
            int savedPosition = EditorPrefs.GetInt(GameViewLanguageDropdown.PositionPrefKey, (int)GameViewLanguageDropdown.Position.Right);
            var currentPosition = Enum.IsDefined(typeof(GameViewLanguageDropdown.Position), savedPosition)
                ? (GameViewLanguageDropdown.Position)savedPosition : GameViewLanguageDropdown.Position.Right;
            var position = new EnumField("Game View position", currentPosition) { name = "gameViewLanguagePosition" };
            position.SetEnabled(dropdown.value);
            dropdown.RegisterValueChangedCallback(e => { EditorPrefs.SetBool(GameViewLanguageDropdown.EnabledPrefKey, e.newValue); position.SetEnabled(e.newValue); });
            position.RegisterValueChangedCallback(e => EditorPrefs.SetInt(GameViewLanguageDropdown.PositionPrefKey, (int)(GameViewLanguageDropdown.Position)e.newValue));
            editor.Add(dropdown); editor.Add(position); Add(editor);
            Add(new HelpBox("Main/Common is enough for a small project. Add a channel for an independent language choice; add a content set to control which data is needed. A set is not a bundle.", HelpBoxMessageType.Info));

            var channels = Card("Channels");
            foreach (var channel in config.Channels)
            {
                string id = channel.Id;
                var fold = new Foldout { text = channel.Name + (id == config.MainChannelId ? " · Main" : ""), value = expandedChannel == id, name = "channel_" + id };
                fold.RegisterValueChangedCallback(e => { if (e.newValue) expandedChannel = id; else if (expandedChannel == id) expandedChannel = null; });
                fold.Add(Text("Name", "channelName_" + id, channel.Name, value => Save(d => Channel(d, id).Name = value, "Rename channel")));
                fold.Add(Id(id));
                if (id != config.MainChannelId)
                {
                    var mode = new EnumField("Language mode", channel.Mode) { name = "channelMode_" + id };
                    mode.RegisterValueChangedCallback(e => Save(d => Channel(d, id).Mode = (LocalizationChannelMode)e.newValue, "Change channel mode")); fold.Add(mode);
                }
                if (editLanguages != null) fold.Add(new Button(() => editLanguages(id)) { text = "Edit available languages and fallback", name = "editLanguages_" + id });
                if (channel.SetIds.Count > 0)
                {
                    string FormatDefaults(string set) => config.Sets.FirstOrDefault(s => s.Id == set)?.Name ?? set;
                    var defaults = new DropdownField("Default set", channel.SetIds.ToList(), channel.DefaultSetId, FormatDefaults, FormatDefaults) { name = "defaultSet_" + id };
                    defaults.RegisterValueChangedCallback(e => Save(d => Channel(d, id).SetDefaultSet(e.newValue), "Change default content set")); fold.Add(defaults);
                }
                foreach (var set in config.Sets)
                {
                    string setId = set.Id; bool included = channel.SetIds.Contains(setId);
                    var row = Row();
                    row.Add(Toggle(set.Name, "includeSet_" + id + "_" + setId, included, value => Save(d =>
                    {
                        var target = Channel(d, id);
                        if (value) target.IncludeSet(setId);
                        else
                        {
                            if (target.DefaultSetId == setId && target.SetIds.Count > 1) throw new InvalidOperationException("Choose another default set before removing this binding.");
                            target.RemoveSet(setId);
                        }
                    }, "Change channel content")));
                    if (included) row.Add(Toggle("Load at startup", "startupSet_" + id + "_" + setId, channel.StartupSetIds.Contains(setId),
                        value => Save(d => Channel(d, id).SetStartup(setId, value), "Change content loading")));
                    fold.Add(row);
                }
                if (id != config.MainChannelId) fold.Add(new Button(() => Save(d => d.RemoveChannel(id), "Remove channel"))
                    { text = "Remove channel", name = "removeChannel_" + id, tooltip = "Keeps content sets and source files." });
                channels.Add(fold);
            }
            var channelName = new TextField("New channel") { name = "newChannelName" }; channels.Add(channelName);
            channels.Add(new Button(() => Save(d => expandedChannel = d.AddChannel(channelName.value, d.MainChannel.Languages).Id, "Add channel"))
                { text = "Add channel", name = "addChannel" }); Add(channels);

            var sets = Card("Content sets");
            foreach (var set in config.Sets)
            {
                string id = set.Id;
                var fold = new Foldout { text = set.Name + " · " + set.Entries.Count + " entries", value = expandedSet == id, name = "set_" + id };
                fold.RegisterValueChangedCallback(e => { if (e.newValue) expandedSet = id; else if (expandedSet == id) expandedSet = null; });
                fold.Add(Text("Name", "setName_" + id, set.Name, value => Save(d => Set(d, id).Name = value, "Rename content set"))); fold.Add(Id(id));
                fold.Add(new Label("Used by: " + string.Join(", ", config.Channels.Where(c => c.SetIds.Contains(id)).Select(c => c.Name))));
                AddSources(fold, set);
                fold.Add(new Button(() => Save(d => d.RemoveSet(id), "Remove content set"))
                    { text = "Remove set", name = "removeSet_" + id, tooltip = "Unbind the set from every channel first. Source files stay on disk." });
                sets.Add(fold);
            }
            var setName = new TextField("New set") { name = "newSetName" }; sets.Add(setName);
            sets.Add(new Button(() => Save(d => expandedSet = d.AddSet(setName.value).Id, "Add content set")) { text = "Add set", name = "addSet" }); Add(sets);
        }
        private void AddSources(VisualElement parent, LocalizationContentSet set)
        {
            foreach (var binding in set.Sources)
                foreach (var source in binding.Sources)
                {
                    string code = binding.Language; var row = Row();
                    row.Add(new Label(code + " · " + source.Kind + " · " + source.ProviderId + ":" + source.Address) { style = { whiteSpace = WhiteSpace.Normal, flexGrow = 1 } });
                    row.Add(new Button(() => Save(d => Set(d, set.Id).SetSources(code, Set(d, set.Id).Sources.Single(s => s.Language == code).Sources
                        .Where(s => !(s.ProviderId == source.ProviderId && s.Kind == source.Kind && s.Address == source.Address)).ToArray()), "Disconnect localization source"))
                        { text = "Disconnect", tooltip = "Keeps the source file and registered entries." }); parent.Add(row);
                }
            var add = new Foldout { text = "Connect a source", name = "connectSource_" + set.Id };
            var language = new DropdownField("Language", config.Languages.Select(l => l.Code).ToList(), config.MainChannel.DefaultLanguage) { name = "sourceLanguage_" + set.Id };
            var provider = new TextField("Provider") { value = "resources", name = "sourceProvider_" + set.Id };
            var kind = new DropdownField("Data", new[] { "Text", "Assets" }.ToList(), "Text") { name = "sourceKind_" + set.Id };
            var address = new TextField("Address") { name = "sourceAddress_" + set.Id };
            var asset = new ObjectField("Resources asset") { objectType = typeof(UnityEngine.Object), allowSceneObjects = false, name = "sourceAsset_" + set.Id };
            asset.RegisterValueChangedCallback(e => { if (e.newValue != null) { provider.value = "resources"; address.value = ResourcesEditorTextReader.ResourceAddress(AssetDatabase.GetAssetPath(e.newValue)) ?? ""; kind.value = e.newValue is LocalizationAssetTable ? "Assets" : "Text"; } });
            provider.RegisterValueChangedCallback(e => asset.SetEnabled(e.newValue == "resources"));
            add.Add(language); add.Add(kind); add.Add(provider); add.Add(asset); add.Add(address);
            add.Add(new Button(() => Save(d =>
            {
                var source = new LocalizationSourceReference(provider.value, address.value, kind.value == "Text" ? LocalizationSourceKind.TextTable : LocalizationSourceKind.AssetTable);
                var existing = Set(d, set.Id).Sources.FirstOrDefault(s => s.Language == language.value)?.Sources ?? Array.Empty<LocalizationSourceReference>();
                if (existing.Any(s => s.ProviderId == source.ProviderId && s.Kind == source.Kind && string.Equals(s.Address, source.Address,
                    source.ProviderId == "resources" ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))) throw new InvalidOperationException("This source is already connected.");
                if (source.ProviderId == "resources")
                {
                    var index = new ResourcesEditorTextReader(); index.ResolveSourceAssetPath(source);
                    if (source.Kind == LocalizationSourceKind.TextTable) index.Read(source);
                }
                Set(d, set.Id).SetSources(language.value, existing.Concat(new[] { source }).ToArray());
            }, "Connect localization source")) { text = "Connect source", name = "addSource_" + set.Id });
            add.Add(new Button(() =>
            {
                string path = EditorUtility.SaveFilePanelInProject("Create text source", "Translations", "json", "Choose a path inside a Resources folder.",
                    Path.GetDirectoryName(AssetDatabase.GetAssetPath(config)).Replace('\\', '/'));
                if (string.IsNullOrEmpty(path)) return;
                Run(() => { using var edit = new LocalizationProjectEdit(config, set.Id); edit.CreateTextSource(set.Id, language.value, path); LocalizationProjectUndo.Record(edit.Commit(), "Create text source"); });
            }) { text = "Create empty JSON", name = "createSource_" + set.Id }); parent.Add(add);
        }
        private void Save(Action<LocalizationProjectConfig> mutation, string label) => Run(() =>
        { using var edit = new LocalizationProjectEdit(config); mutation(edit.Draft); LocalizationProjectUndo.Record(edit.Commit(), label); });
        private void Run(Action action)
        { try { action(); error = null; } catch (Exception failure) { error = failure.Message; } Refresh(); changed?.Invoke(); }
        private static LocalizationChannelDefinition Channel(LocalizationProjectConfig d, string id) => d.Channels.Single(c => c.Id == id);
        private static LocalizationContentSet Set(LocalizationProjectConfig d, string id) => d.Sets.Single(s => s.Id == id);
        private static VisualElement Card(string title) { var card = new VisualElement(); LanguagePickerStyle.Card(card); card.Add(LanguagePickerStyle.Heading(title)); return card; }
        private static VisualElement Row() => new() { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap, alignItems = Align.Center, marginTop = 4 } };
        private static TextField Id(string value) => new("ID") { value = value, isReadOnly = true, tooltip = "Stable identity; renaming does not change it." };
        private static TextField Text(string label, string name, string value, Action<string> change)
        { var field = new TextField(label) { name = name, value = value, isDelayed = true }; field.RegisterValueChangedCallback(e => change(e.newValue)); return field; }
        private static Toggle Toggle(string label, string name, bool value, Action<bool> change)
        { var field = new Toggle(label) { name = name, value = value }; field.RegisterValueChangedCallback(e => change(e.newValue)); return field; }
    }
}
