using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using NUnit.Framework;
using SimplyLocalize.Data;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace SimplyLocalize.Tests
{
    public class PreparedResourcesTests
    {
        [Serializable] private class Measurement
        {
            public string unityVersion, scope, requestedKey, resolvedLanguage, audioLoadState;
            public int legacyFirstLookupReads, preparationReads, readyLookupReads, preparedTableEntries;
            public double preparationMilliseconds, readyLookupMilliseconds;
        }

        [UnityTest] public IEnumerator RealResources_PreparedReadsDoNotLoadAndReturnTheResolvedLanguage()
        {
            var config = ScriptableObject.CreateInstance<LocalizationConfig>();
            var en = ScriptableObject.CreateInstance<LanguageProfile>(); en.languageCode = "en";
            var ru = ScriptableObject.CreateInstance<LanguageProfile>(); ru.languageCode = "ru";
            config.languages.Add(en); config.languages.Add(ru); config.defaultLanguage = config.fallbackLanguage = en;
            config.resourcesBasePath = LocalizationResourceFixtures.VoiceRoot; config.loadTextData = false;
            var provider = new ObservedProvider(config.resourcesBasePath);
            var legacy = new LocalizationManager(config); legacy.SetDataProvider(provider); legacy.SetLanguage("ru");
            var preparedManager = new LocalizationManager(config); preparedManager.SetDataProvider(provider);
            try
            {
                // Use a known independent fixture; no lookup that preloads tables before the probe.
                string key = LocalizationResourceFixtures.PrimaryKey;
                var measurement = new Measurement { unityVersion = Application.unityVersion, scope = config.resourcesBasePath, requestedKey = key };
                int before = provider.Reads;
                Assert.That(legacy.GetAsset<AudioClip>(key), Is.Not.Null);
                measurement.legacyFirstLookupReads = provider.Reads - before;
                Assert.That(measurement.legacyFirstLookupReads, Is.GreaterThan(0));
                before = provider.Reads; provider.LastEntryCount = 0; var timer = Stopwatch.StartNew();
                using (var snapshot = preparedManager.PrepareLanguage("ru"))
                {
                    preparedManager.ApplyPreparedLanguage(snapshot);
                    measurement.preparationReads = provider.Reads - before;
                    measurement.preparedTableEntries = provider.LastEntryCount;
                }
                measurement.preparationMilliseconds = timer.Elapsed.TotalMilliseconds;
                before = provider.Reads; timer.Restart();
                for (int i = 0; i < 1000; i++)
                {
                    var resolved = preparedManager.ResolvePreparedAsset<AudioClip>(key);
                    Assert.That(resolved.Status, Is.EqualTo(LocalizationResolveStatus.Found));
                    Assert.That(preparedManager.ResolvePreparedAsset<AudioClip>("__missing__").Status, Is.EqualTo(LocalizationResolveStatus.Missing));
                }
                measurement.readyLookupMilliseconds = timer.Elapsed.TotalMilliseconds;
                measurement.readyLookupReads = provider.Reads - before;
                var result = preparedManager.ResolvePreparedAsset<AudioClip>(key);
                measurement.resolvedLanguage = result.ResolvedLanguage; measurement.audioLoadState = result.Value.loadState.ToString();
                Assert.That(measurement.readyLookupReads, Is.Zero); Assert.That(result.ResolvedLanguage, Is.EqualTo("ru"));
                preparedManager.ClearCaches();
                Assert.That(preparedManager.ResolvePreparedAsset<AudioClip>(key).Status, Is.EqualTo(LocalizationResolveStatus.NotPrepared));
                Assert.That(result.Value != null, Is.True, "Clearing a manager must not destroy borrowed audio.");
                string path = Path.GetFullPath(Path.Combine(Application.dataPath, "../TestResults/Stage9Preparation/playmode-probe.json"));
                Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, JsonUtility.ToJson(measurement, true));
                yield return null;
            }
            finally
            {
                legacy.ClearCaches(); preparedManager.ClearCaches();
                Object.DestroyImmediate(config); Object.DestroyImmediate(en); Object.DestroyImmediate(ru);
            }
        }

        private sealed class ObservedProvider : ILocalizationDataProvider
        {
            private readonly ResourcesDataProvider _inner;
            internal int Reads, LastEntryCount;
            internal ObservedProvider(string path) { _inner = new ResourcesDataProvider(path); }
            public Dictionary<string, string> LoadTextData(string code) { Reads++; return _inner.LoadTextData(code); }
            public bool HasTextData(string code) => _inner.HasTextData(code);
            public List<LocalizationAssetTable> LoadAssetTables(string code)
            { Reads++; var tables = _inner.LoadAssetTables(code); LastEntryCount += tables.Sum(t => t.Count); return tables; }
        }
    }
}
