using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using SimplyLocalize.Data;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
namespace SimplyLocalize.Tests
{
    public class ResourceDiagnosticsTests
    {
        private LocalizationConfig config;
        private LanguageProfile en, ru;
        private readonly List<LocalizationContext> contexts = new();
        [SetUp] public void Setup()
        {
            LocalizationResourceDiagnostics.RecordingEnabled = true; LocalizationResourceDiagnostics.ClearHistory();
            config = ScriptableObject.CreateInstance<LocalizationConfig>(); config.loadTextData = false;
            en = ScriptableObject.CreateInstance<LanguageProfile>(); en.languageCode = "en";
            ru = ScriptableObject.CreateInstance<LanguageProfile>(); ru.languageCode = "ru";
            config.languages.Add(en); config.languages.Add(ru); config.defaultLanguage = config.fallbackLanguage = en;
        }
        private LocalizationContext Create(string name, LocalizationResourceOwnership ownership = LocalizationResourceOwnership.Owned, int maxAssets = 4096)
        {
            var context = new LocalizationContext(config, new[] { "en", "ru" }.Select(code => new ResourcesLanguageSources(code, assetTables: new[] { LocalizationResourceFixtures.VoiceTable(code) })), new LocalizationContextOptions { DiagnosticName = name, Ownership = ownership, MaxAssetEntries = maxAssets });
            contexts.Add(context); return context;
        }
        private static IEnumerator Done(Task task, bool success = true)
        { float end = Time.realtimeSinceStartup + 20; while (!task.IsCompleted && Time.realtimeSinceStartup < end) yield return null; Assert.That(task.IsCompleted, Is.True); if (success) Assert.That(task.IsCompletedSuccessfully, Is.True, task.Exception?.ToString()); }
        [UnityTearDown] public IEnumerator Cleanup()
        { foreach (var c in contexts) yield return Done(c.ReleaseAsync()); contexts.Clear(); LocalizationResourceDiagnostics.RecordingEnabled = false; LocalizationResourceDiagnostics.ClearHistory(); Object.Destroy(config); Object.Destroy(en); Object.Destroy(ru); }

