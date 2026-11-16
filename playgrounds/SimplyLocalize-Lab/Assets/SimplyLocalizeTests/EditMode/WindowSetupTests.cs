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
            try
            {
                ClosePicker();
                if (_window != null) _window.Close();
            }
            finally
            {
                AssetDatabase.DeleteAsset(Root);
                AssetDatabase.DeleteAsset(_other);
                _other = null;
                Selection.activeObject = _selection;
            }
        }

        private LocalizationConfig Config() => LocalizationProjectSetup.Configure(new[] { "en", "ru" }, "en", "en", true).Config;
        private void Open(LocalizationConfig config)
        {
            _window = ScriptableObject.CreateInstance<LocalizationEditorWindow>();
            _window.ShowUtility();
            _window.rootVisualElement.Q<ObjectField>("configField").value = config;
        }
        private EditorWindow Picker() => Resources.FindObjectsOfTypeAll<EditorWindow>().FirstOrDefault(w => w.GetType().Name == "LanguagePickerPopup");
        private void ClosePicker() { var picker = Picker(); if (picker != null) picker.Close(); }
        private T Field<T>(string name) where T : VisualElement
        {
            var field = _window.rootVisualElement.Q<T>(name);
            if (field != null) return field;
            if (Picker() == null)
            {
                if (_window.rootVisualElement.Q<Button>("openLanguagePicker") == null) return null;
                Click("openLanguagePicker");
            }
            return Picker()?.rootVisualElement.Q<T>(name);
        }
        private void Click(string name)
        {
            var main = _window.rootVisualElement.Q<Button>(name);
            if (main != null && name != "openLanguagePicker") ClosePicker();
            var button = main ?? Field<Button>(name);
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
            Field<TextField>("languageSearch").value = "pl";
            Click("addSelectedLanguage");
            Click("createConfig");
            Assert.That(_window.rootVisualElement.Q<Button>("openLanguagePicker"), Is.Not.Null);
            Assert.That(config.GetLanguageCodes(), Is.EqualTo(new[] { "en", "ru", "pl" }));
            Assert.That(Selection.activeObject, Is.SameAs(selected));
            Assert.That(File.ReadAllBytes(text), Is.EqualTo(original));
        }

        [Test] public void InvalidSetup_ShowsErrorInWindowAndDoesNotCreateFiles()
        {
            Open(null);
            Click("openCustomLanguage");
            Field<TextField>("customLanguageCode").value = "../ru";
            Field<TextField>("customLanguageName").value = "Invalid";
            Click("addCustomLanguage");
            Assert.That(Field<HelpBox>("languagePickerMessage").text, Is.Not.Empty);
            Assert.That(Field<HelpBox>("languagePickerMessage").style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(Directory.Exists(Root), Is.False);
        }

        [Test] public void AddLanguages_IsRepeatableAndDefaultsAreEditableInWindow()
        {
            var config = Config(); Open(config); Click("tabLanguages");
            Field<TextField>("languageSearch").value = "pl"; Click("addSelectedLanguage");
            Field<TextField>("languageSearch").value = "de"; Click("addSelectedLanguage");
            Assert.That(Field<Button>("addSelectedLanguage").enabledSelf, Is.False);
            ClosePicker();
            Assert.That(config.languages.Count, Is.EqualTo(4));
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
            Click("openCustomLanguage");
            Field<TextField>("customLanguageCode").value = "../de";
            Field<TextField>("customLanguageName").value = "Bad";
            Click("addCustomLanguage");
            Assert.That(config.GetLanguageCodes(), Is.EqualTo(new[] { "en", "ru" }));
            Assert.That(Field<HelpBox>("languagePickerMessage").messageType, Is.EqualTo(HelpBoxMessageType.Error));
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
            Field<TextField>("languageSearch").value = "pl";
            var focus = typeof(LocalizationEditorWindow).GetMethod("OnFocus", BindingFlags.Instance | BindingFlags.NonPublic);
            focus.Invoke(_window, null);
            Assert.That(Field<TextField>("languageSearch").value, Is.EqualTo("pl"));
            ClosePicker();
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


        [TestCase("Ан", "en")]
        [TestCase("ру", "ru")]
        [TestCase("ру", "ro")]
        [TestCase("English", "en")]
        [TestCase("francais", "fr")]
        [TestCase("rus", "ru")]
        [TestCase("pt-BR", "pt-BR")]
        public void LanguageSearch_FindsNamesAndStandardCodes(string query, string expected)
        {
            Open(null);
            Field<TextField>("languageSearch").value = query;
            var results = Field<ListView>("languageResults").itemsSource.Cast<string>().ToArray();
            Assert.That(results, Does.Contain(expected));
        }

        [Test] public void LanguageCatalog_IsLargeAndExactCodeComesFirst()
        {
            Open(null);
            Assert.That(Field<ListView>("languageResults").itemsSource.Count, Is.GreaterThan(600));
            Field<TextField>("languageSearch").value = "ro";
            Assert.That(Field<ListView>("languageResults").itemsSource[0], Is.EqualTo("ro"));
        }

        [Test] public void ShortSearch_PrefersNamePrefixesOverUnrelatedSubstrings()
        {
            Open(null);
            Field<TextField>("languageSearch").value = "Ан";
            var results = Field<ListView>("languageResults").itemsSource.Cast<string>().ToArray();
            Assert.That(results.Take(4), Does.Contain("en"));
            Assert.That(results, Does.Not.Contain("ada"));
            Field<TextField>("languageSearch").value = "Рум";
            Assert.That(Field<ListView>("languageResults").itemsSource[0], Is.EqualTo("ro"));
        }

        [Test] public void DefaultAndFallback_OnlyUseSelectedLanguagesAndRepairAfterRemoval()
        {
            Open(null);
            Field<TextField>("languageSearch").value = "ro"; Click("addSelectedLanguage");
            Assert.That(Field<Button>("addSelectedLanguage").enabledSelf, Is.False);
            Assert.That(Field<DropdownField>("setupDefault").choices, Is.EqualTo(new[] { "en", "ru", "ro" }));
            Field<DropdownField>("setupDefault").value = "ro";
            Field<DropdownField>("setupFallback").value = "ro";
            Click("removeLanguage_ro");
            Assert.That(Field<DropdownField>("setupDefault").value, Is.EqualTo("en"));
            Assert.That(Field<DropdownField>("setupFallback").value, Is.EqualTo(""));
            Click("removeLanguage_en"); Click("removeLanguage_ru");
            Assert.That(Field<Button>("createConfig").enabledSelf, Is.False);
            Assert.That(Field<DropdownField>("setupDefault").choices, Is.Empty);
        }

        [TestCase(" PT_br ", "pt-BR")]
        [TestCase("zh_hans", "zh-Hans")]
        [TestCase("RUS", "ru")]
        [TestCase("eng_us", "en-US")]
        [TestCase("x-ELVISH", "x-elvish")]
        [TestCase("aaa", "aaa")]
        public void CustomLanguage_NormalizesStandardCodeAndAvoidsDuplicates(string input, string expected)
        {
            Open(null);
            Click("openCustomLanguage");
            Field<TextField>("customLanguageCode").value = input;
            Field<TextField>("customLanguageName").value = "Custom display name";
            Click("addCustomLanguage"); Click("addCustomLanguage");
            Assert.That(Field<TextField>("customLanguageCode").value, Is.EqualTo(expected));
            Assert.That(Field<DropdownField>("setupDefault").choices.Count(c => c == expected), Is.EqualTo(1));
        }

        [TestCase("English")]
        [TestCase("рус")]
        [TestCase("../en")]
        [TestCase("en/US")]
        [TestCase("xx")]
        [TestCase("en-QQ")]
        [TestCase("en-Abcd")]
        [TestCase("en-unknown")]
        [TestCase("sl-rozaj-rozaj")]
        public void InvalidCustomCode_IsRejectedBeforeAnyWrites(string input)
        {
            Open(null);
            Click("openCustomLanguage");
            Field<TextField>("customLanguageCode").value = input;
            Field<TextField>("customLanguageName").value = "Bad language";
            Click("addCustomLanguage");
            Assert.That(Field<HelpBox>("languagePickerMessage").style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(Field<DropdownField>("setupDefault").choices, Is.EqualTo(new[] { "en", "ru" }));
            Assert.That(Directory.Exists(Root), Is.False);
        }

        [Test] public void SetupSelection_CreatesProfileMetadataAndPreservesExistingProfiles()
        {
            var config = Config(); config.GetProfile("en").displayName = "My English";
            EditorUtility.SetDirty(config.GetProfile("en")); AssetDatabase.SaveAssetIfDirty(config.GetProfile("en"));
            Open(config); Field<ObjectField>("configField").value = null;
            Field<TextField>("languageSearch").value = "ro"; Click("addSelectedLanguage");
            Click("openCustomLanguage");
            Field<TextField>("customLanguageCode").value = "x-elvish";
            Field<TextField>("customLanguageName").value = "Elvish";
            Click("addCustomLanguage"); Click("createConfig");
            Assert.That(config.GetProfile("ro").displayName, Is.Not.EqualTo("ro"));
            Assert.That(config.GetProfile("ro").systemLanguage, Is.EqualTo(SystemLanguage.Romanian));
            Assert.That(config.GetProfile("x-elvish").displayName, Is.EqualTo("Elvish"));
            Assert.That(config.GetProfile("en").displayName, Is.EqualTo("My English"));
        }

        [Test] public void SetupSelection_NoneFallbackClearsExistingFallback()
        {
            var config = Config(); Open(config); Field<ObjectField>("configField").value = null;
            Field<DropdownField>("setupFallback").value = "";
            Click("createConfig");
            Assert.That(config.fallbackLanguage, Is.Null);
            Assert.That(EditorUtility.IsDirty(config), Is.False);
        }


        [Test] public void SetupAndLanguages_UseTheSameCompactPopup()
        {
            var config = Config(); Open(config); Field<ObjectField>("configField").value = null;
            Assert.That(_window.rootVisualElement.Q<TextField>("languageSearch"), Is.Null);
            Click("openLanguagePicker");
            var type = Picker().GetType();
            Assert.That(Field<Button>("openCustomLanguage").text, Is.EqualTo("Add a custom language"));
            ClosePicker();
            Field<ObjectField>("configField").value = config; Click("tabLanguages");
            Assert.That(_window.rootVisualElement.Q<TextField>("languageSearch"), Is.Null);
            Assert.That(_window.rootVisualElement.Q<TextField>("addLanguageCodes"), Is.Null);
            Click("openLanguagePicker");
            Assert.That(Picker().GetType(), Is.SameAs(type));
            Field<TextField>("languageSearch").value = "ro"; Click("addSelectedLanguage");
            Assert.That(config.GetProfile("ro").displayName, Is.Not.EqualTo("ro"));
            Assert.That(config.GetProfile("ro").systemLanguage, Is.EqualTo(SystemLanguage.Romanian));
        }

        [Test] public void LanguagesPopup_CustomLanguagePreservesDefaultsAndExistingTranslations()
        {
            var config = Config(); Open(config); Click("tabLanguages");
            var english = config.GetProfile("en");
            string text = Root + "/Resources/SimplyLocalize/Translations/en/text/main.json";
            File.WriteAllText(text, "{\"translations\":{\"play\":\"Keep me\"}}");
            byte[] before = File.ReadAllBytes(text);
            Click("openCustomLanguage");
            Field<TextField>("customLanguageCode").value = "x-ELVISH";
            Field<TextField>("customLanguageName").value = "Elvish";
            Click("addCustomLanguage"); Click("addCustomLanguage");
            Assert.That(config.languages.Count, Is.EqualTo(3));
            Assert.That(config.GetProfile("x-elvish").displayName, Is.EqualTo("Elvish"));
            Assert.That(config.defaultLanguage, Is.SameAs(english));
            Assert.That(config.fallbackLanguage, Is.SameAs(english));
            Assert.That(File.ReadAllBytes(text), Is.EqualTo(before));
        }

        [Test] public void LanguagesPopup_ReadOnlyConfigLeavesNoPartialLanguageAndAllowsRetry()
        {
            var config = Config(); Open(config); Click("tabLanguages");
            Field<TextField>("languageSearch").value = "ro";
            string path = LocalizationProjectSetup.ConfigAssetPath;
            var attrs = File.GetAttributes(path); File.SetAttributes(path, attrs | FileAttributes.ReadOnly);
            try
            {
                Click("addSelectedLanguage");
                Assert.That(Field<HelpBox>("languagePickerMessage").messageType, Is.EqualTo(HelpBoxMessageType.Error));
                Assert.That(config.GetProfile("ro"), Is.Null);
                Assert.That(File.Exists(Root + "/Languages/ro.asset"), Is.False);
            }
            finally { File.SetAttributes(path, attrs); }
            Click("addSelectedLanguage");
            Assert.That(config.GetProfile("ro"), Is.Not.Null);
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
