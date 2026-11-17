using System;
using System.Linq;
using SimplyLocalize.Editor.Data;
using TMPro;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace SimplyLocalize.Editor.Windows
{
    /// <summary>Language presentation authoring inside Simply Localize. Disabled overrides remain visible.</summary>
    public sealed class ProjectAppearanceView : ScrollView, IDisposable
    {
        private readonly LocalizationProjectConfig config;
        private readonly Action changed;
        private readonly Func<LocalizationSourceReference, LocalizationAppearance> reader;
        private string language, error;
        private LocalizationProjectAssetEdit edit;
        private LocalizationAppearance original, draft;
        private Button save;
        private HelpBox message;
        public string SelectedLanguage => language;
        public bool HasPendingChanges => edit != null && edit.HasChanges;
        public event Action DraftChanged;
        public ProjectAppearanceView(LocalizationProjectConfig config, string language = null, Action changed = null,
            Func<LocalizationSourceReference, LocalizationAppearance> reader = null)
        {
            this.config = config != null ? config : throw new ArgumentNullException(nameof(config));
            this.language = language ?? config.MainChannel.DefaultLanguage; this.changed = changed; this.reader = reader;
            name = "projectAppearance"; style.flexGrow = 1; style.paddingLeft = style.paddingRight = 16; style.paddingTop = 12;
            RegisterCallback<AttachToPanelEvent>(_ => LocalizationProjectUndo.Changed += OnHistory);
            RegisterCallback<DetachFromPanelEvent>(_ => Dispose()); Reload();
        }
        public bool CanLeave()
        {
            if (!HasPendingChanges) return true;
            int choice = EditorUtility.DisplayDialogComplex("Unsaved language appearance", "Save the appearance before leaving?", "Save", "Cancel", "Discard");
            if (choice == 1) return false; if (choice == 0) return SaveChanges(); CloseEdit(); return true;
        }
        public void SelectLanguage(string code)
        {
            if (HasPendingChanges) throw new InvalidOperationException("Save or discard the current appearance first.");
            code = LocalizationLanguageCode.Normalize(code);
            if (!config.Languages.Any(l => l.Code == code)) throw new ArgumentException("Unknown project language.", nameof(code));
            language = code; Reload();
        }
        public bool Reload(bool discard = false)
        {
            if (HasPendingChanges && !discard) { ShowError("Save or discard the current appearance before reloading."); return false; }
            CloseEdit(); error = null;
            var definition = config.Languages.FirstOrDefault(l => l.Code == language);
            if (definition == null) { language = config.MainChannel.DefaultLanguage; definition = config.Languages.FirstOrDefault(l => l.Code == language); }
            if (definition?.Appearance != null)
                try
                {
                    if (definition.Appearance.ProviderId == "resources")
                    {
                        string path = new ResourcesEditorTextReader().ResolveSourceAssetPath(definition.Appearance);
                        original = AssetDatabase.LoadAssetAtPath<LocalizationAppearance>(path);
                        edit = new LocalizationProjectAssetEdit(config); draft = edit.EditSource<LocalizationAppearance>(definition.Appearance);
                    }
                    else if (reader != null) draft = reader(definition.Appearance) ?? throw new InvalidOperationException("Appearance reader returned no data.");
                }
                catch (Exception failure) { CloseEdit(); error = failure.Message; }
            Build(); DraftChanged?.Invoke(); return error == null;
        }
        private void Build()
        {
            Clear(); Add(LanguagePickerStyle.Heading("Language appearance"));
            var selector = new DropdownField("Language", config.Languages.Select(l => l.Code).ToList(), language) { name = "appearanceLanguage" };
            selector.formatListItemCallback = selector.formatSelectedValueCallback = code => config.Languages.FirstOrDefault(l => l.Code == code)?.DisplayName + " (" + code + ")";
            selector.RegisterValueChangedCallback(e => { if (CanLeave()) SelectLanguage(e.newValue); else selector.SetValueWithoutNotify(language); }); Add(selector);
            message = new HelpBox(error ?? "", HelpBoxMessageType.Error) { name = "appearanceMessage" }; message.style.display = error == null ? DisplayStyle.None : DisplayStyle.Flex; Add(message);
            var definition = config.Languages.FirstOrDefault(l => l.Code == language); if (definition == null) return;
            var source = definition.Appearance;
            Add(new Label(source == null ? "No appearance override. Components keep their own styling." : "Source: " + source.ProviderId + ":" + source.Address)
                { name = "appearanceSource", style = { whiteSpace = WhiteSpace.Normal, marginTop = 8 } });
            if (source != null)
            {
                var shared = config.Languages.Where(l => l.Appearance != null && l.Appearance.ProviderId == source.ProviderId
                    && string.Equals(l.Appearance.Address, source.Address, source.ProviderId == "resources" ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)).Select(l => l.Code).ToArray();
                if (shared.Length > 1)
                {
                    Add(new HelpBox("Shared by " + string.Join(", ", shared) + ". Saving changes updates this appearance for all these languages.", HelpBoxMessageType.Info) { name = "sharedAppearance" });
                    if (original != null)
                    {
                        string originalPath = AssetDatabase.GetAssetPath(original);
                        Add(new Button(() => Run(() =>
                        {
                            var copySource = AssetDatabase.LoadAssetAtPath<LocalizationAppearance>(originalPath);
                            if (copySource == null) throw new InvalidOperationException("The original appearance no longer exists. Reload before making a copy.");
                            return LocalizationProjectAuthoring.CreateAppearance(config, language, copyFrom: copySource);
                        }, "Create independent appearance")) { text = "Create independent copy", name = "copyAppearance" });
                    }
                }
            }
            if (source == null) Add(new Button(() => Run(() => LocalizationProjectAuthoring.CreateAppearance(config, language), "Create language appearance"))
                { text = "Create appearance", name = "createAppearance" });
            AddConnection(source);
            if (source != null && source.ProviderId != "resources") Add(new HelpBox("Custom provider. Preview uses its Editor reader when available; saving requires its Editor integration.", HelpBoxMessageType.Info));
            if (draft == null) return;

            var fonts = Section("Font", "overrideFont", draft.overrideFont, value => draft.overrideFont = value);
            fonts.Add(Asset<TMP_FontAsset>("TMP font", "primaryFont", draft.primaryFont, value => draft.primaryFont = value));
            fonts.Add(Asset<TMP_FontAsset>("TMP fallback font", "fallbackFont", draft.fallbackFont, value => draft.fallbackFont = value));
            fonts.Add(Asset<Font>("Legacy font", "legacyFont", draft.legacyFont, value => draft.legacyFont = value));
            var typography = Section("Typography", "overrideTypography", draft.overrideTypography, value => draft.overrideTypography = value);
            var size = new Slider("Size multiplier", 0.5f, 2f) { name = "fontSizeMultiplier", value = draft.fontSizeMultiplier, showInputField = true };
            size.RegisterValueChangedCallback(e => Mutate(() => draft.fontSizeMultiplier = e.newValue)); typography.Add(size);
            typography.Add(Enum("Weight", "fontWeight", draft.fontWeight, value => draft.fontWeight = (FontWeight)value));
            var style = new EnumFlagsField("Style", draft.fontStyle) { name = "fontStyle" }; style.RegisterValueChangedCallback(e => Mutate(() => draft.fontStyle = (FontStyles)e.newValue)); typography.Add(style);
            var spacing = Section("Spacing", "overrideSpacing", draft.overrideSpacing, value => draft.overrideSpacing = value);
            spacing.Add(Number("Line", "lineSpacingAdjustment", draft.lineSpacingAdjustment, value => draft.lineSpacingAdjustment = value));
            spacing.Add(Number("Character", "characterSpacingAdjustment", draft.characterSpacingAdjustment, value => draft.characterSpacingAdjustment = value));
            spacing.Add(Number("Word", "wordSpacingAdjustment", draft.wordSpacingAdjustment, value => draft.wordSpacingAdjustment = value));
            var layout = Section("Layout", "overrideLayout", draft.overrideLayout, value => draft.overrideLayout = value);
            layout.Add(Enum("Direction", "textDirection", draft.textDirection, value => draft.textDirection = (TextDirection)value));
            var alignment = Enum("Alignment", "alignmentOverride", draft.alignmentOverride, value => draft.alignmentOverride = (TextAlignmentOptions)value);
            alignment.SetEnabled(draft.overrideAlignment);
            var alignmentToggle = new Toggle("Override alignment") { name = "overrideAlignment", value = draft.overrideAlignment };
            alignmentToggle.RegisterValueChangedCallback(e => { Mutate(() => draft.overrideAlignment = e.newValue); alignment.SetEnabled(e.newValue); }); layout.Add(alignmentToggle); layout.Add(alignment);
            var actions = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 12 } };
            save = new Button(() => SaveChanges()) { text = "Save appearance", name = "saveAppearance" }; LanguagePickerStyle.Primary(save); actions.Add(save);
            actions.Add(new Button(() => Reload(true)) { text = "Discard", name = "discardAppearance" }); Add(actions); UpdateSave();
        }
        private void AddConnection(LocalizationSourceReference source)
        {
            var connection = new Foldout { text = "Choose a different source", name = "appearanceConnection" };
            var asset = new ObjectField("Resources appearance") { name = "appearanceAsset", objectType = typeof(LocalizationAppearance), allowSceneObjects = false, value = original }; connection.Add(asset);
            connection.Add(new Button(() =>
            {
                var selected = asset.value as LocalizationAppearance;
                if (selected == null) { ShowError("Choose an appearance asset."); return; }
                var reference = new LocalizationSourceReference("resources", ResourcesEditorTextReader.ResourceAddress(AssetDatabase.GetAssetPath(selected)), LocalizationSourceKind.Appearance);
                Run(() =>
                {
                    new ResourcesEditorTextReader().ResolveSourceAssetPath(reference);
                    return Connect(reference);
                }, "Connect language appearance");
            }) { text = "Connect appearance", name = "connectAppearance" });
            var custom = new Foldout { text = "Custom provider" };
            var provider = new TextField("Provider") { name = "appearanceProvider", value = source?.ProviderId != "resources" ? source?.ProviderId ?? "" : "" };
            var address = new TextField("Address") { name = "appearanceAddress", value = source?.ProviderId != "resources" ? source?.Address ?? "" : "" };
            custom.Add(provider); custom.Add(address);
            custom.Add(new Button(() => Run(() =>
            {
                if (provider.value == "resources") throw new ArgumentException("Use the Resources appearance selector above.");
                return Connect(new LocalizationSourceReference(provider.value, address.value, LocalizationSourceKind.Appearance));
            }, "Connect custom appearance")) { text = "Connect source", name = "connectCustomAppearance" }); connection.Add(custom);
            if (source != null) connection.Add(new Button(() => Run(() => Connect(null), "Disconnect appearance"))
                { text = "Disconnect", name = "disconnectAppearance", tooltip = "Keeps the asset and other languages' connections." }); Add(connection);
        }
        private LocalizationProjectChange Connect(LocalizationSourceReference source)
        { using var metadata = new LocalizationProjectEdit(config); metadata.Draft.Languages.Single(l => l.Code == language).Appearance = source; return metadata.Commit(); }
        private void Run(Func<LocalizationProjectChange> action, string label)
        {
            if (!CanLeave()) return;
            try { LocalizationProjectUndo.Record(action(), label); CloseEdit(); Reload(); changed?.Invoke(); }
            catch (Exception failure) { Reload(); ShowError(failure.Message); }
        }
        public bool SaveChanges()
        {
            if (!HasPendingChanges) return true;
            try { LocalizationProjectUndo.Record(edit.Commit(), "Edit language appearance"); CloseEdit(); Reload(); changed?.Invoke(); return true; }
            catch (Exception failure) { ShowError(failure.Message); return false; }
        }
        private VisualElement Section(string title, string name, bool enabled, Action<bool> set)
        {
            var card = new VisualElement(); LanguagePickerStyle.Card(card);
            var toggle = new Toggle("Override " + title.ToLowerInvariant()) { name = name, value = enabled }; toggle.SetEnabled(edit != null); card.Add(toggle);
            var fields = new VisualElement(); fields.SetEnabled(enabled && edit != null);
            toggle.RegisterValueChangedCallback(e => { Mutate(() => set(e.newValue)); fields.SetEnabled(e.newValue && edit != null); }); card.Add(fields); Add(card); return fields;
        }
        private ObjectField Asset<T>(string label, string name, T value, Action<T> set) where T : Object
        { var field = new ObjectField(label) { name = name, objectType = typeof(T), allowSceneObjects = false, value = value }; field.RegisterValueChangedCallback(e => Mutate(() => set(e.newValue as T))); return field; }
        private FloatField Number(string label, string name, float value, Action<float> set)
        { var field = new FloatField(label) { name = name, value = value }; field.RegisterValueChangedCallback(e => Mutate(() => set(e.newValue))); return field; }
        private EnumField Enum(string label, string name, Enum value, Action<Enum> set)
        { var field = new EnumField(label, value) { name = name }; field.RegisterValueChangedCallback(e => Mutate(() => set(e.newValue))); return field; }
        private void Mutate(Action action) { if (edit == null) return; action(); UpdateSave(); DraftChanged?.Invoke(); }
        private void UpdateSave() => save?.SetEnabled(HasPendingChanges);
        private void ShowError(string value) { error = value; if (message != null) { message.text = value ?? ""; message.style.display = value == null ? DisplayStyle.None : DisplayStyle.Flex; } }
        private void OnHistory() { if (HasPendingChanges) ShowError("History changed. Save may conflict; keep your draft or discard and reload."); else Reload(); }
        private void CloseEdit() { edit?.Dispose(); edit = null; original = null; draft = null; save = null; }
        public void Dispose() { LocalizationProjectUndo.Changed -= OnHistory; CloseEdit(); }
    }
}
