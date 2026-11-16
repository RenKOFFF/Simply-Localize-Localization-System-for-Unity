using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using SimplyLocalize.Editor.Data;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace SimplyLocalize.Tests
{
    public class ConfigDiscoveryTests
    {
        private string _root, _id;
        [SetUp] public void Setup()
        {
            _id = "Discovery_" + Guid.NewGuid().ToString("N");
            _root = "Assets/__" + _id;
            Directory.CreateDirectory(_root + "/Resources/" + _id);
            AssetDatabase.Refresh();
        }
        [TearDown] public void Cleanup() => AssetDatabase.DeleteAsset(_root);

        private LocalizationConfig Config(string relative, bool automatic)
        {
            string path = _root + "/" + relative;
            Directory.CreateDirectory(Path.GetDirectoryName(path)); AssetDatabase.Refresh();
            var config = ScriptableObject.CreateInstance<LocalizationConfig>(); config.autoInitialize = automatic;
            AssetDatabase.CreateAsset(config, path); return config;
        }
        private static LocalizationConfig Find(bool automatic) => (LocalizationConfig)typeof(Localization)
            .GetMethod("FindResourceConfig", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { automatic });

        [Test] public void Discovery_KeepsLegacyNamesAndNestedPaths_WithoutRequiringCatalogInEditor()
        {
            var config = Config("Resources/" + _id + "/Nested/MyUnusualName.asset", true);
            Assert.That(Find(true), Is.SameAs(config));
            Assert.That(Find(false), Is.SameAs(config));
        }
        [Test] public void AutomaticDiscovery_IgnoresManualConfigsAndConfigsOutsideResources()
        {
            var automatic = Config("Resources/" + _id + "/Auto.asset", true);
            Config("Resources/" + _id + "/Manual.asset", false);
            Config("Outside.asset", true);
            Assert.That(Find(true), Is.SameAs(automatic));
            LogAssert.Expect(LogType.Error, new Regex("Conflicting LocalizationConfigs"));
            Assert.That(Find(false), Is.Null);
        }
        [Test] public void ConflictingAutomaticConfigs_AreReportedInsteadOfSelectingAnArbitraryOne()
        {
            Config("Resources/" + _id + "/A.asset", true); Config("Resources/" + _id + "/B.asset", true);
            LogAssert.Expect(LogType.Error, new Regex("Conflicting auto-initialize LocalizationConfigs"));
            Assert.That(Find(true), Is.Null);
        }
        [Test] public void NoAutomaticConfig_DoesNotInitializeManualConfig()
        {
            Config("Resources/" + _id + "/Manual.asset", false);
            Assert.That(Find(true), Is.Null);
        }
        [Test] public void DuplicateResourcesPaths_AreRejectedBeforeBuild()
        {
            Config("A/Resources/" + _id + "/Config.asset", true);
            Config("B/Resources/" + _id + "/Config.asset", false);
            Assert.Throws<InvalidOperationException>(() => LocalizationConfigCatalog.CollectEditorEntries());
        }
        [Test] public void Catalog_StoresOnlyMetadata_AndRebuildTracksMovedConfigsAndFlags()
        {
            var config = Config("Resources/" + _id + "/Old.asset", true);
            string catalogPath = _root + "/Catalog.asset";
            void Generate() => typeof(LocalizationConfigCatalogBuilder).GetMethod("Generate", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { catalogPath });
            Generate();
            var catalog = AssetDatabase.LoadAssetAtPath<LocalizationConfigCatalog>(catalogPath);
            var entry = catalog.entries.Single(e => e.resourcePath.StartsWith(_id + "/", StringComparison.Ordinal));
            Assert.That(entry.resourcePath, Is.EqualTo(_id + "/Old")); Assert.That(entry.autoInitialize, Is.True);
            var serialized = new SerializedObject(catalog); var property = serialized.GetIterator();
            while (property.Next(true))
                if (property.propertyType == SerializedPropertyType.ObjectReference && property.name != "m_Script")
                    Assert.That(property.objectReferenceValue, Is.Null, property.propertyPath);
            string guid = AssetDatabase.AssetPathToGUID(catalogPath);
            Assert.That(AssetDatabase.MoveAsset(AssetDatabase.GetAssetPath(config), _root + "/Resources/" + _id + "/New.asset"), Is.Empty);
            config.autoInitialize = false; EditorUtility.SetDirty(config); AssetDatabase.SaveAssetIfDirty(config);
            Generate();
            entry = catalog.entries.Single(e => e.resourcePath.StartsWith(_id + "/", StringComparison.Ordinal));
            Assert.That(entry.resourcePath, Is.EqualTo(_id + "/New")); Assert.That(entry.autoInitialize, Is.False);
            Assert.That(AssetDatabase.AssetPathToGUID(catalogPath), Is.EqualTo(guid));
        }
        [TestCase("Assets/X/Resources/Nested/Config.asset", "Nested/Config")]
        [TestCase("Packages/example/Resources/Config.asset", "Config")]
        [TestCase("Assets/Editor/Resources/Config.asset", null)]
        [TestCase("Assets/Data/Config.asset", null)]
        public void ResourcePaths_MatchBuildVisibility(string path, string expected) =>
            Assert.That(LocalizationConfigCatalog.GetResourcePath(path), Is.EqualTo(expected));
    }
}
