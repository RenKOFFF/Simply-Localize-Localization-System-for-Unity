using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using SimplyLocalize.Pluralization;
using TMPro;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SimplyLocalize.Tests
{
    public class ProjectConfigurationTests
    {
        private LocalizationProjectConfig config;
        private string root;

        [SetUp] public void Setup() => config = LocalizationProjectConfig.Create(new[] { "en", "ru" });
        [TearDown] public void Cleanup()
        {
            if (root != null) AssetDatabase.DeleteAsset(root);
            if (config != null) Object.DestroyImmediate(config);
            root = null;
        }
        private string[] Errors() => config.Validate().Where(i => i.Severity == LocalizationIssueSeverity.Error).Select(i => i.Code).ToArray();

        [Test] public void SimpleProjectHasMainCommonAndNoMutablePlayerState()
        {
            Assert.That(Errors(), Is.Empty);
            Assert.That(config.MainChannel.Name, Is.EqualTo("Main"));
            Assert.That(config.Sets.Single().Name, Is.EqualTo("Common"));
            Assert.That(config.MainChannel.StartupSetIds, Is.EquivalentTo(new[] { config.Sets[0].Id }));
            Assert.Throws<NotSupportedException>(() => ((IList<LocalizationChannelDefinition>)config.Channels).Clear());
            Assert.Throws<ArgumentException>(() => config.AddLanguage("EN"));
            Assert.That(config.Languages.Count, Is.EqualTo(2));
        }

        [Test] public void RenameMoveAndDeleteKeepIdentityWithoutRetargetingOldReferences()
        {
            var set = config.Sets[0];
            var entry = set.AddEntry("menu/play");
            var reference = new LocalizationEntryReference(config.MainChannelId, entry.Id);
            var original = config.CreateEntryIndex();
            set.Name = "Interface"; config.MainChannel.Name = "Text"; set.RenameEntry(entry.Id, "menu/start");
            Assert.That(config.CreateEntryIndex().Resolve(reference, out var renamed), Is.EqualTo(LocalizationReferenceStatus.Found));
            Assert.That(renamed.Key, Is.EqualTo("menu/start"));
            Assert.That(original.Resolve(reference, out var old), Is.EqualTo(LocalizationReferenceStatus.Found));
            Assert.That(old.Key, Is.EqualTo("menu/play"), "Existing runtime metadata must not change under a caller.");
            var target = config.AddSet("Main menu");
            config.MoveEntry(entry.Id, target.Id);
            Assert.That(config.CreateEntryIndex().Resolve(reference, out _), Is.EqualTo(LocalizationReferenceStatus.SetNotAvailable));
            config.MainChannel.IncludeSet(target.Id);
            Assert.That(config.CreateEntryIndex().Resolve(reference, out var moved), Is.EqualTo(LocalizationReferenceStatus.Found));
            Assert.That(moved.SetId, Is.EqualTo(target.Id));
            target.RemoveEntry(entry.Id);
            var replacement = target.AddEntry("menu/start");
            Assert.That(replacement.Id, Is.Not.EqualTo(reference.EntryId));
            Assert.That(config.CreateEntryIndex().Resolve(reference, out _), Is.EqualTo(LocalizationReferenceStatus.MissingEntry));
        }

        [Test] public void ConflictingMoveAndRenameLeaveOriginalEntriesUntouched()
        {
            var source = config.Sets[0]; var entry = source.AddEntry("play"); source.AddEntry("quit");
            var target = config.AddSet("Other"); target.AddEntry("play");
            Assert.Throws<ArgumentException>(() => config.MoveEntry(entry.Id, target.Id));
            Assert.That(source.Entries.Any(e => e.Id == entry.Id), Is.True);
            Assert.That(target.Entries.Count, Is.EqualTo(1));
            Assert.Throws<ArgumentException>(() => source.RenameEntry(entry.Id, "quit"));
            Assert.That(entry.Key, Is.EqualTo("play"));
        }

        [Test] public void DefaultSetSurvivesRenameAndRemovingItDoesNotSilentlyChooseAnotherSet()
        {
            var common = config.Sets[0]; string originalId = common.Id;
            var other = config.AddSet("Other"); config.MainChannel.IncludeSet(other.Id);
            common.Name = "Renamed common";
            Assert.That(config.MainChannel.DefaultSetId, Is.EqualTo(originalId));
            config.MainChannel.RemoveSet(originalId);
            Assert.That(config.MainChannel.DefaultSetId, Is.Empty);
            Assert.That(Errors(), Does.Contain("invalid-default-set"));
            config.MainChannel.SetDefaultSet(other.Id);
            Assert.That(Errors(), Is.Empty);
            Assert.Throws<ArgumentException>(() => config.MainChannel.SetDefaultSet(originalId));
        }

        [Test] public void ImportedDuplicateIdentityAcrossSetsIsRejectedEvenWithDifferentKeys()
        {
            var entry = config.Sets[0].AddEntry("play");
            config.AddSet("Other").AddEntry("different", id: entry.Id);
            Assert.That(Errors(), Does.Contain("duplicate-id"));
            Assert.Throws<ArgumentException>(() => config.CreateEntryIndex());
        }

        [Test] public void ChannelsAllowDifferentLanguagesAndReportInvalidDefaultsAndFallbackCycles()
        {
            var voice = config.AddChannel("Voice", new[] { "en" });
            voice.Mode = LocalizationChannelMode.FollowMain;
            Assert.That(Errors(), Is.Empty, "Following Main with a smaller language catalog is supported.");
            voice.DefaultLanguage = "ru";
            Assert.That(Errors(), Does.Contain("invalid-default"));
            voice.DefaultLanguage = "en"; voice.FallbackLanguage = "ru";
            Assert.That(Errors(), Does.Contain("invalid-fallback"));
            voice.FallbackLanguage = "en";
            config.MainChannel.SetFallback("en", "ru"); // ru falls back to global en.
            Assert.That(Errors(), Does.Contain("fallback-cycle"));
            config.MainChannel.SetFallback("en", null);
            config.MainChannel.SetFallback("ru", "ru");
            Assert.That(Errors(), Does.Contain("fallback-cycle"));
            config.MainChannel.SetFallback("ru", null);
            Assert.That(Errors(), Is.Empty);
            config.MainChannel.Mode = LocalizationChannelMode.FollowMain;
            Assert.That(Errors(), Does.Contain("main-follows-itself"));
        }

        [Test] public void UnknownBindingsAreReportedAndReferencedDefinitionsCannotBeSilentlyRemoved()
        {
            Assert.Throws<InvalidOperationException>(() => config.RemoveLanguage("en"));
            Assert.Throws<InvalidOperationException>(() => config.RemoveSet(config.Sets[0].Id));
            Assert.Throws<InvalidOperationException>(() => config.RemoveChannel(config.MainChannelId));
            config.MainChannel.IncludeSet(Guid.NewGuid().ToString("N"));
            Assert.That(Errors(), Does.Contain("unknown-set"));
        }

        [TestCase("../escape")]
        [TestCase("/root")]
        [TestCase("a\\b")]
        [TestCase("a/")]
        [TestCase("")]
        [TestCase("a//b")]
        public void ResourcesRequiresExactSafeAddress(string address)
        {
            config.Sets[0].SetSources("en", new LocalizationSourceReference("resources", address, LocalizationSourceKind.TextTable));
            Assert.That(Errors(), Does.Contain("invalid-address"));
        }

        [Test] public void CustomProviderCanUseItsOwnAddressSyntaxWithoutLoadingIt()
        {
            config.Sets[0].SetSources("en", new LocalizationSourceReference("remote", "https://example.invalid/translations/en", LocalizationSourceKind.TextTable));
            config.Languages[0].Appearance = new("remote", "fonts/en", LocalizationSourceKind.Appearance);
            Assert.That(Errors(), Is.Empty);
            config.Sets[0].SetSources("en", new LocalizationSourceReference("remote", "fonts/en", LocalizationSourceKind.Appearance));
            Assert.That(Errors(), Does.Contain("invalid-source-kind"));
        }

        [Test] public void EmptyAndDamagedSerializedConfigReportsIssuesWithoutRepairingIt()
        {
            using (var serialized = new SerializedObject(config))
            {
                serialized.FindProperty("_languages").ClearArray();
                serialized.FindProperty("_channels").ClearArray();
                serialized.FindProperty("_sets").ClearArray();
                serialized.FindProperty("_mainChannelId").stringValue = "";
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            string before = EditorJsonUtility.ToJson(config);
            Assert.That(Errors(), Does.Contain("no-languages"));
            Assert.That(Errors(), Does.Contain("no-channels"));
            Assert.That(Errors(), Does.Contain("missing-main"));
            Assert.That(EditorJsonUtility.ToJson(config), Is.EqualTo(before));
        }

        [Test] public void UnknownPluralIsAWarningAndCustomRuleChangesTheReportedSupport()
        {
            string code = "x-c1-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            config.AddLanguage(code);
            Assert.That(Errors(), Is.Empty);
            Assert.That(config.Validate().Any(i => i.Code == "default-plural-rule"), Is.True);
            Assert.That(PluralRuleProvider.GetRuleInfo(code).Support, Is.EqualTo(PluralRuleSupport.Default));
            var custom = new FixedRule(); PluralRuleProvider.RegisterRule(code, custom);
            Assert.That(PluralRuleProvider.GetRuleInfo(code).Support, Is.EqualTo(PluralRuleSupport.Custom));
            Assert.That(PluralRuleProvider.GetRule(code), Is.SameAs(custom));
            Assert.That(config.Validate().Any(i => i.Code == "default-plural-rule"), Is.False);
        }

        [TestCase("en-US", PluralRuleSupport.BuiltIn)]
        [TestCase("pt-BR", PluralRuleSupport.BuiltIn)]
        [TestCase("pt-PT", PluralRuleSupport.BuiltIn)]
        [TestCase("he", PluralRuleSupport.Default)]
        public void RuleSupportDescribesTheActualFormattingRule(string code, PluralRuleSupport support)
        {
            var info = PluralRuleProvider.GetRuleInfo(code);
            Assert.That(info.Support, Is.EqualTo(support));
            Assert.That(info.Rule, Is.SameAs(PluralRuleProvider.GetRule(code)));
        }

        [Test] public void ParentCustomRuleAndExactOverrideInvalidateSupportCache()
        {
            string code = "x-c1-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            Assert.That(PluralRuleProvider.GetRuleInfo(code + "-extra").Support, Is.EqualTo(PluralRuleSupport.Default));
            var parent = new FixedRule(); PluralRuleProvider.RegisterRule(code, parent);
            Assert.That(PluralRuleProvider.GetRuleInfo(code + "-extra").Rule, Is.SameAs(parent));
            var exact = new FixedRule(); PluralRuleProvider.RegisterRule(code + "-extra", exact);
            Assert.That(PluralRuleProvider.GetRuleInfo(code + "-extra").Rule, Is.SameAs(exact));
            Assert.That(PluralRuleProvider.GetRuleInfo(code).Rule, Is.SameAs(parent));
        }

        [TestCase(" RU ", "ru")]
        [TestCase("eng", "en")]
        [TestCase("pt_br", "pt-BR")]
        [TestCase("zh_hant_tw", "zh-Hant-TW")]
        [TestCase("iw", "he")]
        [TestCase("x-My-Game", "x-my-game")]
        public void CanonicalLanguageCodesAreSharedWithRuntime(string input, string expected) =>
            Assert.That(LocalizationLanguageCode.Normalize(input), Is.EqualTo(expected));

        [TestCase("Russian")]
        [TestCase("ru/../../en")]
        [TestCase("xx-YY")]
        [TestCase("en-Abcd")]
        [TestCase("en-US-US")]
        public void InvalidLanguageCodesAreRejected(string input) => Assert.Throws<ArgumentException>(() => LocalizationLanguageCode.Normalize(input));

        [Test] public void SavingAndReloadingConfigPreservesIdsAndDoesNotReferenceAppearanceOrFontAssets()
        {
            root = "Assets/ChannelConfigTest_" + Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(root); AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var font = ScriptableObject.CreateInstance<TMP_FontAsset>();
            AssetDatabase.CreateAsset(font, root + "/Font.asset");
            var appearance = ScriptableObject.CreateInstance<LocalizationAppearance>(); appearance.overrideFont = true; appearance.primaryFont = font;
            AssetDatabase.CreateAsset(appearance, root + "/Appearance.asset");
            config.Languages[0].Appearance = new("resources", "Appearance", LocalizationSourceKind.Appearance);
            var entry = config.Sets[0].AddEntry("start"); string entryId = entry.Id; string channelId = config.MainChannelId;
            config.AutoInitialize = true;
            AssetDatabase.CreateAsset(config, root + "/Config.asset"); AssetDatabase.SaveAssetIfDirty(config);
            var dependencies = AssetDatabase.GetDependencies(root + "/Config.asset", true);
            Assert.That(dependencies, Does.Not.Contain(root + "/Appearance.asset"));
            Assert.That(dependencies, Does.Not.Contain(root + "/Font.asset"));
            string saved = File.ReadAllText(root + "/Config.asset");
            Assert.That(saved, Does.Contain(entryId)); Assert.That(saved, Does.Contain(channelId));
            Resources.UnloadAsset(config); config = AssetDatabase.LoadAssetAtPath<LocalizationProjectConfig>(root + "/Config.asset");
            Assert.That(Errors(), Is.Empty);
            Assert.That(config.AutoInitialize, Is.True);
            Assert.That(config.CreateEntryIndex().Resolve(new(channelId, entryId), out var location), Is.EqualTo(LocalizationReferenceStatus.Found));
            Assert.That(location.Key, Is.EqualTo("start"));
            Assert.That(config.Languages[0].Appearance.Address, Is.EqualTo("Appearance"));
            Assert.That(config.Languages[1].Appearance, Is.Null, "Unity must preserve an absent optional appearance across serialization.");
        }

        [Test] public void DuplicateSerializedIdsCannotPassValidation()
        {
            var other = config.AddChannel("Voice", new[] { "en" });
            string json = EditorJsonUtility.ToJson(config).Replace(other.Id, config.MainChannelId);
            EditorJsonUtility.FromJsonOverwrite(json, config);
            Assert.That(Errors(), Does.Contain("duplicate-id"));
        }

        private sealed class FixedRule : IPluralRule
        { public int FormCount => 1; public int Evaluate(int count) => 0; }
    }
}
