using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using SimplyLocalize.Data;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace SimplyLocalize.Tests
{
    public class ChannelOwnershipTests
    {
        private LocalizationProjectConfig config;

        private readonly List<LocalizationRuntime> contexts = new();
        [SetUp] public void Setup()
        {
            config = LocalizationProjectConfig.Create(new[] { "en", "ru" });
            config.MainChannel.FallbackLanguage = "en";
            var set = config.Sets[0];
            set.AddEntry(LocalizationResourceFixtures.PrimaryKey, LocalizationEntryKind.Asset);
            set.AddEntry(LocalizationResourceFixtures.FallbackKey, LocalizationEntryKind.Asset);
            set.AddEntry("picture", LocalizationEntryKind.Asset);
            foreach (string code in new[] { "en", "ru" })
                set.SetSources(code, new LocalizationSourceReference("resources", LocalizationResourceFixtures.VoiceTable(code), LocalizationSourceKind.AssetTable));
        }
        private LocalizationRuntime Voice(LocalizationResourceOwnership ownership = LocalizationResourceOwnership.Owned)
        {
            var context = new LocalizationRuntime(config, options: new LocalizationRuntimeOptions { Ownership = ownership });
            contexts.Add(context); return context;
        }
        private static Task Change(LocalizationRuntime runtime, string code) =>
            runtime.IsInitialized ? runtime.SwitchLanguageAsync(code) : runtime.InitializeAsync(code);
        private static IEnumerator Done(Task task)
        {
            float deadline = Time.realtimeSinceStartup + 15;
            while (!task.IsCompleted && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(task.IsCompleted, Is.True, "Context operation timed out.");
        }
        private static IEnumerator Success(Task task) { yield return Done(task); Assert.That(task.IsCompletedSuccessfully, Is.True, task.Exception?.ToString()); }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            foreach (var context in contexts) yield return Success(context.ReleaseAsync());
            contexts.Clear(); Object.Destroy(config); yield return null;
        }
        [UnityTest] public IEnumerator AudioIsReady_AndLastOwnerReleasesSharedNativeAssets()
        {
            var first = Voice(); var second = Voice();
            yield return Success(Change(first, "ru"));
            var clip = first.GetAsset<AudioClip>(LocalizationResourceFixtures.PrimaryKey);
            int clipId = clip.GetInstanceID();
            Assert.That(clip != null, Is.True); Assert.That(clip.loadState, Is.EqualTo(AudioDataLoadState.Loaded));
            yield return Success(Change(second, "ru"));
            Assert.That(second.GetAsset<AudioClip>(LocalizationResourceFixtures.PrimaryKey), Is.SameAs(clip));
            yield return Success(first.ReleaseAsync());
            Assert.That(clip != null, Is.True); Assert.That(second.GetAsset<AudioClip>(LocalizationResourceFixtures.PrimaryKey).loadState, Is.EqualTo(AudioDataLoadState.Loaded));
            yield return Success(second.ReleaseAsync());
            Assert.That(Resources.FindObjectsOfTypeAll<AudioClip>().Any(c => c.GetInstanceID() == clipId), Is.False, "Last owner must unload the native asset; Editor wrapper equality is not a residency check.");
        }
        [UnityTest] public IEnumerator BorrowedContextPinsAnOwnedResourceUntilBothClose()
        {
            var owner = Voice(); var borrower = Voice(LocalizationResourceOwnership.Borrowed);
            yield return Success(Change(owner, "en")); yield return Success(Change(borrower, "en"));
            var clip = borrower.GetAsset<AudioClip>(LocalizationResourceFixtures.PrimaryKey);
            int clipId = clip.GetInstanceID();
            yield return Success(owner.ReleaseAsync()); Assert.That(clip != null, Is.True);
            yield return Success(borrower.ReleaseAsync()); Assert.That(Resources.FindObjectsOfTypeAll<AudioClip>().Any(c => c.GetInstanceID() == clipId), Is.False);
        }
        [UnityTest] public IEnumerator BorrowedOnlyContextDoesNotUnloadItsAssets()
        {
            var context = Voice(LocalizationResourceOwnership.Borrowed); yield return Success(Change(context, "en"));
            var clip = context.GetAsset<AudioClip>(LocalizationResourceFixtures.PrimaryKey);
            int clipId = clip.GetInstanceID();
            yield return Success(context.ReleaseAsync()); Assert.That(clip != null, Is.True);
        }
        [UnityTest] public IEnumerator CancelledOwnedLoadReleasesLateResources_WithoutHarmingAnotherOwner()
        {
            var first = Voice(); yield return Success(Change(first, "ru"));
            var clip = first.GetAsset<AudioClip>(LocalizationResourceFixtures.PrimaryKey);
            int clipId = clip.GetInstanceID();
            var canceled = Voice(); var loading = Change(canceled, "ru");
            yield return Success(canceled.ReleaseAsync()); yield return Done(loading);
            Assert.That(loading.IsCanceled, Is.True); Assert.That(clip != null, Is.True);
            yield return Success(first.ReleaseAsync()); Assert.That(Resources.FindObjectsOfTypeAll<AudioClip>().Any(c => c.GetInstanceID() == clipId), Is.False);
        }
        [UnityTest] public IEnumerator LastReadyOwnerCanCloseWhileAnotherOwnerIsStillLoading()
        {
            var first = Voice(); var next = Voice(); yield return Success(Change(first, "ru"));
            var loading = Change(next, "ru");
            yield return Success(first.ReleaseAsync()); yield return Success(loading);
            Assert.That(next.GetAsset<AudioClip>(LocalizationResourceFixtures.PrimaryKey).loadState, Is.EqualTo(AudioDataLoadState.Loaded));
        }
        [UnityTest] public IEnumerator SimultaneousOwnersSurviveOneCancellation()
        {
            var canceled = Voice(); var survivor = Voice();
            var old = Change(canceled, "ru"); var fresh = Change(survivor, "ru");
            yield return Success(canceled.ReleaseAsync()); yield return Done(old); yield return Success(fresh);
            Assert.That(old.IsCanceled, Is.True);
            Assert.That(survivor.GetAsset<AudioClip>(LocalizationResourceFixtures.PrimaryKey).loadState, Is.EqualTo(AudioDataLoadState.Loaded));
        }
        [Test] public void ReassigningAnAddressKeepsTheNewRootAfterOldAliasesClose()
        {
            var type = typeof(LocalizationRuntime).Assembly.GetType("SimplyLocalize.LocalizationResourceLease", true);
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            object Lease() => Activator.CreateInstance(type, flags, null, new object[] { false, 1L, "Aliases", "en" }, null);
            var oldLease = Lease(); var nextLease = Lease();
            var oldRoot = new Texture2D(1, 1); var nextRoot = new Texture2D(1, 1);
            var addresses = (IDictionary)type.GetField("Addresses", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).GetValue(null);
            void Track(object lease, string path, Object root) => type.GetMethod("TrackLoaded", flags).Invoke(lease, new object[] { path, "en", root });
            void Close(object lease) => ((Task)type.GetMethod("ReleaseAsync", flags).Invoke(lease, null)).GetAwaiter().GetResult();
            try
            {
                Track(oldLease, "alias/first", oldRoot); Track(oldLease, "alias/shared", oldRoot);
                Track(nextLease, "ALIAS/SHARED", nextRoot);
                Close(oldLease);
                Assert.That(addresses.Contains("alias/first"), Is.False);
                Assert.That(addresses.Contains("alias/shared"), Is.True);
                Close(nextLease);
                Assert.That(addresses.Contains("alias/shared"), Is.False);
            }
            finally { Close(oldLease); Close(nextLease); Object.DestroyImmediate(oldRoot); Object.DestroyImmediate(nextRoot); }
        }

        [UnityTest] public IEnumerator PreviousSessionCannotReleaseAssetsOfANewSession()
        {
            var old = Voice(); yield return Success(Change(old, "en"));
            var lease = typeof(LocalizationRuntime).Assembly.GetType("SimplyLocalize.LocalizationResourceLease");
            lease.GetMethod("ResetSession", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).Invoke(null, null);
            var current = Voice(); yield return Success(Change(current, "en"));
            var clip = current.GetAsset<AudioClip>(LocalizationResourceFixtures.PrimaryKey); int id = clip.GetInstanceID();
            Assert.Throws<OperationCanceledException>(() => old.GetAsset<AudioClip>(LocalizationResourceFixtures.PrimaryKey));
            yield return Success(old.ReleaseAsync());
            Assert.That(current.GetAsset<AudioClip>(LocalizationResourceFixtures.PrimaryKey).loadState, Is.EqualTo(AudioDataLoadState.Loaded));
            yield return Success(current.ReleaseAsync());
            Assert.That(Resources.FindObjectsOfTypeAll<AudioClip>().Any(c => c.GetInstanceID() == id), Is.False);
        }
        [UnityTest] public IEnumerator IndependentContextsShareFallbackWithoutChangingEachOthersLanguage()
        {
            var russian = Voice();
            var english = Voice();
            yield return Success(Change(russian, "ru"));
            yield return Success(Change(english, "en"));

            Assert.That(russian.GetChannel().TryGetAsset<AudioClip>(LocalizationResourceFixtures.PrimaryKey, out var localized), Is.True);
            Assert.That(russian.GetChannel().TryGetAsset<AudioClip>(LocalizationResourceFixtures.FallbackKey, out var fallback), Is.True);
            var englishPrimary = english.GetAsset<AudioClip>(LocalizationResourceFixtures.PrimaryKey);
            Assert.That(localized.Language, Is.EqualTo("ru"));
            Assert.That(fallback.Language, Is.EqualTo("en"));
            Assert.That(localized.Asset, Is.Not.SameAs(englishPrimary));
            Assert.That(fallback.Asset, Is.SameAs(english.GetAsset<AudioClip>(LocalizationResourceFixtures.FallbackKey)));
            Assert.That(((AudioClip)fallback.Asset).loadState, Is.EqualTo(AudioDataLoadState.Loaded));
            int fallbackId = fallback.Asset.GetInstanceID();

            yield return Success(Change(russian, "en"));
            yield return Success(Change(russian, "ru"));
            Assert.That(english.GetChannel().RequestedLanguage, Is.EqualTo("en"));
            Assert.That(english.GetAsset<AudioClip>(LocalizationResourceFixtures.PrimaryKey), Is.SameAs(englishPrimary));
            yield return Success(russian.ReleaseAsync());
            Assert.That(english.GetAsset<AudioClip>(LocalizationResourceFixtures.FallbackKey).loadState, Is.EqualTo(AudioDataLoadState.Loaded));
            yield return Success(english.ReleaseAsync());
            Assert.That(Resources.FindObjectsOfTypeAll<AudioClip>().Any(c => c.GetInstanceID() == fallbackId), Is.False);
        }

        [UnityTest] public IEnumerator AssetBudgetRejectsSwitchAndReleasesCandidate_KeepingActiveTexture()
        {
            config.Sets[0].SetSources("en", new LocalizationSourceReference("resources", LocalizationResourceFixtures.ImageTable, LocalizationSourceKind.AssetTable));
            var context = new LocalizationRuntime(config,
                options: new LocalizationRuntimeOptions { Ownership = LocalizationResourceOwnership.Owned, MaxAssetEntries = 1 });
            contexts.Add(context); yield return Success(Change(context, "en"));
            var texture = context.GetAsset<Texture2D>("picture");
            int textureId = texture.GetInstanceID();
            var failed = Change(context, "ru"); yield return Done(failed);
            Assert.That(failed.IsFaulted, Is.True); Assert.That(failed.Exception.ToString(), Does.Contain("limits"));
            Assert.That(context.GetChannel().RequestedLanguage, Is.EqualTo("en")); Assert.That(texture != null, Is.True);
            Assert.That(context.GetAsset<Texture2D>("picture"), Is.SameAs(texture));
            yield return Success(context.ReleaseAsync()); Assert.That(Resources.FindObjectsOfTypeAll<Texture2D>().Any(t => t.GetInstanceID() == textureId), Is.False);
        }
    }
}