        [UnityTest] public IEnumerator SharedOwners_ImmutableSnapshotDoesNotPreventLastNativeUnload()
        {
            var a = Create("Intro"); var b = Create("Gameplay"); yield return Done(a.ChangeLanguageAsync("en")); yield return Done(b.ChangeLanguageAsync("en"));
            int id = a.ResolveAsset<AudioClip>(LocalizationResourceFixtures.PrimaryKey).Value.GetInstanceID();
            var before = LocalizationResourceDiagnostics.Capture(); var row = before.Resources.Single(r => r.InstanceId == id);
            Assert.That(row.ReferenceCount, Is.EqualTo(2)); Assert.That(row.Owners.Select(o => o.ContextName), Is.EquivalentTo(new[] { "Intro", "Gameplay" }));
            Assert.Throws<NotSupportedException>(() => ((IList<LocalizationResourceInfo>)before.Resources).Clear());
            yield return Done(a.ReleaseAsync()); Assert.That(LocalizationResourceDiagnostics.Capture().Resources.Single(r => r.InstanceId == id).ReferenceCount, Is.EqualTo(1));
            Assert.That(row.ReferenceCount, Is.EqualTo(2), "A captured snapshot must remain immutable.");
            yield return Done(b.ReleaseAsync()); Assert.That(Resources.FindObjectsOfTypeAll<AudioClip>().Any(c => c.GetInstanceID() == id), Is.False);
            Assert.That(LocalizationResourceDiagnostics.Capture().History.Any(e => e.InstanceId == id && e.Kind == LocalizationResourceEventKind.Unloaded), Is.True);
            Assert.That(before.Resources.Count, Is.GreaterThan(0), "Retaining a snapshot must not retain native assets.");
        }
        [UnityTest] public IEnumerator BorrowedRelease_IsNotReportedAsUnload()
        {
            var c = Create("External audio", LocalizationResourceOwnership.Borrowed); yield return Done(c.ChangeLanguageAsync("en"));
            int id = c.ResolveAsset<AudioClip>(LocalizationResourceFixtures.PrimaryKey).Value.GetInstanceID(); yield return Done(c.ReleaseAsync());
            var snapshot = LocalizationResourceDiagnostics.Capture(); Assert.That(snapshot.Resources, Is.Empty);
            Assert.That(snapshot.History.Any(e => e.InstanceId == id && e.Kind == LocalizationResourceEventKind.BorrowedReleased), Is.True);
            Assert.That(snapshot.History.Any(e => e.Kind == LocalizationResourceEventKind.Unloaded), Is.False);
            Assert.That(Resources.FindObjectsOfTypeAll<AudioClip>().Any(a => a.GetInstanceID() == id), Is.True);
            var owner = Create("Cleanup after detached external consumer"); yield return Done(owner.ChangeLanguageAsync("en")); yield return Done(owner.ReleaseAsync());
        }
        [UnityTest] public IEnumerator LateRecording_ReportsLiveFallbackWithoutInventingHistory()
        {
            LocalizationResourceDiagnostics.RecordingEnabled = false;
            var c = Create("Russian voice"); yield return Done(c.ChangeLanguageAsync("ru"));
            var first = LocalizationResourceDiagnostics.Capture(); Assert.That(first.History, Is.Empty);
            Assert.That(first.Resources.SelectMany(r => r.Owners).Any(o => o.RequestedLanguage == "ru" && o.SourceLanguage == "en" && o.Address == LocalizationResourceFixtures.VoiceTable("en")), Is.True);
            LocalizationResourceDiagnostics.RecordingEnabled = true;
            for (int i = 0; i < 100; i++) c.ResolveAsset<AudioClip>(LocalizationResourceFixtures.PrimaryKey);
            Assert.That(LocalizationResourceDiagnostics.Capture().History, Is.Empty, "Ready reads do not create events.");
            Assert.That(LocalizationResourceDiagnostics.Capture().Sets.Single().Owner.Stage, Is.EqualTo(LocalizationResourceStage.Ready));
        }
        [UnityTest] public IEnumerator CancelPending_ShowsRequestAndDrainsWithoutRetainingSnapshots()
        {
            var c = Create("Cancelled"); var pending = c.ChangeLanguageAsync("en");
            var loading = LocalizationResourceDiagnostics.Capture(); Assert.That(loading.Sets.Single().PendingAddresses, Is.Not.Empty);
            var release = c.ReleaseAsync(); yield return Done(release); yield return Done(pending, false);
            Assert.That(pending.IsCanceled, Is.True); Assert.That(LocalizationResourceDiagnostics.Capture().Sets, Is.Empty);
            Assert.That(LocalizationResourceDiagnostics.Capture().History.Any(e => e.Kind == LocalizationResourceEventKind.Cancelled), Is.True);
        }
        [UnityTest] public IEnumerator LimitsFailure_IsRecordedAndCandidateIsReleased()
        {
            var c = Create("Limited", maxAssets: 1); var operation = c.ChangeLanguageAsync("en"); yield return Done(operation, false);
            Assert.That(operation.IsFaulted, Is.True); _ = operation.Exception;
            var snapshot = LocalizationResourceDiagnostics.Capture(); Assert.That(snapshot.Sets, Is.Empty);
            Assert.That(snapshot.History.Any(e => e.Kind == LocalizationResourceEventKind.Failed && e.Detail.Contains("limits")), Is.True);
        }
        [UnityTest] public IEnumerator History_IsBoundedAndClearingItDoesNotUnloadLiveResources()
        {
            for (int i = 0; i < 35; i++) { var c = Create("Cycle"); yield return Done(c.ChangeLanguageAsync("en")); yield return Done(c.ReleaseAsync()); }
            var active = Create("Keep alive"); yield return Done(active.ChangeLanguageAsync("en"));
            var before = LocalizationResourceDiagnostics.Capture(); Assert.That(before.History.Count, Is.EqualTo(LocalizationResourceDiagnostics.HistoryCapacity)); Assert.That(before.DroppedEvents, Is.GreaterThan(0));
            LocalizationResourceDiagnostics.ClearHistory(); var after = LocalizationResourceDiagnostics.Capture();
            Assert.That(after.History, Is.Empty); Assert.That(after.DroppedEvents, Is.Zero); Assert.That(after.Resources.Count, Is.EqualTo(before.Resources.Count)); Assert.That(active.ResolveAsset<AudioClip>(LocalizationResourceFixtures.PrimaryKey).Value.loadState, Is.EqualTo(AudioDataLoadState.Loaded));
        }
        [UnityTest] public IEnumerator SessionReset_ClearsStaleDiagnostics()
        {
            var c = Create("Old session"); yield return Done(c.ChangeLanguageAsync("en")); int session = LocalizationResourceDiagnostics.Capture().Session;
            typeof(LocalizationContext).Assembly.GetType("SimplyLocalize.LocalizationResourceLease").GetMethod("ResetSession", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
            var after = LocalizationResourceDiagnostics.Capture(); Assert.That(after.Session, Is.Not.EqualTo(session)); Assert.That(after.History, Is.Empty); Assert.That(after.Sets, Is.Empty);
            var fresh = Create("New session"); yield return Done(fresh.ChangeLanguageAsync("en")); yield return Done(c.ReleaseAsync()); Assert.That(LocalizationResourceDiagnostics.Capture().Sets.Single().Owner.ContextName, Is.EqualTo("New session"));
        }
    }
}
