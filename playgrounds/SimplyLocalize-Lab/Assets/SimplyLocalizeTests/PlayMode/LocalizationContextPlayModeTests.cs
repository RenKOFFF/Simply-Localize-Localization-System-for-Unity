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
    public class LocalizationContextPlayModeTests
    {
        private LocalizationConfig config;
        private LanguageProfile en, ru;
        private readonly List<LocalizationContext> contexts = new();
        [SetUp] public void Setup()
        {
            config = ScriptableObject.CreateInstance<LocalizationConfig>(); config.loadTextData = false;
            en = ScriptableObject.CreateInstance<LanguageProfile>(); en.languageCode = "en";
            ru = ScriptableObject.CreateInstance<LanguageProfile>(); ru.languageCode = "ru";
            config.languages.Add(en); config.languages.Add(ru); config.defaultLanguage = config.fallbackLanguage = en;
        }
        private LocalizationContext Voice(LocalizationResourceOwnership ownership = LocalizationResourceOwnership.Owned)
        {
            var context = new LocalizationContext(config, new[] { "en", "ru" }.Select(code => new ResourcesLanguageSources(code,
                assetTables: new[] { "NightShift/Worlds/Precinct/Voice/" + code + "/Radio" })), new LocalizationContextOptions { Ownership = ownership });
            contexts.Add(context); return context;
        }
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
            contexts.Clear(); Object.Destroy(config); Object.Destroy(en); Object.Destroy(ru); yield return null;
        }
        [UnityTest] public IEnumerator AudioIsReady_AndLastOwnerReleasesSharedNativeAssets()
        {
            var first = Voice(); var second = Voice();
            yield return Success(first.ChangeLanguageAsync("ru"));
            var clip = first.ResolveAsset<AudioClip>("Radio/01").Value;
            int clipId = clip.GetInstanceID();
            Assert.That(clip != null, Is.True); Assert.That(clip.loadState, Is.EqualTo(AudioDataLoadState.Loaded));
            yield return Success(second.ChangeLanguageAsync("ru"));
            Assert.That(second.ResolveAsset<AudioClip>("Radio/01").Value, Is.SameAs(clip));
            yield return Success(first.ReleaseAsync());
            Assert.That(clip != null, Is.True); Assert.That(second.ResolveAsset<AudioClip>("Radio/01").Value.loadState, Is.EqualTo(AudioDataLoadState.Loaded));
            yield return Success(second.ReleaseAsync());
            Assert.That(Resources.FindObjectsOfTypeAll<AudioClip>().Any(c => c.GetInstanceID() == clipId), Is.False, "Last owner must unload the native asset; Editor wrapper equality is not a residency check.");
        }
        [UnityTest] public IEnumerator BorrowedContextPinsAnOwnedResourceUntilBothClose()
        {
            var owner = Voice(); var borrower = Voice(LocalizationResourceOwnership.Borrowed);
            yield return Success(owner.ChangeLanguageAsync("en")); yield return Success(borrower.ChangeLanguageAsync("en"));
            var clip = borrower.ResolveAsset<AudioClip>("Radio/01").Value;
            int clipId = clip.GetInstanceID();
            yield return Success(owner.ReleaseAsync()); Assert.That(clip != null, Is.True);
            yield return Success(borrower.ReleaseAsync()); Assert.That(Resources.FindObjectsOfTypeAll<AudioClip>().Any(c => c.GetInstanceID() == clipId), Is.False);
        }
        [UnityTest] public IEnumerator BorrowedOnlyContextDoesNotUnloadItsAssets()
        {
            var context = Voice(LocalizationResourceOwnership.Borrowed); yield return Success(context.ChangeLanguageAsync("en"));
            var clip = context.ResolveAsset<AudioClip>("Radio/01").Value;
            int clipId = clip.GetInstanceID();
            yield return Success(context.ReleaseAsync()); Assert.That(clip != null, Is.True);
        }
        [UnityTest] public IEnumerator CancelledOwnedLoadReleasesLateResources_WithoutHarmingAnotherOwner()
        {
            var first = Voice(); yield return Success(first.ChangeLanguageAsync("ru"));
            var clip = first.ResolveAsset<AudioClip>("Radio/01").Value;
            int clipId = clip.GetInstanceID();
            var canceled = Voice(); var loading = canceled.ChangeLanguageAsync("ru");
            yield return Success(canceled.ReleaseAsync()); yield return Done(loading);
            Assert.That(loading.IsCanceled, Is.True); Assert.That(clip != null, Is.True);
            yield return Success(first.ReleaseAsync()); Assert.That(Resources.FindObjectsOfTypeAll<AudioClip>().Any(c => c.GetInstanceID() == clipId), Is.False);
        }
        [UnityTest] public IEnumerator LastReadyOwnerCanCloseWhileAnotherOwnerIsStillLoading()
        {
            var first = Voice(); var next = Voice(); yield return Success(first.ChangeLanguageAsync("ru"));
            var loading = next.ChangeLanguageAsync("ru");
            yield return Success(first.ReleaseAsync()); yield return Success(loading);
            Assert.That(next.ResolveAsset<AudioClip>("Radio/01").Value.loadState, Is.EqualTo(AudioDataLoadState.Loaded));
        }
        [UnityTest] public IEnumerator SimultaneousOwnersSurviveOneCancellation()
        {
            var canceled = Voice(); var survivor = Voice();
            var old = canceled.ChangeLanguageAsync("ru"); var fresh = survivor.ChangeLanguageAsync("ru");
            yield return Success(canceled.ReleaseAsync()); yield return Done(old); yield return Success(fresh);
            Assert.That(old.IsCanceled, Is.True);
            Assert.That(survivor.ResolveAsset<AudioClip>("Radio/01").Value.loadState, Is.EqualTo(AudioDataLoadState.Loaded));
        }
        [UnityTest] public IEnumerator PreviousSessionCannotReleaseAssetsOfANewSession()
        {
            var old = Voice(); yield return Success(old.ChangeLanguageAsync("en"));
            var lease = typeof(LocalizationContext).Assembly.GetType("SimplyLocalize.LocalizationResourceLease");
            lease.GetMethod("ResetSession", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).Invoke(null, null);
            var current = Voice(); yield return Success(current.ChangeLanguageAsync("en"));
            var clip = current.ResolveAsset<AudioClip>("Radio/01").Value; int id = clip.GetInstanceID();
            Assert.Throws<ObjectDisposedException>(() => old.ResolveAsset<AudioClip>("Radio/01"));
            yield return Success(old.ReleaseAsync());
            Assert.That(current.ResolveAsset<AudioClip>("Radio/01").Value.loadState, Is.EqualTo(AudioDataLoadState.Loaded));
            yield return Success(current.ReleaseAsync());
            Assert.That(Resources.FindObjectsOfTypeAll<AudioClip>().Any(c => c.GetInstanceID() == id), Is.False);
        }
        [UnityTest] public IEnumerator AssetBudgetRejectsSwitchAndReleasesCandidate_KeepingActiveTexture()
        {
            var context = new LocalizationContext(config, new[] {
                new ResourcesLanguageSources("en", assetTables: new[] { "ScopedResourcesProbe/en/SLScoped_SelectedTable" }),
                new ResourcesLanguageSources("ru", assetTables: new[] { "NightShift/Worlds/Precinct/Voice/ru/Radio" }) },
                new LocalizationContextOptions { Ownership = LocalizationResourceOwnership.Owned, MaxAssetEntries = 1 });
            contexts.Add(context); yield return Success(context.ChangeLanguageAsync("en"));
            var texture = context.ResolveAsset<Texture2D>("picture").Value;
            int textureId = texture.GetInstanceID();
            var failed = context.ChangeLanguageAsync("ru"); yield return Done(failed);
            Assert.That(failed.IsFaulted, Is.True); Assert.That(failed.Exception.ToString(), Does.Contain("limits"));
            Assert.That(context.CurrentLanguage, Is.EqualTo("en")); Assert.That(texture != null, Is.True);
            Assert.That(context.AssetEntryCount, Is.EqualTo(1));
            yield return Success(context.ReleaseAsync()); Assert.That(Resources.FindObjectsOfTypeAll<Texture2D>().Any(t => t.GetInstanceID() == textureId), Is.False);
        }
    }
}
