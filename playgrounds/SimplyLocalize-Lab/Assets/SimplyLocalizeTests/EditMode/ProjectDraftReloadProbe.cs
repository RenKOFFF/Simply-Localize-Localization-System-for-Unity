using System;
using System.IO;
using System.Linq;
using SimplyLocalize.Editor;
using SimplyLocalize.Editor.Data;
using SimplyLocalize.Editor.Windows;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace SimplyLocalize.Tests
{
    /// <summary>Explicit local probe, outside Test Runner: verifies real EditorWindow serialization
    /// across a script compilation. No menu or automatic execution; generated fixtures/reports are local.</summary>
    public sealed class ProjectDraftReloadProbe : EditorWindow
    {
        private static readonly string Domain = Guid.NewGuid().ToString("N");
        private string ReportPath => "TestResults/Channels-C5/" + (mainWindow ? "project-window-reload.json" : "project-draft-reload.json");
        [SerializeField] private bool mainWindow;
        [SerializeField] private LocalizationEditorWindow textWindow, assetWindow, appearanceWindow, toolsWindow, setupWindow, autoWindow;
        [SerializeField] private GameObject scanTarget;
        [SerializeField] private string[] previousSelection;
        private static readonly string[] SelectionKeys = { "SimplyLocalize.ProjectEditor.Config", "SimplyLocalize.ProjectEditor.Channel", "SimplyLocalize.ProjectEditor.Set" };
        [SerializeField] private string root, notesPath, domainBefore, textId, assetId;
        [SerializeField] private bool armed;
        [SerializeField] private LocalizationProjectConfig config;
        [SerializeField] private Texture2D texture;
        [SerializeField] private ProjectViewDraft textState, assetState, appearanceState;
        [SerializeField] private int[] previousUndo;
        [SerializeField] private string[] createdNotesFolders;
        private ProjectTranslationsView text;
        private ProjectAssetsView assets;
        private ProjectAppearanceView appearance;
        private void OnEnable()
        {
            AssemblyReloadEvents.beforeAssemblyReload += BeforeReload;
            if (armed && domainBefore != Domain) EditorApplication.delayCall += Finish;
        }
        private void OnDisable() { AssemblyReloadEvents.beforeAssemblyReload -= BeforeReload; }
        public static void Begin() => Start(false);
        public static void BeginMainWindow() => Start(true);
        private static void Start(bool main)
        {
            if (Resources.FindObjectsOfTypeAll<ProjectDraftReloadProbe>().Any()) throw new InvalidOperationException("A draft reload probe is already open.");
            var window = CreateInstance<ProjectDraftReloadProbe>();
            window.mainWindow = main;
            try { window.Prepare(); }
            catch { window.Cleanup(); window.Close(); throw; }
        }
        private void Prepare()
        {
            previousSelection = SelectionKeys.Select(k => SessionState.GetString(k, "")).ToArray();
            root = "Assets/SimplyLocalizeDraftReload_" + Guid.NewGuid().ToString("N"); Directory.CreateDirectory(root); AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            config = LocalizationProjectAuthoring.Create(new[] { "en", "ru" }, "en", "en", root + "/Project.asset", false);
            notesPath = LocalizationProjectNotes.GetAssetPath(config);
            var folders = new System.Collections.Generic.List<string>();
            for (string folder = Path.GetDirectoryName(notesPath).Replace('\\', '/'); folder != "Assets"; folder = Path.GetDirectoryName(folder).Replace('\\', '/'))
                if (!Directory.Exists(folder)) folders.Add(folder);
            createdNotesFolders = folders.ToArray();
            textId = config.Sets[0].AddEntry("greeting").Id; assetId = config.Sets[0].AddEntry("picture", LocalizationEntryKind.Asset).Id; Save(config);
            File.WriteAllText(JsonPath(), "{\"translations\":{\"greeting\":\"Before reload\"}}");
            LocalizationProjectAuthoring.CreateAssetTable(config, config.Sets[0].Id, "en"); LocalizationProjectAuthoring.CreateAssetTable(config, config.Sets[0].Id, "ru");
            LocalizationProjectAuthoring.CreateAppearance(config, "en"); texture = new Texture2D(1, 1); AssetDatabase.CreateAsset(texture, root + "/Picture.asset");
            previousUndo = Resources.FindObjectsOfTypeAll<LocalizationProjectUndo>().Select(r => r.GetInstanceID()).ToArray();
            titleContent = new GUIContent("Draft reload check"); position = new Rect(200, 200, 800, 700); ShowUtility();
            if (mainWindow) CreateMainWindows(); else CreateViews();
            text.Q<TextField>("translation_en").value = "Text survived reload";
            text.Q<TextField>("entryDescription").value = "Note survived reload";
            assets.Q<ObjectField>("asset_ru").value = texture;
            appearance.Q<Toggle>("overrideTypography").value = true; appearance.Q<Slider>("fontSizeMultiplier").value = 1.7f;
            domainBefore = Domain; armed = true;
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath)); File.WriteAllText(ReportPath, "{\"status\":\"waiting for assembly reload\"}");
            EditorApplication.delayCall += () => CompilationPipeline.RequestScriptCompilation();
        }
        private void BeforeReload()
        {
            if (!armed) return;
            if (mainWindow) return; // The real windows capture and restore their own state.
            textState?.Dispose(); assetState?.Dispose(); appearanceState?.Dispose();
            textState = text.CaptureDraft(); assetState = assets.CaptureDraft(); appearanceState = appearance.CaptureDraft();
            DisposeViews();
        }
        private void CreateViews()
        {
            rootVisualElement.Clear(); var scroll = new ScrollView(); rootVisualElement.Add(scroll);
            text = new ProjectTranslationsView(config); text.SelectEntry(textId); scroll.Add(text);
            assets = new ProjectAssetsView(config); assets.SelectEntry(assetId); scroll.Add(assets);
            appearance = new ProjectAppearanceView(config); scroll.Add(appearance);
        }
        private void Finish()
        {
            if (!armed) return;
            armed = false; var result = new Result { unity = Application.unityVersion, domainChanged = domainBefore != Domain };
            try
            {
                Require(result.domainChanged, "No real domain reload occurred.");
                if (!mainWindow) Require(textState?.IsPending == true && assetState?.IsPending == true && appearanceState?.IsPending == true, "Serialized draft state was lost.");
                Require(File.ReadAllText(JsonPath()).Contains("Before reload"), "Draft text was saved without an explicit action.");
                Require(!File.Exists(notesPath), "Draft note was saved without an explicit action.");
                Require(!Table().HasKey("picture"), "Draft asset was saved without an explicit action.");
                if (mainWindow) CheckMainWindows();
                else { CreateViews(); text.RestoreDraft(textState); assets.RestoreDraft(assetState); appearance.RestoreDraft(appearanceState); }
                Require(text.Q<TextField>("translation_en").value == "Text survived reload", "Text value was lost.");
                Require(text.Q<TextField>("entryDescription").value == "Note survived reload", "Note was lost.");
                Require(assets.Q<ObjectField>("asset_ru").value == texture, "Unity asset reference was lost.");
                Require(Mathf.Approximately(appearance.Q<Slider>("fontSizeMultiplier").value, 1.7f), "Appearance clone was lost.");
                Require(text.SaveChanges(), "Restored text could not be saved."); Require(assets.SaveChanges(), "Restored asset could not be saved."); Require(appearance.SaveChanges(), "Restored appearance could not be saved.");
                Require(Table().Get("picture") == texture, "Saved native reference differs.");
                if (mainWindow) Require(!textWindow.hasUnsavedChanges && !assetWindow.hasUnsavedChanges && !appearanceWindow.hasUnsavedChanges, "Main window dirty flags did not clear after Save.");
                result.status = "passed";
            }
            catch (Exception failure) { result.status = "failed"; result.error = failure.ToString(); }
            finally
            {
                try { Cleanup(); } catch (Exception failure) { result.status = "failed"; result.error += "\nCleanup: " + failure; }
                File.WriteAllText(ReportPath, JsonUtility.ToJson(result, true)); Close();
            }
        }
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        private LocalizationEditorWindow OpenMain(int tab)
        {
            var window = CreateInstance<LocalizationEditorWindow>(); window.ShowUtility(); window.TrySelectConfig(config); window.ShowTab(tab); return window;
        }
        private void CreateMainWindows()
        {
            textWindow = OpenMain(0); text = textWindow.rootVisualElement.Q<ProjectTranslationsView>(); text.SelectEntry(textId);
            assetWindow = OpenMain(1); assets = assetWindow.rootVisualElement.Q<ProjectAssetsView>(); assets.SelectEntry(assetId);
            appearanceWindow = OpenMain(3); appearance = appearanceWindow.rootVisualElement.Q<ProjectAppearanceView>();
            toolsWindow = OpenMain(6); Require(toolsWindow.rootVisualElement.Q<ProjectToolsView>().PreviewCsv("Key,en\ngreeting,Imported after reload\n"), "CSV preview failed.");
            autoWindow = OpenMain(5); scanTarget = new GameObject("Reload check", typeof(RectTransform), typeof(UnityEngine.UI.Text));
            scanTarget.GetComponent<UnityEngine.UI.Text>().text = "Scanned before reload";
            var auto = autoWindow.rootVisualElement.Q<ProjectAutoLocalizeView>(); Require(auto.Scan(new[] { scanTarget }), "Scan failed.");
            auto.Candidates[0].Key = "scan/reload"; Require(auto.PreviewChanges(), "Scan preview failed.");
            setupWindow = OpenMain(0); setupWindow.TrySelectConfig(null); setupWindow.rootVisualElement.Q<DropdownField>("setupDefault").value = "ru";
        }
        private void CheckMainWindows()
        {
            Require(textWindow != null && assetWindow != null && appearanceWindow != null && toolsWindow != null && autoWindow != null && setupWindow != null, "A serialized main window was lost.");
            Require(textWindow.ActiveTabIndex == 0 && assetWindow.ActiveTabIndex == 1 && appearanceWindow.ActiveTabIndex == 3 && toolsWindow.ActiveTabIndex == 6 && autoWindow.ActiveTabIndex == 5, "Selected tabs were lost.");
            Require(textWindow.Context.ChannelId == config.MainChannelId && textWindow.Context.SetId == config.Sets[0].Id, "Editor scope was lost.");
            Require(textWindow.hasUnsavedChanges && assetWindow.hasUnsavedChanges && appearanceWindow.hasUnsavedChanges && toolsWindow.hasUnsavedChanges && autoWindow.hasUnsavedChanges, "Native close protection was lost.");
            text = textWindow.rootVisualElement.Q<ProjectTranslationsView>(); assets = assetWindow.rootVisualElement.Q<ProjectAssetsView>(); appearance = appearanceWindow.rootVisualElement.Q<ProjectAppearanceView>();
            var tools = toolsWindow.rootVisualElement.Q<ProjectToolsView>();
            Require(tools.Preview == null && tools.Q<TextField>("csvRecoveredInput").value.Contains("Imported after reload"), "CSV input was lost or stale preview reused.");
            Require(!tools.ApplyPreview(), "An import was applied without reviewing a fresh preview.");
            Require(tools.PreviewCsv(tools.Q<TextField>("csvRecoveredInput").value), "Recovered CSV could not be previewed.");
            var auto = autoWindow.rootVisualElement.Q<ProjectAutoLocalizeView>();
            Require(auto.Preview == null && auto.Candidates.Single().Target == scanTarget && auto.Candidates.Single().Key == "scan/reload", "Scan target or user key was lost.");
            Require(!auto.ApplyPreview() && auto.PreviewChanges(), "Scan did not require and accept a fresh preview.");
            Require(setupWindow.ProjectConfig == null && setupWindow.rootVisualElement.Q<DropdownField>("setupDefault").value == "ru", "Setup selection was lost.");
        }
        private static void Save(UnityEngine.Object asset) { EditorUtility.SetDirty(asset); AssetDatabase.SaveAssetIfDirty(asset); }
        private string JsonPath() => root + "/Resources/SimplyLocalize/Translations/en/Common.json";
        private LocalizationAssetTable Table() => AssetDatabase.LoadAssetAtPath<LocalizationAssetTable>(new ResourcesEditorTextReader().ResolveSourceAssetPath(config.Sets[0].Sources.Single(s => s.Language == "ru").Sources.Single(s => s.Kind == LocalizationSourceKind.AssetTable)));
        private void DisposeViews() { text?.Dispose(); assets?.Dispose(); appearance?.Dispose(); text = null; assets = null; appearance = null; rootVisualElement.Clear(); }
        private void Cleanup()
        {
            foreach (var window in new[] { textWindow, assetWindow, appearanceWindow, toolsWindow, autoWindow, setupWindow })
                if (window != null) { window.DiscardChanges(); window.Close(); }
            if (scanTarget != null) DestroyImmediate(scanTarget);
            armed = false; DisposeViews(); textState?.Dispose(); assetState?.Dispose(); appearanceState?.Dispose();
            if (previousUndo != null) foreach (var record in Resources.FindObjectsOfTypeAll<LocalizationProjectUndo>().Where(r => !previousUndo.Contains(r.GetInstanceID()))) record.Dispose();
            if (!string.IsNullOrEmpty(notesPath) && File.Exists(notesPath)) AssetDatabase.DeleteAsset(notesPath);
            if (createdNotesFolders != null) foreach (string folder in createdNotesFolders)
                if (Directory.Exists(folder) && Directory.GetFileSystemEntries(folder).Length == 0) AssetDatabase.DeleteAsset(folder);
            if (!string.IsNullOrEmpty(root) && root.StartsWith("Assets/SimplyLocalizeDraftReload_", StringComparison.Ordinal)) AssetDatabase.DeleteAsset(root);
            if (previousSelection != null) for (int i = 0; i < SelectionKeys.Length; i++) SessionState.SetString(SelectionKeys[i], previousSelection[i]);
            EditorProjectSelection.Invalidate();
        }
        [Serializable] private sealed class Result { public string status, unity, error; public bool domainChanged; }
    }
}
