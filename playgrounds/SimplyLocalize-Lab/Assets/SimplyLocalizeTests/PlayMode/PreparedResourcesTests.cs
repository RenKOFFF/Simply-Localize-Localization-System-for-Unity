using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
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
            public int beforeInitializationReads, preparationReads, readyLookupReads, preparedTableEntries;
            public double preparationMilliseconds, readyLookupMilliseconds;
        }
        [UnityTest] public IEnumerator RealResources_ReadyReadsDoNotLoadAndReturnTheResolvedLanguage()
        {
            var config = LocalizationProjectConfig.Create(new[] { "en", "ru" });
            config.MainChannel.FallbackLanguage = "en";
            config.Sets[0].AddEntry(LocalizationResourceFixtures.PrimaryKey, LocalizationEntryKind.Asset);
            foreach (string code in new[] { "en", "ru" })
                config.Sets[0].SetSources(code, new LocalizationSourceReference("observed", LocalizationResourceFixtures.VoiceTable(code), LocalizationSourceKind.AssetTable));
            var provider = new ObservedProvider(); var registry = new LocalizationSourceRegistry(false); registry.Register("observed", provider);
            var runtime = new LocalizationRuntime(config, registry);
            try
            {
                string key = LocalizationResourceFixtures.PrimaryKey;
                var measurement = new Measurement { unityVersion = Application.unityVersion, scope = LocalizationResourceFixtures.VoiceRoot, requestedKey = key };
                Assert.Throws<InvalidOperationException>(() => runtime.GetAsset<AudioClip>(key));
                measurement.beforeInitializationReads = provider.Reads; Assert.That(provider.Reads, Is.Zero);
                var timer = Stopwatch.StartNew(); var preparation = runtime.InitializeAsync("ru");
                while (!preparation.IsCompleted) yield return null;
                preparation.GetAwaiter().GetResult();
                measurement.preparationMilliseconds = timer.Elapsed.TotalMilliseconds;
                measurement.preparationReads = provider.Reads; measurement.preparedTableEntries = provider.LastEntryCount;
                Assert.That(measurement.preparationReads, Is.EqualTo(2), "Only the requested and fallback table are loaded.");
                int before = provider.Reads; timer.Restart();
                for (int i = 0; i < 1000; i++)
                {
                    Assert.That(runtime.GetChannel().TryGetAsset<AudioClip>(key, out _), Is.True);
                    Assert.That(runtime.GetChannel().TryGetAsset<AudioClip>("__missing__", out _), Is.False);
                }
                measurement.readyLookupMilliseconds = timer.Elapsed.TotalMilliseconds;
                measurement.readyLookupReads = provider.Reads - before;
                Assert.That(runtime.GetChannel().TryGetAsset<AudioClip>(key, out var result), Is.True);
                var clip = (AudioClip)result.Asset;
                measurement.resolvedLanguage = result.Language; measurement.audioLoadState = clip.loadState.ToString();
                Assert.That(measurement.readyLookupReads, Is.Zero); Assert.That(result.Language, Is.EqualTo("ru"));
                Assert.That(clip.loadState, Is.EqualTo(AudioDataLoadState.Loaded));
                var release = runtime.ReleaseAsync(); while (!release.IsCompleted) yield return null; release.GetAwaiter().GetResult();
                Assert.Throws<ObjectDisposedException>(() => runtime.GetAsset<AudioClip>(key));
                Assert.That(clip != null, Is.True, "Releasing a Borrowed source does not destroy external audio.");
                string path = Path.GetFullPath(Path.Combine(Application.dataPath, "../TestResults/Channels-C4/prepared-resources.json"));
                Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, JsonUtility.ToJson(measurement, true));
            }
            finally { runtime.Dispose(); Object.DestroyImmediate(config); }
        }
        private sealed class ObservedProvider : ILocalizationSourceProvider
        {
            private readonly ResourcesLocalizationSourceProvider inner = new();
            internal int Reads, LastEntryCount;
            public LocalizationProviderCapabilities GetCapabilities(LocalizationSourceKind kind) => inner.GetCapabilities(kind);
            public async Task<LocalizationSourceLease> LoadAsync(LocalizationSourceRequest request, CancellationToken token)
            {
                Reads++; var source = await inner.LoadAsync(request, token);
                LastEntryCount += source.Data.SourceEntryCount; return source;
            }
        }
    }
}
