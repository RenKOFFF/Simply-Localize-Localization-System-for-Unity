using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;

namespace SimplyLocalize.Tests
{
    public class SourceProviderResourcesTests
    {
        private readonly List<LocalizationSourceLease> leases = new();
        private static readonly LocalizationRuntimeOptions Owned = new() { Ownership = LocalizationResourceOwnership.Owned };
        private static LocalizationSourceRequest Request(string address, string language = "en", LocalizationRuntimeOptions options = null,
            string[] keys = null, LocalizationSourceKind kind = LocalizationSourceKind.AssetTable) =>
            new(new LocalizationSourceReference("resources", address, kind), language, keys, options ?? Owned);
        private static IEnumerator Done(Task task, bool success = true)
        {
            float deadline = Time.realtimeSinceStartup + 15;
            while (!task.IsCompleted && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(task.IsCompleted, Is.True, "Source operation timed out.");
            if (success) Assert.That(task.IsCompletedSuccessfully, Is.True, task.Exception?.ToString());
        }
        private IEnumerator Load(LocalizationSourceRegistry registry, LocalizationSourceRequest request, Action<LocalizationSourceLease> use)
        {
            var task = registry.LoadAsync(request); yield return Done(task);
            leases.Add(task.Result); use(task.Result);
        }
        [UnityTearDown] public IEnumerator Cleanup()
        { foreach (var lease in leases) yield return Done(lease.ReleaseAsync()); leases.Clear(); }

        [UnityTest] public IEnumerator OneRequestedKeyStillLoadsTheWholeResourcesTableAndItsDependencies()
        {
            const string address = "SimplyLocalizeTests/Lifecycle/Voice/Large";
            var registry = new LocalizationSourceRegistry();
            yield return Load(registry, Request(address), _ => { });
            yield return Done(leases[0].ReleaseAsync()); // Establish a cold native baseline for these exclusive fixtures.
            Assert.That(Resources.FindObjectsOfTypeAll<AudioClip>().Count(c => c.name.StartsWith("SLTest_")), Is.Zero);
            yield return Load(registry, Request(address, keys: new[] { "large/0" }), lease =>
            {
                Assert.That(lease.Data.Assets.Count, Is.EqualTo(1));
                Assert.That(lease.Data.SourceEntryCount, Is.EqualTo(64));
                Assert.That(Resources.FindObjectsOfTypeAll<AudioClip>().Count(c => c.name.StartsWith("SLTest_")), Is.EqualTo(4));
                Assert.That(((AudioClip)lease.Data.Assets["large/0"]).loadState, Is.EqualTo(AudioDataLoadState.Loaded));
            });
            yield return Done(leases[1].ReleaseAsync());
            Assert.That(Resources.FindObjectsOfTypeAll<AudioClip>().Count(c => c.name.StartsWith("SLTest_")), Is.Zero);
        }

        [UnityTest] public IEnumerator DifferentProviderInstancesShareNativeOwnershipUntilLastRelease()
        {
            var first = new LocalizationSourceRegistry(); var second = new LocalizationSourceRegistry();
            yield return Load(first, Request(LocalizationResourceFixtures.VoiceTable("en")), _ => { });
            yield return Load(second, Request(LocalizationResourceFixtures.VoiceTable("en")), _ => { });
            var clip = (AudioClip)leases[0].Data.Assets[LocalizationResourceFixtures.PrimaryKey]; int id = clip.GetInstanceID();
            Assert.That(leases[1].Data.Assets[LocalizationResourceFixtures.PrimaryKey], Is.SameAs(clip));
            yield return Done(leases[0].ReleaseAsync()); Assert.That(clip.loadState, Is.EqualTo(AudioDataLoadState.Loaded));
            yield return Done(leases[1].ReleaseAsync());
            Assert.That(Resources.FindObjectsOfTypeAll<AudioClip>().Any(c => c.GetInstanceID() == id), Is.False);
        }

        [UnityTest] public IEnumerator BorrowedOwnerPinsOwnedDataAndBorrowedOnlyDoesNotUnloadIt()
        {
            var registry = new LocalizationSourceRegistry();
            var borrowed = new LocalizationRuntimeOptions { Ownership = LocalizationResourceOwnership.Borrowed };
            yield return Load(registry, Request(LocalizationResourceFixtures.VoiceTable("en"), options: borrowed), _ => { });
            var clip = (AudioClip)leases[0].Data.Assets[LocalizationResourceFixtures.PrimaryKey]; int id = clip.GetInstanceID();
            yield return Done(leases[0].ReleaseAsync()); Assert.That(clip != null, Is.True);
            yield return Load(registry, Request(LocalizationResourceFixtures.VoiceTable("en")), _ => { });
            yield return Load(registry, Request(LocalizationResourceFixtures.VoiceTable("en"), options: borrowed), _ => { });
            yield return Done(leases[1].ReleaseAsync()); Assert.That(clip != null, Is.True);
            yield return Done(leases[2].ReleaseAsync());
            Assert.That(Resources.FindObjectsOfTypeAll<AudioClip>().Any(c => c.GetInstanceID() == id), Is.False);
        }

        [UnityTest] public IEnumerator CancelledNativeLoadDrainsWithoutDamagingAnotherOwner()
        {
            var registry = new LocalizationSourceRegistry();
            yield return Load(registry, Request(LocalizationResourceFixtures.VoiceTable("ru"), "ru"), _ => { });
            var clip = (AudioClip)leases[0].Data.Assets[LocalizationResourceFixtures.PrimaryKey]; int id = clip.GetInstanceID();
            using var cancellation = new CancellationTokenSource();
            var cancelled = registry.LoadAsync(Request(LocalizationResourceFixtures.VoiceTable("ru"), "ru"), cancellation.Token);
            cancellation.Cancel(); yield return Done(cancelled, false);
            Assert.That(cancelled.IsCanceled, Is.True); Assert.That(clip.loadState, Is.EqualTo(AudioDataLoadState.Loaded));
            yield return Done(leases[0].ReleaseAsync());
            Assert.That(Resources.FindObjectsOfTypeAll<AudioClip>().Any(c => c.GetInstanceID() == id), Is.False);
        }

        [UnityTest] public IEnumerator WrongTypeAndBudgetFailuresCleanUpAndRetryWorks()
        {
            var registry = new LocalizationSourceRegistry();
            var wrong = registry.LoadAsync(Request(LocalizationResourceFixtures.ImageTable, kind: LocalizationSourceKind.TextTable));
            yield return Done(wrong, false); Assert.That(wrong.IsFaulted, Is.True); _ = wrong.Exception;
            var limited = registry.LoadAsync(Request("SimplyLocalizeTests/Lifecycle/Voice/Large",
                options: new LocalizationRuntimeOptions { Ownership = LocalizationResourceOwnership.Owned, MaxAssetEntries = 63 }));
            yield return Done(limited, false); Assert.That(limited.Exception.ToString(), Does.Contain("limits"));
            Assert.That(Resources.FindObjectsOfTypeAll<AudioClip>().Any(c => c.name.StartsWith("SLTest_")), Is.False);
            yield return Load(registry, Request("SimplyLocalizeTests/Lifecycle/Voice/Large"), lease => Assert.That(lease.Data.Assets.Count, Is.EqualTo(64)));
        }

        [UnityTest] public IEnumerator PreviousSessionCannotReadOrReleaseNewSessionResources()
        {
            var old = new LocalizationSourceRegistry();
            yield return Load(old, Request(LocalizationResourceFixtures.VoiceTable("en")), _ => { });
            typeof(LocalizationRuntime).Assembly.GetType("SimplyLocalize.LocalizationResourceLease")
                .GetMethod("ResetSession", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
            var current = new LocalizationSourceRegistry();
            yield return Load(current, Request(LocalizationResourceFixtures.VoiceTable("en")), _ => { });
            var clip = (AudioClip)leases[1].Data.Assets[LocalizationResourceFixtures.PrimaryKey]; int id = clip.GetInstanceID();
            Assert.Throws<ObjectDisposedException>(() => _ = leases[0].Data);
            var stale = old.LoadAsync(Request(LocalizationResourceFixtures.VoiceTable("en"))); yield return Done(stale, false); Assert.That(stale.IsCanceled, Is.True);
            yield return Done(leases[0].ReleaseAsync()); Assert.That(clip.loadState, Is.EqualTo(AudioDataLoadState.Loaded));
            yield return Done(leases[1].ReleaseAsync());
            Assert.That(Resources.FindObjectsOfTypeAll<AudioClip>().Any(c => c.GetInstanceID() == id), Is.False);
        }

        [UnityTest] public IEnumerator TextSelectionStillEnforcesWholeSourceBudgetsAndReleasesRoot()
        {
            const string address = "SimplyLocalizeTests/Lifecycle/Text";
            var registry = new LocalizationSourceRegistry();
            var limits = new[]
            {
                new LocalizationRuntimeOptions { Ownership = LocalizationResourceOwnership.Owned, MaxSourceCharacters = 10 },
                new LocalizationRuntimeOptions { Ownership = LocalizationResourceOwnership.Owned, MaxTextEntries = 2 },
                new LocalizationRuntimeOptions { Ownership = LocalizationResourceOwnership.Owned, MaxTextCharacters = 15 }
            };
            foreach (var limit in limits)
            {
                var rejected = registry.LoadAsync(Request(address, options: limit, keys: new[] { "menu/play" }, kind: LocalizationSourceKind.TextTable));
                yield return Done(rejected, false);
                Assert.That(rejected.Exception.ToString(), Does.Contain("limits"));
            }
            yield return Load(registry, Request(address, keys: new[] { "menu/play" }, kind: LocalizationSourceKind.TextTable), lease =>
            {
                Assert.That(lease.Data.Text.Count, Is.EqualTo(1));
                Assert.That(lease.Data.Text["menu/play"], Is.EqualTo("Play"));
                Assert.That(lease.Data.SourceEntryCount, Is.EqualTo(3));
            });
            // Locate the exact root already loaded by the provider, without retaining a second owner.
            var root = Resources.Load<TextAsset>(address); int id = root.GetInstanceID();
            yield return Done(leases[0].ReleaseAsync());
            Assert.That(Resources.FindObjectsOfTypeAll<TextAsset>().Any(a => a.GetInstanceID() == id), Is.False);
        }

        [UnityTest] public IEnumerator SoleCancelledLoadCleansItsLateResultAndCanRetry()
        {
            const string address = "SimplyLocalizeTests/Lifecycle/Voice/Large";
            var registry = new LocalizationSourceRegistry();
            using var cancellation = new CancellationTokenSource();
            var pending = registry.LoadAsync(Request(address), cancellation.Token);
            cancellation.Cancel(); yield return Done(pending, false);
            Assert.That(pending.IsCanceled, Is.True);
            Assert.That(Resources.FindObjectsOfTypeAll<AudioClip>().Any(c => c.name.StartsWith("SLTest_")), Is.False);
            yield return Load(registry, Request(address), lease => Assert.That(lease.Data.Assets.Count, Is.EqualTo(64)));
        }

        [UnityTest] public IEnumerator DifferentTablesKeepSharedFallbackDependenciesUntilLastOwner()
        {
            var registry = new LocalizationSourceRegistry();
            yield return Load(registry, Request(LocalizationResourceFixtures.VoiceTable("en")), _ => { });
            yield return Load(registry, Request("SimplyLocalizeTests/Lifecycle/Voice/Large"), _ => { });
            var fallback = (AudioClip)leases[0].Data.Assets["audio/fallback"]; int id = fallback.GetInstanceID();
            Assert.That(leases[1].Data.Assets.Values, Does.Contain(fallback));
            yield return Done(leases[0].ReleaseAsync());
            Assert.That(fallback.loadState, Is.EqualTo(AudioDataLoadState.Loaded));
            yield return Done(leases[1].ReleaseAsync());
            Assert.That(Resources.FindObjectsOfTypeAll<AudioClip>().Any(c => c.GetInstanceID() == id), Is.False);
        }

        [UnityTest] public IEnumerator AppearanceKeepsItsFontAtlasAndMaterialUntilLastOwner()
        {
            var registry = new LocalizationSourceRegistry();
            var request = Request("SimplyLocalizeTests/Lifecycle/Appearance", kind: LocalizationSourceKind.Appearance);
            yield return Load(registry, request, _ => { }); yield return Load(registry, request, _ => { });
            var appearance = leases[0].Data.Appearance; var font = appearance.primaryFont;
            int fontId = font.GetInstanceID(), textureId = font.atlasTextures[0].GetInstanceID(), materialId = font.material.GetInstanceID();
            yield return Done(leases[0].ReleaseAsync()); Assert.That(leases[1].Data.Appearance.primaryFont != null, Is.True);
            yield return Done(leases[1].ReleaseAsync());
            Assert.That(Resources.FindObjectsOfTypeAll<TMP_FontAsset>().Any(f => f.GetInstanceID() == fontId), Is.False);
            Assert.That(Resources.FindObjectsOfTypeAll<Texture2D>().Any(f => f.GetInstanceID() == textureId), Is.False);
            Assert.That(Resources.FindObjectsOfTypeAll<Material>().Any(f => f.GetInstanceID() == materialId), Is.False);
        }
    }
}
