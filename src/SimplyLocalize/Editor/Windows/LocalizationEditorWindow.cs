using System;
using System.Collections.Generic;
using System.Linq;
using SimplyLocalize.Editor.Data;
using SimplyLocalize.Editor.Windows.Tabs;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace SimplyLocalize.Editor.Windows
{
    /// <summary>Main authoring surface. Editor selection is independent from the running game's language.</summary>
    public partial class LocalizationEditorWindow : EditorWindow
    {
        [SerializeField] private LocalizationProjectConfig _config;
        [SerializeField] private string channelId, setId, appearanceLanguage, selectedText, selectedAsset;
        [SerializeField] private int _activeTabIndex;
        [SerializeField] private bool selectionInitialized;
        [SerializeField] private ProjectViewDraft entryDraft;
        private VisualElement _tabContent, _tabBar, scopeBar;
        private HelpBox windowMessage;
        private ProjectTranslationsView textView;
        private ProjectAssetsView assetView;
        private ProjectAppearanceView appearanceView;
        private ProjectAutoLocalizeView autoView;
        private ProjectToolsView toolsView;
        private IDisposable disposableView;
        private bool reloading, building;
        private readonly List<(string name, Type type)> customTabs = new();
        private static readonly string[] Builtins = { "Translations", "Assets", "Languages", "Appearance", "Coverage", "Auto Localize", "Tools", "Settings", "Diagnostics" };
        // Instance dialog boundary: tests can exercise navigation without modal automation.
        internal Func<string, int> ConfirmNavigation = message => EditorUtility.DisplayDialogComplex("Unsaved localization changes", message, "Save", "Cancel", "Discard");
        public LocalizationProjectConfig ProjectConfig => _config;
        public EditorProjectContext Context => _config == null ? null : new EditorProjectContext(_config, channelId, setId);
        public int ActiveTabIndex => _activeTabIndex;
        public bool HasPendingChanges => entryDraft?.IsPending == true || textView?.HasPendingChanges == true || assetView?.HasPendingChanges == true
            || appearanceView?.HasPendingChanges == true || autoView?.HasPendingChanges == true || toolsView?.HasPendingChanges == true || AuxiliaryPending;

        [MenuItem("Window/Simply Localize/Localization Editor", priority = 100)]
        public static void Open() => GetWindow<LocalizationEditorWindow>().Show();
        public static void OpenConfig(LocalizationProjectConfig config) { var window = GetWindow<LocalizationEditorWindow>(); window.TrySelectConfig(config); window.Show(); }
        private void AssignConfig(LocalizationProjectConfig config) => TrySelectConfig(config);
        public bool TrySelectConfig(LocalizationProjectConfig config)
        {
            if (_config == config && selectionInitialized) return true;
            if (!CanNavigate()) return false;
            if (config != null) config.ValidateOrThrow();
            CaptureAuxiliaryState(); ClearAuxiliaryDrafts();
            _config = config; selectionInitialized = true; channelId = config?.MainChannelId; setId = config?.MainChannel.DefaultSetId;
            selectedText = selectedAsset = appearanceLanguage = null;
            EditorProjectSelection.Select(config, channelId, setId); BuildUI(); return true;
        }
        public bool TrySelectContext(string channel, string set = null)
        {
            if (_config == null) return false;
            var context = new EditorProjectContext(_config, channel, set);
            if (channelId == context.ChannelId && setId == context.SetId) return true;
            if (!CanNavigate()) return false;
            ClearAuxiliaryDrafts();
            channelId = context.ChannelId; setId = context.SetId; selectedText = selectedAsset = null;
            EditorProjectSelection.Select(_config, channelId, setId); BuildUI(); return true;
        }
        public bool ShowTab(int index)
        {
            if (index < 0 || index >= Builtins.Length + customTabs.Count) throw new ArgumentOutOfRangeException(nameof(index));
            if (_activeTabIndex == index && _tabContent?.childCount > 0) return true;
            if (!CanNavigate()) return false;
            CaptureSelection(); CaptureAuxiliaryState(); _activeTabIndex = index; BuildUI(); return true;
        }
        private void SetActiveTab(int index) => ShowTab(index);
        private void OnEnable()
        {
            reloading = false; titleContent = new GUIContent("Simply Localize"); minSize = new Vector2(700, 400);
            saveChangesMessage = "Simply Localize has unsaved changes. Save them, discard them, or cancel closing this window. Recovered import/scan results must be reviewed before applying.";
            if (!selectionInitialized)
            {
                _config = EditorProjectSelection.Config; selectionInitialized = true;
                if (_config != null) { var context = EditorProjectSelection.Current; channelId = context.ChannelId; setId = context.SetId; }
            }
            AssemblyReloadEvents.beforeAssemblyReload += BeforeReload;
            BuildUI();
        }
        private void BeforeReload() { CaptureWindowState(); reloading = true; DisposeView(); }
        private void OnDisable()
        {
            AssemblyReloadEvents.beforeAssemblyReload -= BeforeReload;
            if (!reloading) CaptureWindowState();
            DisposeView();
        }
        private void OnDestroy() { DisposeView(); entryDraft?.Dispose(); entryDraft = null; }
        private void OnFocus() { if (!building) UpdateUnsaved(); } // Refocus must not discard input or rebuild a popup's owner.
        private void CaptureSelection()
        {
            if (textView != null) selectedText = textView.SelectedEntryId;
            if (assetView != null) selectedAsset = assetView.SelectedEntryId;
            if (appearanceView != null) appearanceLanguage = appearanceView.SelectedLanguage;
        }
        private void CaptureWindowState()
        {
            CaptureSelection(); CaptureAuxiliaryState();
            var snapshot = textView?.CaptureDraft() ?? assetView?.CaptureDraft() ?? appearanceView?.CaptureDraft();
            if (snapshot != null) { entryDraft?.Dispose(); entryDraft = snapshot; }
        }
        private void DisposeView()
        {
            disposableView?.Dispose(); disposableView = null;
            textView = null; assetView = null; appearanceView = null; autoView = null; toolsView = null;
        }
        private bool CanNavigate()
        {
            UpdateUnsaved(); if (!HasPendingChanges) return true;
            int answer = ConfirmNavigation(saveChangesMessage);
            if (answer == 1) return false;
            if (answer == 0) return SavePendingChanges();
            DiscardChanges(); return true;
        }
        public bool SavePendingChanges()
        {
            bool result = textView != null ? textView.SaveChanges() : assetView != null ? assetView.SaveChanges() : appearanceView != null ? appearanceView.SaveChanges()
                : toolsView != null ? toolsView.ApplyPreview() : autoView != null ? autoView.ApplyPreview() : entryDraft?.IsPending != true;
            if (result) { entryDraft?.Dispose(); entryDraft = null; }
            UpdateUnsaved(); return result && !HasPendingChanges;
        }
        public override void SaveChanges() { if (SavePendingChanges()) base.SaveChanges(); }
        public override void DiscardChanges()
        {
            bool recoveryVisible = rootVisualElement.Q("draftRecovery") != null || rootVisualElement.Q("auxiliaryRecovery") != null;
            entryDraft?.Dispose(); entryDraft = null; ClearAuxiliaryDrafts();
            if (textView != null) textView.Reload(true);
            if (assetView != null) assetView.Reload(true);
            if (appearanceView != null) appearanceView.Reload(true);
            toolsView?.ClearPreview(); autoView?.ClearScan(); UpdateUnsaved(); base.DiscardChanges();
            if (recoveryVisible && !building) BuildUI();
        }
        private void UpdateUnsaved() { hasUnsavedChanges = HasPendingChanges; }
        private void Changed()
        {
            EditorProjectSelection.Invalidate(); CaptureSelection(); UpdateUnsaved();
            if (!building) { NormalizeContext(); BuildScope(); }
        }
        public void FullRefresh()
        {
            if (!CanNavigate()) return;
            CaptureSelection(); CaptureAuxiliaryState(); EditorProjectSelection.Invalidate(); BuildUI();
        }
        public void RefreshCurrentTab() => FullRefresh();
        private void EditAppearance(string language)
        {
            if (!CanNavigate()) return;
            CaptureSelection(); appearanceLanguage = language; _activeTabIndex = 3; BuildUI();
        }
        private void NormalizeContext()
        {
            if (_config == null) return;
            // Keep a recovered draft tied to its original route. Falling back here would
            // prevent Retry from working when the original channel/set is restored.
            if (entryDraft?.IsPending == true || AuxiliaryPending) return;
            var channel = _config.Channels.FirstOrDefault(c => c.Id == channelId) ?? _config.MainChannel;
            if (channel == null) { channelId = setId = null; return; }
            channelId = channel.Id;
            if (!channel.SetIds.Contains(setId)) setId = channel.DefaultSetId;
        }
        private void BuildUI()
        {
            building = true;
            try
            {
                DisposeView(); rootVisualElement.Clear(); windowMessage = null; _tabContent = null; rootVisualElement.style.flexGrow = 1;
                NormalizeContext(); DiscoverCustomTabs();
                var header = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, paddingLeft = 12, paddingRight = 12, paddingTop = 8, paddingBottom = 8 } };
                header.Add(new Label("Simply Localize") { style = { unityFontStyleAndWeight = FontStyle.Bold, fontSize = 14, flexGrow = 1 } });
                var config = new ObjectField("Config") { name = "configField", objectType = typeof(LocalizationProjectConfig), allowSceneObjects = false, value = _config };
                config.style.width = 350; config.labelElement.style.minWidth = 50; config.labelElement.style.width = 50;
                config.RegisterValueChangedCallback(e =>
                {
                    try { if (!TrySelectConfig(e.newValue as LocalizationProjectConfig)) config.SetValueWithoutNotify(_config); }
                    catch (Exception error) { config.SetValueWithoutNotify(_config); ShowError(error.Message); }
                });
                header.Add(config); rootVisualElement.Add(header);
                scopeBar = new VisualElement { name = "projectScope", style = { flexDirection = FlexDirection.Row, paddingLeft = 12, paddingRight = 12, flexWrap = Wrap.Wrap } }; rootVisualElement.Add(scopeBar); BuildScope();
                _tabBar = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap, paddingLeft = 8 } }; rootVisualElement.Add(_tabBar);
                _activeTabIndex = Mathf.Clamp(_activeTabIndex, 0, Builtins.Length + customTabs.Count - 1);
                var names = Builtins.Concat(customTabs.Select(t => t.name)).ToArray();
                for (int i = 0; i < names.Length; i++)
                {
                    int index = i; var button = new Button(() => ShowTab(index)) { name = "tab" + names[i].Replace(" ", ""), text = names[i] };
                    button.SetEnabled(_config != null || i == 8); button.style.backgroundColor = Color.clear; button.style.borderBottomWidth = 2;
                    button.style.borderTopWidth = button.style.borderLeftWidth = button.style.borderRightWidth = 0;
                    button.style.borderBottomColor = i == _activeTabIndex ? new Color(0.2f, 0.5f, 0.9f) : Color.clear;
                    button.style.paddingTop = button.style.paddingBottom = 8; button.style.paddingLeft = button.style.paddingRight = 14; _tabBar.Add(button);
                }
                windowMessage = new HelpBox("", HelpBoxMessageType.Error) { name = "windowMessage" }; windowMessage.style.display = DisplayStyle.None; rootVisualElement.Add(windowMessage);
                _tabContent = new VisualElement { name = "tabContent", style = { flexGrow = 1, minHeight = 0 } }; rootVisualElement.Add(_tabContent);
                BuildActiveView(); RestoreAuxiliaryState(); UpdateUnsaved();
            }
            catch (Exception error)
            {
                ShowError(error.Message);
                if (entryDraft?.IsPending == true || AuxiliaryPending)
                {
                    if (_tabContent == null) { _tabContent = new VisualElement { name = "tabContent" }; rootVisualElement.Add(_tabContent); }
                    DisposeView(); _tabContent.Clear(); ShowRecovery(error.Message);
                }
            }
            finally { building = false; UpdateUnsaved(); }
        }
        private void BuildScope()
        {
            if (scopeBar == null) return; scopeBar.Clear(); if (_config == null) return;
            var channelOptions = _config.Channels.Select(c => c.Id).ToList();
            if (!string.IsNullOrEmpty(channelId) && !channelOptions.Contains(channelId)) channelOptions.Add(channelId);
            var channel = new DropdownField("Channel", channelOptions, channelId) { name = "channelSelector" };
            channel.formatListItemCallback = channel.formatSelectedValueCallback = id => _config.Channels.FirstOrDefault(c => c.Id == id)?.Name ?? "Unavailable channel";
            channel.style.flexGrow = 1; channel.style.minWidth = 240;
            channel.RegisterValueChangedCallback(e => { try { if (!TrySelectContext(e.newValue)) channel.SetValueWithoutNotify(channelId); } catch (Exception error) { channel.SetValueWithoutNotify(channelId); ShowError(error.Message); } }); scopeBar.Add(channel);
            var definition = _config.Channels.FirstOrDefault(c => c.Id == channelId); if (definition == null) return;
            var setOptions = definition.SetIds.ToList();
            // Display a lost draft route without silently selecting a valid replacement.
            if (!setOptions.Contains(setId ?? "")) setOptions.Add(setId ?? "");
            var set = new DropdownField("Content set", setOptions, setId ?? "") { name = "setSelector" };
            set.formatListItemCallback = set.formatSelectedValueCallback = id => string.IsNullOrEmpty(id) ? "(none)"
                : definition.SetIds.Contains(id) ? _config.Sets.FirstOrDefault(s => s.Id == id)?.Name ?? "Unavailable set" : "Unavailable set";
            set.style.flexGrow = 1; set.style.minWidth = 240; set.SetEnabled(definition.SetIds.Count > 0);
            set.RegisterValueChangedCallback(e => { try { if (!TrySelectContext(channelId, e.newValue)) set.SetValueWithoutNotify(setId); } catch (Exception error) { set.SetValueWithoutNotify(setId); ShowError(error.Message); } }); scopeBar.Add(set);
        }
        private void BuildActiveView()
        {
            if (_activeTabIndex == 8) { new DiagnosticsTab().Build(_tabContent); return; }
            if (_config == null)
            {
                if (entryDraft?.IsPending == true || AuxiliaryPending) ShowRecovery("The project config is unavailable.");
                else _tabContent.Add(new ProjectSetupView(this, config => { _activeTabIndex = 2; TrySelectConfig(config); }));
                return;
            }
            _config.ValidateOrThrow();
            bool needsSet = _activeTabIndex == 0 || _activeTabIndex == 1 || _activeTabIndex == 4 || _activeTabIndex == 5 || _activeTabIndex == 6;
            if (needsSet && string.IsNullOrEmpty(setId))
            {
                if (entryDraft?.IsPending == true || AuxiliaryPending) ShowRecovery("The original content set is unavailable.");
                else _tabContent.Add(new HelpBox("Connect a content set to this channel in Settings.", HelpBoxMessageType.Info));
                return;
            }
            if (needsSet) _ = new EditorProjectContext(_config, channelId, setId);
            VisualElement view = null;
            switch (_activeTabIndex)
            {
                case 0: textView = new ProjectTranslationsView(_config, channelId, setId, Changed, LocalizationEditorReaders.Text); textView.DraftChanged += UpdateUnsaved; view = textView;
                    if (entryDraft?.IsPending != true && selectedText != null) textView.SelectEntry(selectedText); break;
                case 1: assetView = new ProjectAssetsView(_config, channelId, setId, Changed, LocalizationEditorReaders.Assets); assetView.DraftChanged += UpdateUnsaved; view = assetView;
                    if (entryDraft?.IsPending != true && selectedAsset != null) assetView.SelectEntry(selectedAsset); break;
                case 2: view = new ProjectLanguagesView(_config, this, channelId, Changed, EditAppearance); break;
                case 3: appearanceView = new ProjectAppearanceView(_config, appearanceLanguage, Changed, LocalizationEditorReaders.ReadAppearance); appearanceView.DraftChanged += UpdateUnsaved; view = appearanceView; break;
                case 4: view = new ProjectCoverageView(_config, channelId, setId, LocalizationEditorReaders.Text, LocalizationEditorReaders.Assets); break;
                case 5: autoView = new ProjectAutoLocalizeView(_config, channelId, setId, Changed); view = autoView; break;
                case 6: toolsView = new ProjectToolsView(_config, channelId, setId, Changed); view = toolsView; break;
                case 7: view = new ProjectSettingsView(_config, Changed, id => { if (TrySelectContext(id)) ShowTab(2); }); break;
                default:
                    var tab = (IEditorTab)Activator.CreateInstance(customTabs[_activeTabIndex - Builtins.Length].type);
                    disposableView = tab as IDisposable;
                    if (tab is IContextualLocalizationTab contextual) contextual.SetContext(Context);
                    tab.Build(_tabContent); return;
            }
            disposableView = view as IDisposable; _tabContent.Add(view);
            if (entryDraft?.IsPending == true)
            {
                try
                {
                    if (textView != null) textView.RestoreDraft(entryDraft); else if (assetView != null) assetView.RestoreDraft(entryDraft); else if (appearanceView != null) appearanceView.RestoreDraft(entryDraft); else return;
                    entryDraft.Dispose(); entryDraft = null;
                }
                catch (Exception error)
                {
                    DisposeView(); _tabContent.Clear(); _tabContent.Add(new ProjectDraftRecoveryView(entryDraft, error.Message, BuildUI, DiscardChanges));
                }
            }
        }
        private void DiscoverCustomTabs()
        {
            customTabs.Clear();
            foreach (var type in TypeCache.GetTypesWithAttribute<LocalizationEditorTabAttribute>().Where(t => !t.IsAbstract && typeof(IEditorTab).IsAssignableFrom(t))
                .OrderBy(t => ((LocalizationEditorTabAttribute)Attribute.GetCustomAttribute(t, typeof(LocalizationEditorTabAttribute))).Order).ThenBy(t => t.FullName, StringComparer.Ordinal))
            {
                var attribute = (LocalizationEditorTabAttribute)Attribute.GetCustomAttribute(type, typeof(LocalizationEditorTabAttribute));
                customTabs.Add((attribute.TabName, type));
            }
        }
        private void ShowError(string text)
        {
            if (windowMessage == null) { windowMessage = new HelpBox("", HelpBoxMessageType.Error) { name = "windowMessage" }; rootVisualElement.Add(windowMessage); }
            windowMessage.text = text; windowMessage.style.display = string.IsNullOrEmpty(text) ? DisplayStyle.None : DisplayStyle.Flex;
        }
    }
}
