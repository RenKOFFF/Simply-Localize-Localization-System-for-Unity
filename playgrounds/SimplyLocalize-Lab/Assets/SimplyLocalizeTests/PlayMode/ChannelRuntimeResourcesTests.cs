using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace SimplyLocalize.Tests
{
    public class ChannelRuntimeResourcesTests
    {
        private LocalizationProjectConfig config;
        private readonly List<LocalizationRuntime> runtimes = new();
        private readonly List<LocalizationChannelLease> leases = new();
        private readonly List<LocalizationSourceLease> sourceLeases = new();
        private static IEnumerator Done(Task task)
        {
            float deadline = Time.realtimeSinceStartup + 15;
            while (!task.IsCompleted && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(task.IsCompletedSuccessfully, Is.True, task.Exception?.ToString() ?? "Runtime operation timed out.");
        }
        private LocalizationRuntime Create()
        {
            if (config == null)
            {
                config = LocalizationProjectConfig.Create(new[] { "en", "ru" });
                config.MainChannel.FallbackLanguage = "";
                var set = config.Sets[0]; set.AddEntry(LocalizationResourceFixtures.PrimaryKey, LocalizationEntryKind.Asset);
                foreach (string language in new[] { "en", "ru" })
                    set.SetSources(language, new LocalizationSourceReference("resources", LocalizationResourceFixtures.VoiceTable(language), LocalizationSourceKind.AssetTable));
            }
            var runtime = new LocalizationRuntime(config, options: new LocalizationContextOptions { Ownership = LocalizationResourceOwnership.Owned });
            runtimes.Add(runtime); return runtime;
        }
        private static AudioClip Clip(LocalizationRuntime runtime)
        {
            Assert.That(runtime.GetChannel().TryGetAsset(LocalizationResourceFixtures.PrimaryKey, out var value), Is.True);
            return (AudioClip)value.Asset;
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            foreach (var runtime in runtimes) yield return Done(runtime.ReleaseAsync());
            foreach (var lease in leases) yield return Done(lease.ReleaseAsync());
            foreach (var lease in sourceLeases) yield return Done(lease.ReleaseAsync());
            runtimes.Clear(); leases.Clear();
            sourceLeases.Clear();
            if (config != null) UnityEngine.Object.DestroyImmediate(config);
        }
        [UnityTest] public IEnumerator TwoPlayersShareSourcesButKeepTheirLanguagesAndReleaseIndependently()
        {
            var first = Create(); var second = Create(); yield return Done(first.InitializeAsync()); yield return Done(second.InitializeAsync());
            var english = Clip(second); Assert.That(Clip(first), Is.SameAs(english));
            yield return Done(first.SwitchLanguageAsync("ru")); Assert.That(Clip(first), Is.Not.SameAs(english));
            Assert.That(english.loadState, Is.EqualTo(AudioDataLoadState.Loaded)); Assert.That(second.GetChannel().RequestedLanguage, Is.EqualTo("en"));
            yield return Done(first.ReleaseAsync()); Assert.That(english.loadState, Is.EqualTo(AudioDataLoadState.Loaded));
            yield return Done(second.ReleaseAsync());
            Assert.That(Resources.FindObjectsOfTypeAll<AudioClip>().Any(c => c.name.StartsWith("SLTest_")), Is.False);
        }
        [UnityTest] public IEnumerator RetainedLineKeepsOldClipUntilTheConsumerFinishes()
        {
            var runtime = Create(); yield return Done(runtime.InitializeAsync()); var english = Clip(runtime); int oldId = english.GetInstanceID();
            var line = runtime.GetChannel().Retain(); leases.Add(line);
            yield return Done(runtime.SwitchLanguageAsync("ru")); var russian = Clip(runtime); int newId = russian.GetInstanceID();
            Assert.That(english.loadState, Is.EqualTo(AudioDataLoadState.Loaded));
            yield return Done(line.ReleaseAsync()); Assert.That(Resources.FindObjectsOfTypeAll<AudioClip>().Any(c => c.GetInstanceID() == oldId), Is.False);
            Assert.That(russian.loadState, Is.EqualTo(AudioDataLoadState.Loaded)); yield return Done(runtime.ReleaseAsync());
            Assert.That(Resources.FindObjectsOfTypeAll<AudioClip>().Any(c => c.GetInstanceID() == newId), Is.False);
        }
        [UnityTest] public IEnumerator PreviousSessionRuntimeCannotReadOrUnloadTheNewSession()
        {
            var old = Create(); yield return Done(old.InitializeAsync()); var snapshot = old.GetChannel();
            typeof(LocalizationContext).Assembly.GetType("SimplyLocalize.LocalizationResourceLease")
                .GetMethod("ResetSession", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
            var current = Create(); yield return Done(current.InitializeAsync()); var clip = Clip(current); int clipId = clip.GetInstanceID();
            Assert.Throws<OperationCanceledException>(() => old.GetChannel());
            Assert.Throws<ObjectDisposedException>(() => snapshot.TryGetAsset(LocalizationResourceFixtures.PrimaryKey, out _));
            yield return Done(old.ReleaseAsync()); Assert.That(clip.loadState, Is.EqualTo(AudioDataLoadState.Loaded));
            yield return Done(current.ReleaseAsync());
            Assert.That(Resources.FindObjectsOfTypeAll<AudioClip>().Any(c => c.GetInstanceID() == clipId), Is.False);
        }
        [UnityTest] public IEnumerator RetainingOneAssetSourceDoesNotPinUnrelatedTablesInTheSameChannel()
        {
            Create(); var set = config.Sets[0]; set.AddEntry("picture", LocalizationEntryKind.Asset);
            set.SetSources("en",
                new LocalizationSourceReference("resources", LocalizationResourceFixtures.VoiceTable("en"), LocalizationSourceKind.AssetTable),
                new LocalizationSourceReference("resources", LocalizationResourceFixtures.ImageTable, LocalizationSourceKind.AssetTable));
            var optional = config.AddSet("Optional"); config.MainChannel.IncludeSet(optional.Id);
            var runtime = Create(); yield return Done(runtime.InitializeAsync());
            // Retained source handles in unchanged sets must be rebound to the replacement snapshot.
            var acquired = runtime.AcquireSetAsync(null, optional.Id); yield return Done(acquired);
            Assert.That(runtime.GetChannel().TryGetAsset(LocalizationResourceFixtures.PrimaryKey, out var afterAcquire), Is.True);
            var brieflyRetained = afterAcquire.RetainSource(); sourceLeases.Add(brieflyRetained); yield return Done(brieflyRetained.ReleaseAsync());
            yield return Done(acquired.Result.ReleaseAsync());
            Assert.That(runtime.GetChannel().TryGetAsset(LocalizationResourceFixtures.PrimaryKey, out var line), Is.True);
            int lineId = line.Asset.GetInstanceID(); var owner = line.RetainSource(); sourceLeases.Add(owner);
            Assert.That(runtime.GetChannel().TryGetAsset("picture", out var picture), Is.True); int pictureId = picture.Asset.GetInstanceID();
            yield return Done(runtime.SwitchLanguageAsync("ru"));
            Assert.That(((AudioClip)line.Asset).loadState, Is.EqualTo(AudioDataLoadState.Loaded));
            Assert.That(Resources.FindObjectsOfTypeAll<UnityEngine.Object>().Any(o => o.GetInstanceID() == pictureId), Is.False);
            yield return Done(owner.ReleaseAsync());
            Assert.That(Resources.FindObjectsOfTypeAll<AudioClip>().Any(c => c.GetInstanceID() == lineId), Is.False);
        }
        [UnityTest] public IEnumerator AppearanceCanBeRetainedWithoutKeepingTheOldVoiceTable()
        {
            Create(); config.Languages.First(l => l.Code == "en").Appearance = new LocalizationSourceReference(
                "resources", "SimplyLocalizeTests/Lifecycle/Appearance", LocalizationSourceKind.Appearance);
            var runtime = Create(); yield return Done(runtime.InitializeAsync());
            var appearance = runtime.GetChannel().GetAppearance(); Assert.That(appearance, Is.Not.Null);
            int fontId = appearance.primaryFont.GetInstanceID(), voiceId = Clip(runtime).GetInstanceID();
            var owner = runtime.GetChannel().RetainAppearance(); sourceLeases.Add(owner);
            yield return Done(runtime.SwitchLanguageAsync("ru")); Assert.That(runtime.GetChannel().GetAppearance(), Is.Null);
            Assert.That(owner.Data.Appearance.primaryFont != null, Is.True);
            Assert.That(Resources.FindObjectsOfTypeAll<AudioClip>().Any(c => c.GetInstanceID() == voiceId), Is.False);
            yield return Done(owner.ReleaseAsync());
            Assert.That(Resources.FindObjectsOfTypeAll<TMPro.TMP_FontAsset>().Any(f => f.GetInstanceID() == fontId), Is.False);
        }
    }
}
