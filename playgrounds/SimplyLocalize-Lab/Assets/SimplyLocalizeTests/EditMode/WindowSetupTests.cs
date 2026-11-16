using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using SimplyLocalize.Editor;
using SimplyLocalize.Editor.Windows;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace SimplyLocalize.Tests
{
    public class WindowSetupTests
    {
        private const string Root = "Assets/SimplyLocalize";
        private LocalizationEditorWindow _window;
        private UnityEngine.Object _selection;
        private string _other;

        [SetUp] public void Setup()
        {
            _other = null;
            _window = null;
            if (Directory.Exists(Root) || File.Exists(Root + ".meta")) Assert.Ignore("Existing project setup; use the dedicated lab.");
            _other = "Assets/WindowSetup_" + Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(_other);
            AssetDatabase.Refresh();
            _selection = Selection.activeObject;
        }

        [TearDown] public void Cleanup()
        {
            if (_other == null) return;
            if (_window != null) _window.Close();
            AssetDatabase.DeleteAsset(Root);
            AssetDatabase.DeleteAsset(_other);
            _other = null;
            Selection.activeObject = _selection;
        }

        private LocalizationConfig Config() => LocalizationProjectSetup.Configure(new[] { "en", "ru" }, "en", "en", true).Config;
        private void Open(LocalizationConfig config)
        {
            _window = ScriptableObject.CreateInstance<LocalizationEditorWindow>();
            _window.ShowUtility();
            _window.rootVisualElement.Q<ObjectField>("configField").value = config;
        }
        private T Field<T>(string name) where T : VisualElement => _window.rootVisualElement.Q<T>(name);
        private void Click(string name)
        {
            var button = Field<Button>(name);
            Assert.That(button, Is.Not.Null, name);
            Assert.That(button.enabledInHierarchy, Is.True, name);
            using var submit = NavigationSubmitEvent.GetPooled();
            submit.target = button;
            button.SendEvent(submit);
        }

        [Test] public void SetupFromWindow_SelectsLanguagesWithoutChangingSelectionOrExistingText()
        {
            var config = Config();
            string text = Root + "/Resources/SimplyLocalize/Translations/en/text/main.json";
            File.WriteAllText(text, "{\"translations\":{\"play\":\"Play externally edited\"}}");
            byte[] original = File.ReadAllBytes(text);
            Open(config);
            Field<ObjectField>("configField").value = null;
            var selected = Selection.activeObject;
            Field<TextField>("setupLanguages").value = "en,ru,pl";
            Click("createConfig");
            Assert.That(Field<TextField>("addLanguageCodes"), Is.Not.Null);
            Assert.That(config.GetLanguageCodes(), Is.EqualTo(new[] { "en", "ru", "pl" }));
            Assert.That(Selection.activeObject, Is.SameAs(selected));
            Assert.That(File.ReadAllBytes(text), Is.EqualTo(original));
        }

        [Test] public void InvalidSetup_ShowsErrorInWindowAndDoesNotCreateFiles()
        {
            Open(null);
            Field<TextField>("setupLanguages").value = "../ru";
            Click("createConfig");
            Assert.That(Field<HelpBox>("setupMessage").text, Is.Not.Empty);
            Assert.That(Field<HelpBox>("setupMessage").style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(Directory.Exists(Root), Is.False);
        }

        [Test] public void AddLanguages_IsRepeatableAndDefaultsAreEditableInWindow()
        {
            var config = Config(); Open(config); Click("tabLanguages");
            Field<TextField>("addLanguageCodes").value = "pl,de"; Click("addLanguages");
            Field<TextField>("addLanguageCodes").value = "pl,de"; Click("addLanguages");
            Assert.That(config.languages.Count, Is.EqualTo(4));
            Assert.That(Field<HelpBox>("languageMessage").text, Does.Contain("Created 0"));
            Field<PopupField<LanguageProfile>>("defaultLanguage").value = config.GetProfile("ru");
            Field<PopupField<LanguageProfile>>("fallbackLanguage").value = config.GetProfile("pl");
            Assert.That(config.DefaultLanguageCode, Is.EqualTo("ru"));
            Assert.That(config.FallbackLanguageCode, Is.EqualTo("pl"));
            Assert.That(EditorUtility.IsDirty(config), Is.False);
            AssetDatabase.ImportAsset(LocalizationProjectSetup.ConfigAssetPath, ImportAssetOptions.ForceUpdate);
            Assert.That(config.DefaultLanguageCode, Is.EqualTo("ru"));
            Assert.That(config.FallbackLanguageCode, Is.EqualTo("pl"));
        }

        [Test] public void InvalidLanguage_ShowsInlineErrorAndPreservesConfig()
        {
            var config = Config(); Open(config); Click("tabLanguages");
            Field<TextField>("addLanguageCodes").value = "pl,../de"; Click("addLanguages");
            Assert.That(config.GetLanguageCodes(), Is.EqualTo(new[] { "en", "ru" }));
            Assert.That(Field<HelpBox>("languageMessage").messageType, Is.EqualTo(HelpBoxMessageType.Error));
        }

        [Test] public void ReadOnlyConfig_DefaultChangeShowsErrorAndKeepsPreviousValue()
        {
            var config = Config(); Open(config); Click("tabLanguages");
            string path = LocalizationProjectSetup.ConfigAssetPath;
            var attributes = File.GetAttributes(path);
            File.SetAttributes(path, attributes | FileAttributes.ReadOnly);
            try
            {
                Field<PopupField<LanguageProfile>>("defaultLanguage").value = config.GetProfile("ru");
                Assert.That(config.DefaultLanguageCode, Is.EqualTo("en"));
                Assert.That(Field<HelpBox>("languageMessage").messageType, Is.EqualTo(HelpBoxMessageType.Error));
                Assert.That(EditorUtility.IsDirty(config), Is.False);
            }
            finally { File.SetAttributes(path, attributes); }
        }

        [Test] public void EditProfile_OpensCorrectProfileInsideSameWindow()
        {
            var config = Config(); Open(config); Click("tabLanguages");
            var selected = Selection.activeObject;
            Click("editProfile_ru");
            Assert.That(Field<PopupField<string>>("profileSelector").index, Is.EqualTo(1));
            Assert.That(Selection.activeObject, Is.SameAs(selected));
        }

        [Test] public void Refocus_PreservesLanguageDraftAndSelectedProfile()
        {
            var config = Config(); Open(config); Click("tabLanguages");
            Field<TextField>("addLanguageCodes").value = "pl,de";
            var focus = typeof(LocalizationEditorWindow).GetMethod("OnFocus", BindingFlags.Instance | BindingFlags.NonPublic);
            focus.Invoke(_window, null);
            Assert.That(Field<TextField>("addLanguageCodes").value, Is.EqualTo("pl,de"));
            Click("editProfile_ru");
            focus.Invoke(_window, null);
            Assert.That(Field<PopupField<string>>("profileSelector").index, Is.EqualTo(1));
        }

        [Test] public void Settings_MigratesManualConfigAndPreservesPendingChangesAndGuid()
        {
            var config = ScriptableObject.CreateInstance<LocalizationConfig>();
            string oldPath = _other + "/GameLocalization.asset";
            AssetDatabase.CreateAsset(config, oldPath);
            LocalizationProjectSetup.AddLanguages(config, "en");
            config.defaultLanguage = config.languages[0];
            config.enableLogging = true;
            string guid = AssetDatabase.AssetPathToGUID(oldPath);
            Open(config); Click("tabSettings"); Click("useForAutomaticStartup");
            Assert.That(AssetDatabase.GetAssetPath(config), Is.EqualTo(LocalizationProjectSetup.ConfigAssetPath));
            Assert.That(AssetDatabase.AssetPathToGUID(LocalizationProjectSetup.ConfigAssetPath), Is.EqualTo(guid));
            Assert.That(config.name, Is.EqualTo("SimplyLocalizeSettings"));
            Assert.That(config.DefaultLanguageCode, Is.EqualTo("en"));
            Assert.That(config.enableLogging, Is.True);
            Assert.That(config.autoInitialize, Is.True);
            Assert.That(Field<HelpBox>("startupMessage").messageType, Is.EqualTo(HelpBoxMessageType.Info));
            Assert.That(Field<Button>("useForAutomaticStartup"), Is.Null);
            AssetDatabase.ImportAsset(LocalizationProjectSetup.ConfigAssetPath, ImportAssetOptions.ForceUpdate);
            Assert.That(config.name, Is.EqualTo("SimplyLocalizeSettings"));
        }

        [Test] public void MigrationConflict_ShowsErrorWithoutOverwritingEitherConfig()
        {
            var startup = Config();
            var old = ScriptableObject.CreateInstance<LocalizationConfig>();
            string path = _other + "/Old.asset";
            AssetDatabase.CreateAsset(old, path);
            Open(old); Click("tabSettings"); Click("useForAutomaticStartup");
            Assert.That(AssetDatabase.GetAssetPath(old), Is.EqualTo(path));
            Assert.That(old.autoInitialize, Is.False);
            Assert.That(AssetDatabase.LoadAssetAtPath<LocalizationConfig>(LocalizationProjectSetup.ConfigAssetPath), Is.SameAs(startup));
            Assert.That(Field<HelpBox>("startupMessage").messageType, Is.EqualTo(HelpBoxMessageType.Error));
        }

        [Test] public void OpeningWindow_PrefersStartupConfigOverOtherConfigs()
        {
            var config = Config();
            var other = ScriptableObject.CreateInstance<LocalizationConfig>();
            AssetDatabase.CreateAsset(other, _other + "/Other.asset");
            _window = ScriptableObject.CreateInstance<LocalizationEditorWindow>();
            _window.ShowUtility();
            Assert.That(Field<ObjectField>("configField").value, Is.SameAs(config));
        }
    }
}
