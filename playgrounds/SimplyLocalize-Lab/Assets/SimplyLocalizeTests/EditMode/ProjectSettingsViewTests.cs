using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using SimplyLocalize.Editor;
using SimplyLocalize.Editor.Data;
using SimplyLocalize.Editor.Windows;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace SimplyLocalize.Tests
{
    public class ProjectSettingsViewTests
    {
        private string root;
        private LocalizationProjectConfig config;
        private EditorWindow host;
        private HashSet<int> priorUndo;
        [SetUp] public void Setup()
        {
            root = "Assets/SimplyLocalizeSettingsView_" + Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(root); AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            config = LocalizationProjectAuthoring.Create(new[] { "en", "ru" }, "en", "en", root + "/Project.asset", false);
            priorUndo = new HashSet<int>(Resources.FindObjectsOfTypeAll<LocalizationProjectUndo>().Select(r => r.GetInstanceID()));
            host = ScriptableObject.CreateInstance<SettingsHost>(); host.ShowUtility();
        }
        [TearDown] public void Cleanup()
        {
            if (host != null) host.Close();
            if (priorUndo != null) foreach (var record in Resources.FindObjectsOfTypeAll<LocalizationProjectUndo>().Where(r => !priorUndo.Contains(r.GetInstanceID()))) record.Dispose();
            AssetDatabase.DeleteAsset(root);
        }
        private void Open(Action<string> languages = null)
        { host.rootVisualElement.Clear(); host.rootVisualElement.Add(new ProjectSettingsView(config, editLanguages: languages)); }
        private T Field<T>(string name) where T : VisualElement => host.rootVisualElement.Q<T>(name);
        private void Click(string name)
        { var button = Field<Button>(name); Assert.That(button, Is.Not.Null); using var submit = NavigationSubmitEvent.GetPooled(); submit.target = button; button.SendEvent(submit); }
        private void Save() { EditorUtility.SetDirty(config); AssetDatabase.SaveAssetIfDirty(config); }

        [Test] public void NewChannelIsIndependentAndChangingItsModeOrNameKeepsIdentity()
        {
            Open(); Field<TextField>("newChannelName").value = "Voice"; Click("addChannel");
            var voice = config.Channels.Single(c => c.Name == "Voice"); string id = voice.Id;
            Assert.That(voice.Mode, Is.EqualTo(LocalizationChannelMode.Independent)); Assert.That(voice.SetIds, Is.Empty);
            Field<TextField>("channelName_" + id).value = "Speech";
            Field<EnumField>("channelMode_" + id).value = LocalizationChannelMode.FollowMain;
            Assert.That(config.Channels.Single(c => c.Id == id).Name, Is.EqualTo("Speech"));
            Assert.That(config.Channels.Single(c => c.Id == id).Mode, Is.EqualTo(LocalizationChannelMode.FollowMain));
            Assert.That(Field<EnumField>("channelMode_" + config.MainChannelId), Is.Null);
            Undo.PerformUndo(); Assert.That(config.Channels.Single(c => c.Id == id).Mode, Is.EqualTo(LocalizationChannelMode.Independent));
        }
        [Test] public void SetMembershipDefaultAndStartupAreIndependentPoliciesAndUndoRefreshesUi()
        {
            Open(); Field<TextField>("newSetName").value = "Chapter"; Click("addSet");
            string set = config.Sets.Single(s => s.Name == "Chapter").Id, main = config.MainChannelId;
            Assert.That(config.MainChannel.SetIds, Does.Not.Contain(set));
            Field<Toggle>("includeSet_" + main + "_" + set).value = true;
            Assert.That(config.MainChannel.StartupSetIds, Does.Not.Contain(set));
            Field<DropdownField>("defaultSet_" + main).value = set;
            Field<Toggle>("startupSet_" + main + "_" + set).value = true;
            Assert.That(config.MainChannel.StartupSetIds, Does.Contain(set));
            Undo.PerformUndo(); Assert.That(Field<Toggle>("startupSet_" + main + "_" + set).value, Is.False);
            Field<Toggle>("includeSet_" + main + "_" + set).value = false;
            Assert.That(config.MainChannel.SetIds, Does.Contain(set)); Assert.That(Field<HelpBox>("settingsMessage").text, Does.Contain("default"));
        }
        [Test] public void RemovingBoundSetIsRejectedAndRemovingChannelKeepsSetsAndFiles()
        {
            string common = config.Sets[0].Id, voice = config.AddChannel("Voice", new[] { "en" }).Id;
            config.Channels.Single(c => c.Id == voice).IncludeSet(common); Save(); Open();
            Click("removeSet_" + common); Assert.That(config.Sets.Count, Is.EqualTo(1)); Assert.That(Field<HelpBox>("settingsMessage"), Is.Not.Null);
            Click("removeChannel_" + voice); Assert.That(config.Channels.Count, Is.EqualTo(1)); Assert.That(config.Sets[0].Id, Is.EqualTo(common));
            Assert.That(File.Exists(root + "/Resources/SimplyLocalize/Translations/en/Common.json"), Is.True);
            Assert.That(Field<Button>("removeChannel_" + config.MainChannelId), Is.Null);
        }
        [Test] public void SourceConnectionValidatesResourcesAndRejectsCaseAliasesWithoutOverwriting()
        {
            string set = config.Sets[0].Id; Open();
            Field<TextField>("sourceAddress_" + set).value = "SimplyLocalize/Translations/en/Common".ToUpperInvariant(); Click("addSource_" + set);
            Assert.That(Field<HelpBox>("settingsMessage").text, Does.Contain("already connected"));
            Assert.That(config.Sets[0].Sources.Single(s => s.Language == "en").Sources.Count, Is.EqualTo(1));
            Field<TextField>("sourceAddress_" + set).value = "DefinitelyMissing"; Click("addSource_" + set);
            Assert.That(Field<HelpBox>("settingsMessage").text, Does.Contain("Missing"));
            Field<TextField>("sourceProvider_" + set).value = "game-mods";
            Field<TextField>("sourceAddress_" + set).value = "external-dialogue"; Click("addSource_" + set);
            Assert.That(config.Sets[0].Sources.Single(s => s.Language == "en").Sources.Last().ProviderId, Is.EqualTo("game-mods"));
            Undo.PerformUndo(); Assert.That(config.Sets[0].Sources.Single(s => s.Language == "en").Sources.Count, Is.EqualTo(1));
        }
        [Test] public void ResourcesAssetSourceChecksTypeWithoutRegisteringAJsonAsAssetTable()
        {
            string set = config.Sets[0].Id; Open();
            Field<DropdownField>("sourceKind_" + set).value = "Assets";
            Field<TextField>("sourceAddress_" + set).value = "SimplyLocalize/Translations/en/Common"; Click("addSource_" + set);
            Assert.That(Field<HelpBox>("settingsMessage").text, Does.Contain("LocalizationAssetTable"));
            var table = ScriptableObject.CreateInstance<LocalizationAssetTable>();
            string path = root + "/Resources/" + Path.GetFileName(root) + ".asset";
            AssetDatabase.CreateAsset(table, path); AssetDatabase.SaveAssetIfDirty(table);
            Field<DropdownField>("sourceKind_" + set).value = "Assets";
            Field<TextField>("sourceAddress_" + set).value = Path.GetFileName(root); Click("addSource_" + set);
            Assert.That(config.Sets[0].Sources.Single(s => s.Language == "en").Sources.Last().Kind, Is.EqualTo(LocalizationSourceKind.AssetTable));
        }
        [Test] public void LanguageActionTargetsChannelWithoutSelectingInspectorAndInitUndoWorks()
        {
            string selected = null; var before = Selection.activeObject; Open(id => selected = id);
            Click("editLanguages_" + config.MainChannelId); Assert.That(selected, Is.EqualTo(config.MainChannelId)); Assert.That(Selection.activeObject, Is.SameAs(before));
            Field<Toggle>("autoDetectLanguage").value = true; Assert.That(config.AutoDetectLanguage, Is.True);
            Undo.PerformUndo(); Assert.That(Field<Toggle>("autoDetectLanguage").value, Is.False);
        }
        private sealed class SettingsHost : EditorWindow { }
    }
}
