using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;

namespace SimplyLocalize.Lab.Editor
{
    public static class LocalizationLabSetup
    {
        public const string Root = "Assets/LocalizationLab";
        public const string ScenePath = Root + "/LocalizationLab.unity";

        [MenuItem("Tools/Simply Localize Lab/Create Playground")]
        public static void Create()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play Mode before creating the lab.");
            // Existing fixtures are intentionally left untouched. Rebuild in a new folder/project.
            if (File.Exists(ScenePath))
            {
                EditorSceneManager.OpenScene(ScenePath);
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            string sourcePath = "Assets/TextMesh Pro/Fonts/LiberationSans.ttf";
            if (!File.Exists(sourcePath))
            {
                string package = Directory.GetFiles("Library/PackageCache", "TMP Essential Resources.unitypackage", SearchOption.AllDirectories).First();
                AssetDatabase.ImportPackage(package, false);
            }
            var source = AssetDatabase.LoadAssetAtPath<Font>(sourcePath);
            if (source == null) throw new InvalidOperationException("TMP resources must finish importing before creating the lab.");
            Directory.CreateDirectory(Root + "/Data");
            AssetDatabase.Refresh();
            var font = TMP_FontAsset.CreateFontAsset(source);
            font.name = "Lab Liberation Sans";
            font.atlasPopulationMode = AtlasPopulationMode.Dynamic;
            AssetDatabase.CreateAsset(font, Root + "/Data/LabFont.asset");
            AssetDatabase.AddObjectToAsset(font.material, font);
            foreach (var texture in font.atlasTextures) AssetDatabase.AddObjectToAsset(texture, font);
            var config = LocalizationProjectConfig.Create(new[] { "en", "ru", "pl" });
            foreach (string key in new[] { "Lab/Welcome", "Lab/Coins", "Lab/Named", "Lab/Profile", "Lab/Escapes", "Lab/Fallback" })
                config.Sets[0].AddEntry(key);
            foreach (string code in new[] { "en", "ru", "pl" })
            {
                var profile = ScriptableObject.CreateInstance<LocalizationAppearance>();
                var language = config.Languages.First(l => l.Code == code);
                language.SystemLanguage = code == "en" ? SystemLanguage.English : code == "ru" ? SystemLanguage.Russian : SystemLanguage.Polish;
                profile.overrideFont = true; profile.primaryFont = font; profile.legacyFont = source;
                profile.overrideTypography = code == "ru"; profile.fontSizeMultiplier = code == "ru" ? 1.1f : 1f;
                string appearancePath = "SimplyLocalizeLab/" + code + "/Appearance";
                Directory.CreateDirectory(Root + "/Resources/SimplyLocalizeLab/" + code);
                AssetDatabase.Refresh();
                AssetDatabase.CreateAsset(profile, Root + "/Resources/" + appearancePath + ".asset");
                language.Appearance = new LocalizationSourceReference("resources", appearancePath, LocalizationSourceKind.Appearance);
                config.Sets[0].SetSources(code, new LocalizationSourceReference("resources", "SimplyLocalizeLab/" + code + "/text/lab", LocalizationSourceKind.TextTable));
                WriteData(code);
            }
            config.MainChannel.SetFallback("ru", "en"); config.MainChannel.SetFallback("pl", "en");
            AssetDatabase.CreateAsset(config, Root + "/Data/LabConfig.asset");
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            config = AssetDatabase.LoadAssetAtPath<LocalizationProjectConfig>(Root + "/Data/LabConfig.asset");
            font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Root + "/Data/LabFont.asset");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camera = new GameObject("Main Camera", typeof(Camera));
            camera.tag = "MainCamera";
            camera.GetComponent<Camera>().clearFlags = CameraClearFlags.SolidColor;
            camera.GetComponent<Camera>().backgroundColor = new Color(0.035f, 0.055f, 0.09f);
            var events = new GameObject("EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM
            events.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
            events.AddComponent<StandaloneInputModule>();
#endif
            var root = new GameObject("LocalizationLab");
            // Keep components disabled while assigning localization keys in edit mode.
            root.SetActive(false);
            var lab = root.AddComponent<LocalizationLab>();
            lab.config = config; lab.uiFont = font; lab.legacyFont = source;
            lab.CreateView();
            var serialized = new SerializedObject(lab.localOverride);
            var entries = serialized.FindProperty("_entries"); entries.arraySize = 1;
            var entry = entries.GetArrayElementAtIndex(0);
            entry.FindPropertyRelative("languageCode").stringValue = "ru";
            entry.FindPropertyRelative("overrideTypography").boolValue = true;
            entry.FindPropertyRelative("fontSizeMultiplier").floatValue = 1.4f;
            entry.FindPropertyRelative("fontWeight").intValue = (int)FontWeight.Regular;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            root.SetActive(true);
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            Selection.activeGameObject = root;
            Debug.Log("Simply Localize playground created: " + ScenePath);
        }

        private static void WriteData(string code)
        {
            var data = new Dictionary<string, string>
            {
                ["Lab/Welcome"] = code == "ru" ? "Привет, мир!" : code == "pl" ? "Witaj, świecie!" : "Hello, world!",
                ["Lab/Coins"] = code == "ru" ? "{0} {0|монета|монеты|монет}" : code == "pl" ? "{0} {0|moneta|monety|monet}" : "{0} {0|coin|coins}",
                ["Lab/Named"] = code == "ru" ? "Игрок: {name}, предметов: {count}" : code == "pl" ? "Gracz: {name}, przedmioty: {count}" : "Player: {name}, items: {count}",
                ["Lab/Profile"] = code == "ru" ? "Локальный профиль: размер x1.4" : code == "pl" ? "Profil: rozmiar bazowy" : "Profile: baseline size",
                ["Lab/Escapes"] = "Escaped braces: {{name}} / {{0}}"
            };
            if (code == "en") data["Lab/Fallback"] = "Fallback: this key exists only in English.";
            string folder = Root + "/Resources/SimplyLocalizeLab/" + code + "/text";
            Directory.CreateDirectory(folder);
            File.WriteAllText(folder + "/lab.json", JsonConvert.SerializeObject(new { translations = data }, Formatting.Indented));
        }
    }
}
