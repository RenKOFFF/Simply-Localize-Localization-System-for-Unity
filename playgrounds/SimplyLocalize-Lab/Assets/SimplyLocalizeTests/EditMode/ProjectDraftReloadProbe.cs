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
        private const string ReportPath = "TestResults/Channels-C5/project-draft-reload.json";
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
        public static void Begin()
        {
            if (Resources.FindObjectsOfTypeAll<ProjectDraftReloadProbe>().Any()) throw new InvalidOperationException("A draft reload probe is already open.");
            var window = CreateInstance<ProjectDraftReloadProbe>();
            try { window.Prepare(); }
            catch { window.Cleanup(); window.Close(); throw; }
        }
        private void Prepare()
        {
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
            titleContent = new GUIContent("Draft reload check"); position = new Rect(200, 200, 800, 700); ShowUtility(); CreateViews();
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
                Require(textState?.IsPending == true && assetState?.IsPending == true && appearanceState?.IsPending == true, "Serialized draft state was lost.");
                Require(File.ReadAllText(JsonPath()).Contains("Before reload"), "Draft text was saved without an explicit action.");
                Require(!File.Exists(notesPath), "Draft note was saved without an explicit action.");
                Require(!Table().HasKey("picture"), "Draft asset was saved without an explicit action.");
                CreateViews(); text.RestoreDraft(textState); assets.RestoreDraft(assetState); appearance.RestoreDraft(appearanceState);
                Require(text.Q<TextField>("translation_en").value == "Text survived reload", "Text value was lost.");
                Require(text.Q<TextField>("entryDescription").value == "Note survived reload", "Note was lost.");
                Require(assets.Q<ObjectField>("asset_ru").value == texture, "Unity asset reference was lost.");
                Require(Mathf.Approximately(appearance.Q<Slider>("fontSizeMultiplier").value, 1.7f), "Appearance clone was lost.");
                Require(text.SaveChanges(), "Restored text could not be saved."); Require(assets.SaveChanges(), "Restored asset could not be saved."); Require(appearance.SaveChanges(), "Restored appearance could not be saved.");
                Require(Table().Get("picture") == texture, "Saved native reference differs.");
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
        private static void Save(UnityEngine.Object asset) { EditorUtility.SetDirty(asset); AssetDatabase.SaveAssetIfDirty(asset); }
        private string JsonPath() => root + "/Resources/SimplyLocalize/Translations/en/Common.json";
        private LocalizationAssetTable Table() => AssetDatabase.LoadAssetAtPath<LocalizationAssetTable>(new ResourcesEditorTextReader().ResolveSourceAssetPath(config.Sets[0].Sources.Single(s => s.Language == "ru").Sources.Single(s => s.Kind == LocalizationSourceKind.AssetTable)));
        private void DisposeViews() { text?.Dispose(); assets?.Dispose(); appearance?.Dispose(); text = null; assets = null; appearance = null; rootVisualElement.Clear(); }
        private void Cleanup()
        {
            armed = false; DisposeViews(); textState?.Dispose(); assetState?.Dispose(); appearanceState?.Dispose();
            if (previousUndo != null) foreach (var record in Resources.FindObjectsOfTypeAll<LocalizationProjectUndo>().Where(r => !previousUndo.Contains(r.GetInstanceID()))) record.Dispose();
            if (!string.IsNullOrEmpty(notesPath) && File.Exists(notesPath)) AssetDatabase.DeleteAsset(notesPath);
            if (createdNotesFolders != null) foreach (string folder in createdNotesFolders)
                if (Directory.Exists(folder) && Directory.GetFileSystemEntries(folder).Length == 0) AssetDatabase.DeleteAsset(folder);
            if (!string.IsNullOrEmpty(root) && root.StartsWith("Assets/SimplyLocalizeDraftReload_", StringComparison.Ordinal)) AssetDatabase.DeleteAsset(root);
        }
        [Serializable] private sealed class Result { public string status, unity, error; public bool domainChanged; }
    }
}
