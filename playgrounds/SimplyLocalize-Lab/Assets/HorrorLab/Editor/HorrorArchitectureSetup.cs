using System;
using System.IO;
using System.Linq;
using SimplyLocalize.Data;
using TMPro;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SimplyLocalize.HorrorLab.Editor
{
    /// <summary>Explicit authoring of this demo only. Never a package migration or runtime config converter.</summary>
    public static class HorrorArchitectureSetup
    {
        public const string Root = HorrorLabSetup.Root;
        public const string MenuPath = Root + "/NightShiftMenu.unity";
        public const string ConfigPath = Root + "/Data/LocalizationProject.asset";
        [MenuItem("Tools/Simply Localize Lab/Configure Night Shift Channels")]
        public static void Upgrade()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode.");
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
                if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("Save scene edits before configuring the demo.");
            var project = AssetDatabase.LoadAssetAtPath<LocalizationProjectConfig>(ConfigPath);
            if (project == null) project = CreateProject();
            project.ValidateOrThrow();
            var voice = project.Channels.Single(c => c.Name == "Voice");
            var visual = project.Channels.Single(c => c.Name == "Visuals");
            var definition = AssetDatabase.LoadAssetAtPath<HorrorWorldDefinition>(Root + "/Data/Precinct.asset");
            if (definition == null) throw new InvalidOperationException("Missing Precinct content definition.");
            definition.voiceSetId = voice.DefaultSetId; definition.visualSetId = visual.DefaultSetId;
            EditorUtility.SetDirty(definition); AssetDatabase.SaveAssetIfDirty(definition);
            foreach (string code in new[] { "en", "ru", "pl" })
            foreach (string id in new[] { "Archive", "Reception", "Warning" })
            {
                var variant = AssetDatabase.LoadAssetAtPath<HorrorVisualVariant>(Root + "/Data/VisualVariants/" + code + "-" + id + ".asset");
                if (variant == null) throw new InvalidOperationException("Missing visual variant: " + code + "/" + id);
                variant.contentLanguage = code; EditorUtility.SetDirty(variant); AssetDatabase.SaveAssetIfDirty(variant);
            }
            var scene = EditorSceneManager.OpenScene(MenuPath);
            var app = UnityEngine.Object.FindFirstObjectByType<HorrorApp>();
            if (app == null) throw new InvalidOperationException("Missing Night Shift bootstrap.");
            app.localization = project; app.voiceChannelId = voice.Id; app.visualChannelId = visual.Id;
            app.worldDefinition = definition;
            EditorUtility.SetDirty(app); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            Debug.Log("Night Shift uses one project config and Main, Voice, Visuals channels.");
        }
        private static LocalizationProjectConfig CreateProject()
        {
            var project = LocalizationProjectConfig.Create(new[] { "en", "ru", "pl" });
            try
            {
                project.AutoInitialize = false; // The demo bootstrap owns its explicit runtime.
                var common = project.Sets[0];
                foreach (var language in project.Languages)
                {
                    language.DisplayName = language.Code switch { "en" => "English", "ru" => "Русский", "pl" => "Polski", _ => language.Code };
                    language.SystemLanguage = language.Code switch { "en" => SystemLanguage.English, "ru" => SystemLanguage.Russian, _ => SystemLanguage.Polish };
                    string folder = Root + "/Resources/NightShift/Text/" + language.Code + "/text";
                    var files = Directory.GetFiles(folder, "*.json").OrderBy(p => p, StringComparer.Ordinal).ToArray();
                    if (files.Length == 0) throw new InvalidOperationException("Missing demo text: " + language.Code);
                    foreach (string path in files)
                        foreach (string key in LocalizationFileParser.Parse(File.ReadAllText(path)).Keys)
                            if (!common.Entries.Any(e => e.Key == key)) common.AddEntry(key);
                    common.SetSources(language.Code, files.Select(path => new LocalizationSourceReference("resources",
                        "NightShift/Text/" + language.Code + "/text/" + Path.GetFileNameWithoutExtension(path), LocalizationSourceKind.TextTable)).ToArray());
                }
                // All three existing demo profiles use this same font and default typography.
                string appearanceFolder = Root + "/Resources/NightShift/Appearance";
                Directory.CreateDirectory(appearanceFolder); AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                string appearancePath = appearanceFolder + "/Common.asset";
                var appearance = AssetDatabase.LoadAssetAtPath<LocalizationAppearance>(appearancePath);
                if (appearance == null)
                {
                    appearance = ScriptableObject.CreateInstance<LocalizationAppearance>();
                    appearance.overrideFont = true; appearance.primaryFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Root + "/Data/Font.asset");
                    AssetDatabase.CreateAsset(appearance, appearancePath);
                }
                foreach (var language in project.Languages)
                    language.Appearance = new("resources", "NightShift/Appearance/Common", LocalizationSourceKind.Appearance);
                AddWorldChannel(project, "Voice", new[] { "en", "ru" }, "Radio");
                AddWorldChannel(project, "Visuals", new[] { "en", "ru", "pl" }, "Signs");
                project.ValidateOrThrow(); AssetDatabase.CreateAsset(project, ConfigPath); AssetDatabase.SaveAssetIfDirty(project);
                return project;
            }
            catch { if (!AssetDatabase.Contains(project)) UnityEngine.Object.DestroyImmediate(project); throw; }
        }
        private static void AddWorldChannel(LocalizationProjectConfig project, string name, string[] languages, string tableName)
        {
            var channel = project.AddChannel(name, languages); channel.FallbackLanguage = "en";
            var set = project.AddSet("Precinct " + name); channel.IncludeSet(set.Id);
            foreach (string code in languages)
            {
                string address = "NightShift/Worlds/Precinct/" + name + "/" + code + "/" + tableName;
                var table = AssetDatabase.LoadAssetAtPath<LocalizationAssetTable>(Root + "/Resources/" + address + ".asset");
                if (table == null) throw new InvalidOperationException("Missing table: " + address);
                foreach (var entry in table.Entries) if (!set.Entries.Any(e => e.Key == entry.key)) set.AddEntry(entry.key, LocalizationEntryKind.Asset);
                set.SetSources(code, new LocalizationSourceReference("resources", address, LocalizationSourceKind.AssetTable));
            }
        }
        public static void BuildAndVerify() => Build(BuildOptions.None);
        public static void BuildVerificationPlayer() => Build(BuildOptions.Development);

        private static void Build(BuildOptions options)
        {
            string checks = HorrorLabChecks.Data();
            EditorSceneManager.OpenScene(HorrorLabSetup.ScenePath);
            checks += "\n" + HorrorInteractionChecks.Run() + "\n" + HorrorInteractionChecks.SceneTargets();
            EditorSceneManager.OpenScene(MenuPath); HorrorBuildIdentity.Write(options == BuildOptions.Development);
            Directory.CreateDirectory("TestResults/NightShiftChannels");
            File.WriteAllText("TestResults/NightShiftChannels/editor-checks.txt", checks);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes = new[] { MenuPath, HorrorLabSetup.ScenePath },
                locationPathName = (options == BuildOptions.Development ? "Builds/Verification" : "Builds/NightShift") + "/NightShift.exe",
                target = BuildTarget.StandaloneWindows64, options = options });
            File.WriteAllText("TestResults/NightShiftChannels/build.txt", report.summary.result + "\nErrors: " + report.summary.totalErrors + "\nBytes: " + report.summary.totalSize);
            if (report.summary.result != BuildResult.Succeeded) throw new InvalidOperationException("Night Shift player build failed.");
        }
    }
}
