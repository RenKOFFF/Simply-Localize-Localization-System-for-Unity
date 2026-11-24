using System;
using System.IO;
using System.Linq;
using SimplyLocalize;
using SimplyLocalize.Components;
using SimplyLocalize.Editor;
using SimplyLocalize.Editor.Data;
using SimplyLocalize.Editor.Windows;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;
#if TEST_ADDRESSABLES
using SimplyLocalize.Integrations.Editor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Settings;
#endif

// Run only in a disposable project. The same scene and assertions exercise both distributions.
public static class ChapterAudioBuild
{
    public static void Run()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var config = LocalizationProjectAuthoring.Create(new[] { "en", "ru" }, "en", "en");
        var common = config.Sets[0];
        common.AddEntry("Diary/Subtitle");
        foreach (var language in new[] { "en", "ru" })
        {
            string address = common.Sources.Single(s => s.Language == language).Sources.Single().Address;
            string json = "Assets/SimplyLocalize/Resources/" + address + ".json";
            File.WriteAllText(json, "{\"translations\":{\"Diary/Subtitle\":\"" + (language == "en" ? "Hello" : "Привет") + "\"}}");
            AssetDatabase.ImportAsset(json);
        }
        var voice = config.AddChannel("Voice", new[] { "en" });
        voice.Mode = LocalizationChannelMode.Independent;
        var chapter = config.AddSet("Chapter one");
        voice.IncludeSet(chapter.Id);
        var line = chapter.AddEntry("Diary/Voice", LocalizationEntryKind.Asset);
        EditorUtility.SetDirty(config);
        AssetDatabase.SaveAssetIfDirty(config);
        var view = new ProjectSettingsView(config);
        var create = view.Q<Button>("createAddressableTable_" + chapter.Id);
#if TEST_ADDRESSABLES
        Require(create != null, "Addressables action missing from Settings");
        string path = "Assets/ChapterVoice.asset";
        AddressableTableAuthoring.CreateTable(config, chapter.Id, "en", path);
        var source = config.Sets.Single(s => s.Id == chapter.Id).Sources.Single().Sources.Single();
        var table = AssetDatabase.LoadAssetAtPath<LocalizationAssetTable>(path);
        Require(AddressableAssetSettingsDefaultObject.Settings.FindAssetEntry(source.Address).AssetPath == path, "Wrong catalog GUID");
        bool duplicate = false;
        try { AddressableTableAuthoring.ConnectTable(config, chapter.Id, "en", table); }
        catch (InvalidOperationException) { duplicate = true; }
        Require(duplicate, "Repeated connection must keep the original table");
        bool refused = false;
        try { AddressableTableAuthoring.CreateTable(config, chapter.Id, "en", path); }
        catch (IOException) { refused = true; }
        Require(refused, "Creating a table must preserve existing files");
#else
        Require(create == null, "Unexpected Addressables action");
        Require(!UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages().Any(p => p.name == "com.unity.addressables"), "Unexpected dependency");
        LocalizationProjectAuthoring.CreateAssetTable(config, chapter.Id, "en");
#endif
        var importer = (AudioImporter)AssetImporter.GetAtPath("Assets/Narration.wav");
        var audioSettings = importer.defaultSampleSettings;
        audioSettings.preloadAudioData = false;
        importer.defaultSampleSettings = audioSettings;
        importer.SaveAndReimport();
        var clip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Narration.wav");
        Require(clip != null, "Missing imported audio fixture");
        using (var edit = new LocalizationProjectAssetEdit(config))
        {
            edit.SetAsset(line.Id, "en", clip);
            edit.Commit();
        }
        var data = EditorProjectAssetData.Load(config, voice.Id, chapter.Id, LocalizationEditorReaders.Assets);
        Require(data.TryGetDirect(line.Id, "en", out var cell) && cell.Asset == clip, "Window table reader returned wrong clip");
        var driver = new GameObject("Probe").AddComponent<ChapterAudioProbe>();
#if TEST_ADDRESSABLES
        driver.tableGuid = source.Address;
#endif
        driver.channel = voice.Id;
        driver.set = chapter.Id;
        driver.first = new GameObject("Chapter");
        driver.second = new GameObject("Overlapping chapter owner");
        foreach (var owner in new[] { driver.first, driver.second })
        {
            owner.SetActive(false);
            ProjectSettingsView.BindChapter(owner, voice.Id, chapter.Id);
            ProjectSettingsView.BindChapter(owner, voice.Id, chapter.Id);
            Require(owner.GetComponents<LocalizedContentSetScope>().Length == 1, "Duplicate binding");
        }
        var sound = driver.first.AddComponent<AudioSource>();
        sound.playOnAwake = false;
        var binding = driver.first.AddComponent<LocalizedAudioClip>();
        binding.ChannelId = voice.Id;
        binding.SetId = chapter.Id;
        binding.Key = line.Key;
        driver.source = sound;
        Require(EditorSceneManager.SaveScene(scene, "Assets/Chapter.unity"), "Save scene failed");
#if TEST_ADDRESSABLES
        AddressableAssetSettings.BuildPlayerContent(out AddressablesPlayerBuildResult content);
        Require(string.IsNullOrEmpty(content.Error), "Addressables build failed: " + content.Error);
        AddressableAssetSettingsDefaultObject.Settings.BuildAddressablesWithPlayerBuild =
            AddressableAssetSettings.PlayerBuildOption.DoNotBuildWithPlayer;
#endif
        var report = BuildPipeline.BuildPlayer(new[] { "Assets/Chapter.unity" }, "Build/Chapter.exe",
            BuildTarget.StandaloneWindows64, BuildOptions.None);
        Require(report.summary.result == BuildResult.Succeeded, "Windows build failed");
    }

    private static void Require(bool passed, string message)
    {
        if (!passed) throw new InvalidOperationException(message);
    }
}
