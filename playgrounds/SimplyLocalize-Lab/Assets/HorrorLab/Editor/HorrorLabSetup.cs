using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;

namespace SimplyLocalize.HorrorLab.Editor
{
    public static class HorrorLabSetup
    {
        public const string Root = "Assets/HorrorLab";
        public const string ScenePath = Root + "/NightShift.unity";
        private static readonly List<HorrorInteractable> targets = new();
        [MenuItem("Tools/Simply Localize Lab/Create Horror Playground")]
        public static void Create()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode first.");
            if (File.Exists(ScenePath)) { HorrorArchitectureSetup.Upgrade(); return; }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Directory.CreateDirectory(Root + "/Data");
            Directory.CreateDirectory(Root + "/Materials");
            string fontPath = "Assets/TextMesh Pro/Fonts/LiberationSans.ttf";
            if (!File.Exists(fontPath))
            {
                var package = Directory.GetFiles("Library/PackageCache", "TMP Essential Resources.unitypackage", SearchOption.AllDirectories).First();
                AssetDatabase.ImportPackage(package, false);
            }
            AssetDatabase.Refresh();
            var source = AssetDatabase.LoadAssetAtPath<Font>(fontPath);
            if (source == null) throw new InvalidOperationException("Wait for TMP Essential Resources to import.");
            var font = TMP_FontAsset.CreateFontAsset(source);
            font.name = "Night Shift Multilingual"; font.atlasPopulationMode = AtlasPopulationMode.Dynamic;
            AssetDatabase.CreateAsset(font, Root + "/Data/Font.asset");
            AssetDatabase.AddObjectToAsset(font.material, font);
            foreach (var texture in font.atlasTextures) AssetDatabase.AddObjectToAsset(texture, font);
            var profiles = new List<LanguageProfile>();
            string[] codes = { "en", "ru", "pl" }, names = { "English", "Русский", "Polski" };
            for (int i = 0; i < codes.Length; i++)
            {
                var profile = ScriptableObject.CreateInstance<LanguageProfile>();
                profile.languageCode = codes[i]; profile.displayName = names[i]; profile.overrideFont = true; profile.primaryFont = font;
                profile.systemLanguage = i == 0 ? SystemLanguage.English : i == 1 ? SystemLanguage.Russian : SystemLanguage.Polish;
                AssetDatabase.CreateAsset(profile, Root + "/Data/" + codes[i] + ".asset"); profiles.Add(profile);
                var data = JObject.Parse(File.ReadAllText(Root + "/Content/" + codes[i] + ".json"))["translations"].ToObject<Dictionary<string, string>>();
                var textFolder = Root + "/Resources/NightShift/Text/" + codes[i] + "/text";
                Directory.CreateDirectory(textFolder);
                foreach (var group in data.GroupBy(p => p.Key.Split('/')[0]))
                    File.WriteAllText(textFolder + "/" + group.Key + ".json", JsonConvert.SerializeObject(new { translations = group.ToDictionary(p => p.Key, p => p.Value) }, Formatting.Indented));
                Directory.CreateDirectory(Root + "/Resources/NightShift/Art/" + codes[i]);
                if (i < 2) Directory.CreateDirectory(Root + "/Resources/NightShift/Voice/" + codes[i]);
            }
            AssetDatabase.Refresh();
            var textConfig = Config("Text", profiles);
            var voiceConfig = Config("Voice", profiles.Take(2).ToList());
            var artConfig = Config("Art", profiles);
            foreach (var code in codes)
            {
                var table = ScriptableObject.CreateInstance<LocalizationAssetTable>();
                foreach (var sign in new[] { "Archive", "Warning", "Reception" })
                {
                    var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Media/" + code + "/" + sign + ".png");
                    if (texture == null) throw new InvalidOperationException("Missing texture " + code + "/" + sign);
                    table.Set("Signs/" + sign, texture);
                }
                AssetDatabase.CreateAsset(table, Root + "/Resources/NightShift/Art/" + code + "/Signs.asset");
                if (code == "pl") continue;
                var voices = ScriptableObject.CreateInstance<LocalizationAssetTable>();
                for (int i = 1; i <= 3; i++)
                {
                    var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(Root + "/Media/" + code + "/" + i.ToString("00") + ".wav");
                    if (clip == null) throw new InvalidOperationException("Missing demo voice " + code + "/" + i);
                    voices.Set("Radio/" + i.ToString("00"), clip);
                }
                AssetDatabase.CreateAsset(voices, Root + "/Resources/NightShift/Voice/" + code + "/Radio.asset");
            }
            var sequence = ScriptableObject.CreateInstance<HorrorRadioSequence>();
            sequence.lines = Enumerable.Range(1, 3).Select(i => new HorrorRadioSequence.Line { speakerKey = "Dialogue/Speaker", textKey = "Dialogue/" + i.ToString("00"), audioKey = "Radio/" + i.ToString("00") }).ToArray();
            AssetDatabase.CreateAsset(sequence, Root + "/Data/Radio.asset");
            AssetDatabase.SaveAssets();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            textConfig = AssetDatabase.LoadAssetAtPath<LocalizationConfig>(Root + "/Data/TextConfig.asset");
            voiceConfig = AssetDatabase.LoadAssetAtPath<LocalizationConfig>(Root + "/Data/VoiceConfig.asset");
            artConfig = AssetDatabase.LoadAssetAtPath<LocalizationConfig>(Root + "/Data/ArtConfig.asset");
            font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Root + "/Data/Font.asset");
            sequence = AssetDatabase.LoadAssetAtPath<HorrorRadioSequence>(Root + "/Data/Radio.asset");
            targets.Clear();
            var root = new GameObject("Night Shift — Localization Playground");
            var game = root.AddComponent<HorrorPlayground>();
            var plaster = Material("Old plaster", new Color(.25f, .3f, .28f));
            var trim = Material("Painted wood", new Color(.085f, .13f, .12f));
            var tileA = Material("Tile A", new Color(.20f, .23f, .23f));
            var tileB = Material("Tile B", new Color(.11f, .14f, .15f));
            var wood = Material("Desk wood", new Color(.19f, .12f, .07f));
            var iron = Material("Iron", new Color(.1f, .13f, .14f));
            var brass = Material("Brass", new Color(.58f, .39f, .12f));
            var paper = Material("Paper", new Color(.7f, .68f, .52f));
            var green = Material("Emergency lamp", new Color(.15f, .65f, .4f), true);
            var warm = Material("Tube lamp", new Color(1, .77f, .44f), true);
            var signMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Texture"));
            AssetDatabase.CreateAsset(signMaterial, Root + "/Materials/Sign.asset");
            var room = new GameObject("West Precinct").transform;
            Cube("Foundation", room, new Vector3(0, -.2f, 5), new Vector3(14, .3f, 20), iron);
            for (int x = -7; x < 7; x++) for (int z = -5; z < 15; z++)
                Cube("Floor tile", room, new Vector3(x + .5f, -.025f, z + .5f), new Vector3(.99f, .05f, .99f), (x + z) % 2 == 0 ? tileA : tileB, false);
            foreach (float x in new[] { -7f, 7f })
            {
                Cube("Wall", room, new Vector3(x, 2, 5), new Vector3(.25f, 4, 20), plaster);
                Cube("Wainscot", room, new Vector3(x * .98f, .65f, 5), new Vector3(.2f, 1.3f, 20), trim);
                Cube("Dado rail", room, new Vector3(x * .975f, 1.35f, 5), new Vector3(.24f, .1f, 20), wood);
                for (int z = -3; z < 14; z += 4) Cube("Pilaster", room, new Vector3(x * .97f, 2, z), new Vector3(.4f, 4, .4f), trim);
            }
            Cube("Back wall", room, new Vector3(0, 2, -5), new Vector3(14, 4, .25f), plaster);
            Cube("Archive rear", room, new Vector3(0, 2, 15), new Vector3(14, 4, .25f), plaster);
            Cube("Ceiling", room, new Vector3(0, 4.1f, 5), new Vector3(14, .2f, 20), trim);
            foreach (float x in new[] { -4f, 4f }) Cube("Archive partition", room, new Vector3(x, 2, 11), new Vector3(6, 4, .3f), plaster);
            Cube("Lintel", room, new Vector3(0, 3.5f, 11), new Vector3(2, 1, .3f), plaster);
            var door = new GameObject("Archive door hinge"); door.transform.position = new Vector3(-1, 0, 10.8f);
            var leaf = Cube("Door", door.transform, new Vector3(1, 1.45f, 0), new Vector3(2, 2.9f, .18f), wood);
            Cube("Handle", door.transform, new Vector3(1.8f, 1.3f, -.18f), new Vector3(.18f, .06f, .16f), brass, false);
            Target(door, HorrorInteraction.Door, "World/Door");
            Sign("Archive", room, new Vector3(0, 3.3f, 10.75f), new Vector3(2.8f, .7f, .06f), signMaterial);
            Sign("Reception", room, new Vector3(-6.75f, 2.6f, 3), new Vector3(3, .8f, .06f), signMaterial).transform.rotation = Quaternion.Euler(0, -90, 0);
            var warning = Sign("Warning", room, new Vector3(3.7f, 2.1f, 10.75f), new Vector3(2.2f, 1.1f, .06f), signMaterial);
            for (int i = 0; i < 5; i++)
            {
                var cabinet = Cube("Evidence locker", room, new Vector3(6.25f, 1.2f, 4 + i * 1.05f), new Vector3(1.1f, 2.4f, 1), iron);
                Cube("Locker handle", room, cabinet.transform.position + new Vector3(-.58f, 0, .3f), new Vector3(.08f, .25f, .06f), brass, false);
                for (int j = 0; j < 3; j++) Cube("Vent", room, cabinet.transform.position + new Vector3(-.56f, .6f + .12f * j, 0), new Vector3(.02f, .035f, .65f), trim, false);
            }
            Table(room, new Vector3(-3.6f, 0, 3), new Vector3(3.2f, 1, 1.4f), wood, iron);
            var radioObject = Cube("Radio", room, new Vector3(-3.1f, 1.2f, 3), new Vector3(.7f, .35f, .35f), iron);
            Cube("Radio display", room, new Vector3(-3.1f, 1.23f, 2.81f), new Vector3(.4f, .09f, .025f), green, false);
            Target(radioObject, HorrorInteraction.Radio, "World/Radio");
            var note = Cube("Duty officer note", room, new Vector3(-4.35f, 1.065f, 2.7f), new Vector3(.65f, .035f, .45f), paper);
            Target(note, HorrorInteraction.Note, "World/Note");
            Table(room, new Vector3(4.6f, 0, 7), new Vector3(1.3f, .8f, 1.1f), wood, iron);
            var key = Cube("Archive key", room, new Vector3(4.45f, .9f, 6.8f), new Vector3(.34f, .09f, .13f), brass);
            var keyCollider = key.GetComponent<BoxCollider>(); keyCollider.size = new Vector3(2, 3, 3);
            Target(key, HorrorInteraction.Key, "World/Key");
            for (int i = 0; i < 3; i++)
            {
                Cube("Bench seat", room, new Vector3(-5.7f, .5f, 6 + i * 2), new Vector3(1.2f, .15f, 1.7f), wood);
                Cube("Bench back", room, new Vector3(-6.1f, 1, 6 + i * 2), new Vector3(.15f, 1, 1.7f), wood);
            }
            for (int i = 0; i < 4; i++)
            {
                float z = -1 + i * 4;
                Cube("Overhead fitting", room, new Vector3(0, 3.87f, z), new Vector3(2, .12f, .3f), warm, false);
                Lamp("Ceiling pool", new Vector3(0, 3.5f, z), new Color(.75f, .83f, .75f), 2.1f, 8);
            }
            Lamp("Desk pool", new Vector3(-3.5f, 2.5f, 2.6f), new Color(1, .66f, .32f), 2.2f, 5);
            Lamp("Key pool", new Vector3(4.5f, 2.6f, 7), new Color(.48f, .8f, .9f), 2, 5);
            Lamp("Archive emergency", new Vector3(0, 2.5f, 13), new Color(1, .12f, .07f), 2, 5);
            var avatar = new GameObject("Investigator"); avatar.layer = 2; avatar.transform.position = new Vector3(0, .1f, -1.5f);
            game.player = avatar.AddComponent<CharacterController>(); game.player.height = 1.8f; game.player.radius = .3f; game.player.center = new Vector3(0, .9f, 0); game.player.stepOffset = .25f;
            var coat = Material("Investigator coat", new Color(.08f, .12f, .16f));
            Cube("Coat", avatar.transform, new Vector3(0, 1, 0), new Vector3(.52f, .8f, .32f), coat, false).layer = 2;
            var head = GameObject.CreatePrimitive(PrimitiveType.Sphere); head.name = "Head"; head.transform.SetParent(avatar.transform); head.transform.localPosition = new Vector3(0, 1.65f, 0); head.transform.localScale = new Vector3(.3f, .35f, .3f); UnityEngine.Object.DestroyImmediate(head.GetComponent<Collider>()); head.GetComponent<Renderer>().sharedMaterial = Material("Skin", new Color(.39f, .29f, .22f)); head.layer = 2;
            foreach (float x in new[] { -.16f, .16f }) Cube("Boot and trouser", avatar.transform, new Vector3(x, .35f, 0), new Vector3(.21f, .7f, .25f), coat, false).layer = 2;
            var camera = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener)); camera.tag = "MainCamera"; game.view = camera.GetComponent<Camera>();
            camera.transform.position = new Vector3(.65f, 2.2f, -4); camera.transform.rotation = Quaternion.Euler(12, 0, 0);
            game.view.fieldOfView = 62; game.view.nearClipPlane = .05f; game.view.farClipPlane = 65; game.view.clearFlags = CameraClearFlags.SolidColor; game.view.backgroundColor = new Color(.02f, .035f, .04f);
            var flash = new GameObject("Flashlight", typeof(Light)); flash.transform.SetParent(camera.transform, false); flash.transform.localPosition = new Vector3(.2f, -.15f, .4f);
            game.flashlight = flash.GetComponent<Light>(); game.flashlight.type = LightType.Spot; game.flashlight.spotAngle = 58; game.flashlight.innerSpotAngle = 30; game.flashlight.intensity = 5; game.flashlight.range = 18; game.flashlight.color = new Color(.8f, .88f, 1); game.flashlight.shadows = LightShadows.Soft;
            var presentation = root.AddComponent<HorrorWorldPresentation>();
            presentation.voiceSource = root.AddComponent<AudioSource>(); presentation.voiceSource.playOnAwake = false; presentation.voiceSource.spatialBlend = 0; presentation.voiceSource.volume = .8f;
            var events = new GameObject("EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM
            events.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
            events.AddComponent<StandaloneInputModule>();
#endif
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(.12f, .16f, .19f); RenderSettings.fog = true; RenderSettings.fogColor = new Color(.025f, .045f, .05f); RenderSettings.fogMode = FogMode.Exponential; RenderSettings.fogDensity = .024f;
            game.interactables = targets.ToArray();
            AssetDatabase.SaveAssets(); EditorSceneManager.SaveScene(scene, ScenePath);
            // Append without changing the existing lab's build order.
            if (!EditorBuildSettings.scenes.Any(s => s.path == ScenePath)) EditorBuildSettings.scenes = EditorBuildSettings.scenes.Concat(new[] { new EditorBuildSettingsScene(ScenePath, true) }).ToArray();
            Selection.activeGameObject = root;
            HorrorArchitectureSetup.Upgrade();
            Debug.Log("Night Shift created: " + ScenePath);
        }
        private static LocalizationConfig Config(string channel, List<LanguageProfile> profiles)
        {
            var config = ScriptableObject.CreateInstance<LocalizationConfig>(); config.resourcesBasePath = "NightShift/" + channel; config.languages = profiles; config.defaultLanguage = config.fallbackLanguage = profiles[0]; config.autoInitialize = false;
            AssetDatabase.CreateAsset(config, Root + "/Data/" + channel + "Config.asset"); return config;
        }
        private static Material Material(string name, Color color, bool emissive = false)
        {
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard")); material.name = name; material.color = color;
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", .25f);
            if (emissive) { material.EnableKeyword("_EMISSION"); material.SetColor("_EmissionColor", color * 2); }
            AssetDatabase.CreateAsset(material, Root + "/Materials/" + name + ".mat"); return material;
        }
        private static GameObject Cube(string name, Transform parent, Vector3 position, Vector3 scale, Material material, bool collider = true)
        {
            var obj = GameObject.CreatePrimitive(PrimitiveType.Cube); obj.name = name; obj.transform.SetParent(parent, false); obj.transform.localPosition = position; obj.transform.localScale = scale; obj.GetComponent<Renderer>().sharedMaterial = material;
            if (!collider) UnityEngine.Object.DestroyImmediate(obj.GetComponent<Collider>()); return obj;
        }
        private static HorrorInteractable Target(GameObject obj, HorrorInteraction kind, string key)
        { var target = obj.AddComponent<HorrorInteractable>(); target.kind = kind; target.textKey = key; targets.Add(target); return target; }
        private static GameObject Sign(string id, Transform parent, Vector3 position, Vector3 scale, Material material)
        {
            var obj = GameObject.CreatePrimitive(PrimitiveType.Quad); obj.name = "Sign " + id;
            obj.transform.SetParent(parent, false); obj.transform.localPosition = position; obj.transform.localScale = scale;
            obj.GetComponent<Renderer>().sharedMaterial = material;
            var target = Target(obj, HorrorInteraction.Sign, "World/Sign" + id); target.surface = obj.GetComponent<Renderer>(); target.textureKey = "Signs/" + id; return obj;
        }
        private static void Table(Transform room, Vector3 position, Vector3 size, Material top, Material leg)
        {
            Cube("Table top", room, position + Vector3.up * size.y, new Vector3(size.x, .12f, size.z), top);
            foreach (float x in new[] { -.4f, .4f }) foreach (float z in new[] { -.35f, .35f }) Cube("Table leg", room, position + new Vector3(x * size.x, size.y / 2, z * size.z), new Vector3(.1f, size.y, .1f), leg);
        }
        private static void Lamp(string name, Vector3 position, Color color, float intensity, float range)
        { var obj = new GameObject(name, typeof(Light)); obj.transform.position = position; var light = obj.GetComponent<Light>(); light.type = LightType.Point; light.color = color; light.intensity = intensity; light.range = range; }
    }
}



