using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace SimplyLocalize.Tests
{
    public class FixtureIsolationTests
    {
        [Test]
        public void LifecycleResourceDependenciesRemainInsideTestFixtures()
        {
            const string root = "Assets/SimplyLocalizeTests/Fixtures/Lifecycle/";
            var tables = AssetDatabase.FindAssets("t:LocalizationAssetTable", new[] { root.TrimEnd('/') });
            Assert.That(tables, Is.Not.Empty, "Lifecycle tests require imported standalone Resources tables.");

            foreach (string guid in tables)
            {
                string table = AssetDatabase.GUIDToAssetPath(guid);
                foreach (string dependency in AssetDatabase.GetDependencies(table, true))
                {
                    if (!dependency.StartsWith("Assets/", StringComparison.Ordinal)) continue; // Package scripts/built-ins.
                    Assert.That(dependency.StartsWith(root, StringComparison.Ordinal), Is.True,
                        $"{table} must survive demo removal; unexpected project dependency: {dependency}");
                    if (AssetDatabase.GetMainAssetTypeAtPath(dependency) == typeof(AudioClip))
                    {
                        var importer = (AudioImporter)AssetImporter.GetAtPath(dependency);
                        Assert.That(importer.defaultSampleSettings.preloadAudioData, Is.False,
                            "Audio readiness must be exercised by LocalizationRuntime, not hidden by importer preloading.");
                    }
                }
            }
        }
    }
}
