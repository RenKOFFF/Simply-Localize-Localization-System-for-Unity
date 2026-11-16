using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;

namespace SimplyLocalize.HorrorLab.Editor
{
    public static class HorrorArchitectureSetup
    {
        public const string Root = HorrorLabSetup.Root;
        public const string MenuPath = Root + "/NightShiftMenu.unity";
        [MenuItem("Tools/Simply Localize Lab/Upgrade Horror Architecture")]
        public static void Upgrade()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode.");
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Directory.CreateDirectory(Root + "/Data/VisualVariants");
            Directory.CreateDirectory(Root + "/Resources/NightShift/Worlds/Precinct");
            AssetDatabase.Refresh();
            MoveFolder(Root + "/Resources/NightShift/Voice", Root + "/Resources/NightShift/Worlds/Precinct/Voice");
            MoveFolder(Root + "/Resources/NightShift/Art", Root + "/Resources/NightShift/Worlds/Precinct/Visuals");
            var text = AssetDatabase.LoadAssetAtPath<LocalizationConfig>(Root + "/Data/TextConfig.asset");
            var voice = AssetDatabase.LoadAssetAtPath<LocalizationConfig>(Root + "/Data/VoiceConfig.asset");
            var visual = AssetDatabase.LoadAssetAtPath<LocalizationConfig>(Root + "/Data/ArtConfig.asset");
            voice.resourcesBasePath = "NightShift/Worlds/Precinct/Voice";
            visual.resourcesBasePath = "NightShift/Worlds/Precinct/Visuals";
            EditorUtility.SetDirty(voice); EditorUtility.SetDirty(visual);
            foreach (var profile in voice.languages)
            foreach (string file in Directory.GetFiles(Root + "/Media/" + profile.Code, "*.wav"))
            {
                var importer = (AudioImporter)AssetImporter.GetAtPath(file.Replace('\\', '/'));
                var settings = importer.defaultSampleSettings;
                // Fixed audit fixture: expose first-use behavior; the playground does not decode clips.
                settings.preloadAudioData = false; settings.loadType = AudioClipLoadType.DecompressOnLoad;
                importer.defaultSampleSettings = settings; importer.loadInBackground = true;
                importer.SaveAndReimport();
            }
            // Editor-only fixture authoring. Runtime locale choices are entirely data driven.
            foreach (var profile in visual.languages)
            {
                var table = AssetDatabase.LoadAssetAtPath<LocalizationAssetTable>(Root + "/Resources/" + visual.resourcesBasePath + "/" + profile.Code + "/Signs.asset");
                foreach (var id in new[] { "Archive", "Reception", "Warning" })
                {
                    string path = Root + "/Data/VisualVariants/" + profile.Code + "-" + id + ".asset";
                    var variant = AssetDatabase.LoadAssetAtPath<HorrorVisualVariant>(path);
                    if (variant == null) { variant = ScriptableObject.CreateInstance<HorrorVisualVariant>(); AssetDatabase.CreateAsset(variant, path); }
                    variant.texture = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Media/" + profile.Code + "/" + id + ".png");
                    variant.contentLanguage = profile; variant.containsText = true; EditorUtility.SetDirty(variant);
                    // Deliberate incomplete visual coverage demonstrates actual-source Auto translation.
                    if (profile.Code == "pl" && id == "Warning") table.Remove("Signs/" + id);
                    else table.Set("Signs/" + id, variant);
                }
                EditorUtility.SetDirty(table);
            }
            foreach (var profile in text.languages)
            {
                var data = JObject.Parse(File.ReadAllText(Root + "/Content/" + profile.Code + ".json"))["translations"].ToObject<System.Collections.Generic.Dictionary<string, string>>();
                string folder = Root + "/Resources/" + text.resourcesBasePath + "/" + profile.Code + "/text";
                Directory.CreateDirectory(folder);
                foreach (var group in data.GroupBy(pair => pair.Key.Split('/')[0]))
                    File.WriteAllText(folder + "/" + group.Key + ".json", JsonConvert.SerializeObject(new { translations = group.ToDictionary(p => p.Key, p => p.Value) }, Formatting.Indented));
            }
            var sequence = AssetDatabase.LoadAssetAtPath<HorrorRadioSequence>(Root + "/Data/Radio.asset");
            sequence.sequenceId = "dispatch-intro"; EditorUtility.SetDirty(sequence);
            var definition = AssetDatabase.LoadAssetAtPath<HorrorWorldDefinition>(Root + "/Data/Precinct.asset");
            if (definition == null) { definition = ScriptableObject.CreateInstance<HorrorWorldDefinition>(); AssetDatabase.CreateAsset(definition, Root + "/Data/Precinct.asset"); }
            definition.voiceConfig = voice; definition.visualConfig = visual; definition.dialogues = new[] { sequence };
            definition.visualKeys = new[] { "Signs/Archive", "Signs/Reception", "Signs/Warning" }; EditorUtility.SetDirty(definition);
            AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
            var worldScene = EditorSceneManager.OpenScene(HorrorLabSetup.ScenePath);
            var game = UnityEngine.Object.FindFirstObjectByType<HorrorPlayground>();
            var presentation = game.GetComponent<HorrorWorldPresentation>(); if (presentation == null) presentation = game.gameObject.AddComponent<HorrorWorldPresentation>();
            presentation.voiceSource = game.GetComponent<AudioSource>();
            if (presentation.voiceSource == null) presentation.voiceSource = game.gameObject.AddComponent<AudioSource>();
            presentation.voiceSource.playOnAwake = false;
            foreach (var target in game.interactables)
            {
                if (target.kind == HorrorInteraction.Radio) target.contentId = "dispatch-intro";
                if (target.surface != null) target.surface.SetPropertyBlock(null);
                EditorUtility.SetDirty(target);
            }
            foreach (var events in UnityEngine.Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None)) UnityEngine.Object.DestroyImmediate(events.gameObject);
            EditorSceneManager.SaveScene(worldScene);
            var menuScene = File.Exists(MenuPath) ? EditorSceneManager.OpenScene(MenuPath) : EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var app = UnityEngine.Object.FindFirstObjectByType<HorrorApp>();
            if (app == null) app = new GameObject("Night Shift Bootstrap", typeof(HorrorApp)).GetComponent<HorrorApp>();
            app.textConfig = AssetDatabase.LoadAssetAtPath<LocalizationConfig>(Root + "/Data/TextConfig.asset");
            app.worldDefinition = AssetDatabase.LoadAssetAtPath<HorrorWorldDefinition>(Root + "/Data/Precinct.asset");
            app.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Root + "/Data/Font.asset");
            if (app.menuCamera == null)
            {
                app.menuCamera = new GameObject("Menu Camera", typeof(Camera)).GetComponent<Camera>();
                app.menuCamera.clearFlags = CameraClearFlags.SolidColor; app.menuCamera.backgroundColor = new Color(.022f, .035f, .04f); app.menuCamera.cullingMask = 0;
            }
            if (UnityEngine.Object.FindFirstObjectByType<EventSystem>() == null)
            {
                var events = new GameObject("EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM
                events.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
                events.AddComponent<StandaloneInputModule>();
#endif
            }
            EditorSceneManager.SaveScene(menuScene, MenuPath);
            var scenes = EditorBuildSettings.scenes.ToList();
            if (!scenes.Any(s => s.path == MenuPath)) scenes.Add(new EditorBuildSettingsScene(MenuPath, true));
            if (!scenes.Any(s => s.path == HorrorLabSetup.ScenePath)) scenes.Add(new EditorBuildSettingsScene(HorrorLabSetup.ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray(); AssetDatabase.SaveAssets();
            Debug.Log("Night Shift architecture upgraded. Entry scene: " + MenuPath);
        }
        private static void MoveFolder(string source, string target)
        {
            if (!AssetDatabase.IsValidFolder(source)) return;
            if (AssetDatabase.IsValidFolder(target)) throw new InvalidOperationException("Both old and new content folders exist: " + target);
            string error = AssetDatabase.MoveAsset(source, target); if (!string.IsNullOrEmpty(error)) throw new IOException(error);
        }
        public static void BuildAndVerify()
        {
            Upgrade();
            string checks = HorrorLabChecks.Data();
            EditorSceneManager.OpenScene(HorrorLabSetup.ScenePath);
            checks += "\n" + HorrorInteractionChecks.Run() + "\n" + HorrorInteractionChecks.SceneTargets();
            EditorSceneManager.OpenScene(MenuPath);
            HorrorBuildIdentity.Write();
            Directory.CreateDirectory("TestResults/NightShiftArchitecture");
            File.WriteAllText("TestResults/NightShiftArchitecture/editor-checks.txt", checks);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes = new[] { MenuPath, HorrorLabSetup.ScenePath },
                locationPathName = "Builds/NightShift/NightShift.exe", target = BuildTarget.StandaloneWindows64,
                // Release player avoids Development PlayerConnection discovery/listening sockets.
                options = BuildOptions.None
            });
            File.WriteAllText("TestResults/NightShiftArchitecture/build.txt", report.summary.result + "\nErrors: " + report.summary.totalErrors + "\nBytes: " + report.summary.totalSize + "\nDuration: " + report.summary.totalTime);
            if (report.summary.result != BuildResult.Succeeded) throw new InvalidOperationException("Night Shift player build failed.");
        }
    }
}
