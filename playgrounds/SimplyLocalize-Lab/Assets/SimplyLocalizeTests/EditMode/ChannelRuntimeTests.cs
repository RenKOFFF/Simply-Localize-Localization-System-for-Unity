using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace SimplyLocalize.Tests
{
    public class ChannelRuntimeTests
    {
        private LocalizationProjectConfig config;
        private readonly List<LocalizationRuntime> runtimes = new();
        private readonly List<LocalizationChannelLease> retained = new();
        private readonly List<LocalizationSetLease> registrations = new();
        private Probe provider;
        private string mainSet, voice, voiceSet, independent;
        private LocalizationEntryReference mainEntry;
        private static IEnumerator Done(Task task, bool success = true)
        {
            float deadline = Time.realtimeSinceStartup + 10;
            while (!task.IsCompleted && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(task.IsCompleted, Is.True, "Channel operation did not drain.");
            if (success) Assert.That(task.IsCompletedSuccessfully, Is.True, task.Exception?.ToString());
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            foreach (var runtime in runtimes) yield return Done(runtime.ReleaseAsync());
            foreach (var lease in retained) yield return Done(lease.ReleaseAsync());
            foreach (var lease in registrations) yield return Done(lease.ReleaseAsync());
            runtimes.Clear(); retained.Clear(); registrations.Clear();
            if (config != null) UnityEngine.Object.DestroyImmediate(config);
            if (provider != null) Assert.That(provider.Active, Is.Zero, "All source owners must be returned.");
        }
        private LocalizationRuntime Create(bool channels = false, LocalizationContextOptions options = null)
        {
            config = LocalizationProjectConfig.Create(new[] { "en", "ru", "fr" });
            var common = config.Sets[0]; mainSet = common.Id;
            mainEntry = new(config.MainChannelId, common.AddEntry("key").Id); common.AddEntry("fallback"); AddSources(common, "main");
            if (channels)
            {
                var voiceDefinition = config.AddChannel("Voice", new[] { "en", "ru" }); voiceDefinition.Mode = LocalizationChannelMode.FollowMain;
                var voiceContent = config.AddSet("Voice content"); voiceContent.AddEntry("key"); voiceContent.AddEntry("fallback"); AddSources(voiceContent, "voice");
                voice = voiceDefinition.Id; voiceSet = voiceContent.Id; voiceDefinition.IncludeSet(voiceSet, true);
                var independentDefinition = config.AddChannel("Independent", new[] { "en", "ru" });
                independent = independentDefinition.Id; independentDefinition.IncludeSet(mainSet, true);
            }
            provider = new Probe(); return CreateRuntime(options);
        }
        private LocalizationRuntime CreateRuntime(LocalizationContextOptions options = null)
        {
            var registry = new LocalizationSourceRegistry(false); registry.Register("probe", provider);
            var runtime = new LocalizationRuntime(config, registry, options); runtimes.Add(runtime); return runtime;
        }
        private static void AddSources(LocalizationContentSet set, string address)
        {
            foreach (string language in new[] { "en", "ru", "fr" }) set.SetSources(language, new LocalizationSourceReference("probe", address + "/" + language, LocalizationSourceKind.TextTable));
        }
        private static string Text(LocalizationRuntime runtime, string channel = null, string key = "key", string set = null)
        { Assert.That(runtime.GetChannel(channel).TryGetText(key, out var value, set), Is.True); return value.Text; }

        [UnityTest] public IEnumerator LinkedChannelsPublishTogetherAndIndependentChannelDoesNotLoad()
        {
            var runtime = Create(true); yield return Done(runtime.InitializeAsync());
            int events = 0; var independentState = runtime.GetChannel(independent); provider.Loads.Clear();
            runtime.Changed += (r, changed) =>
            {
                events++; Assert.That(changed, Is.EquivalentTo(new[] { config.MainChannelId, voice }));
                Assert.That(Text(r), Is.EqualTo("main/ru")); Assert.That(Text(r, voice), Is.EqualTo("voice/ru"));
                Assert.That(r.GetChannel(independent), Is.SameAs(independentState));
            };
            yield return Done(runtime.SwitchLanguageAsync("ru")); Assert.That(events, Is.EqualTo(1));
            Assert.That(provider.Loads.Count(s => s == "main/en"), Is.EqualTo(1), "Only Main's fallback, not the independent channel, reloads.");
        }
        [UnityTest] public IEnumerator IndependentSwitchLeavesMainAndFollowerUntouched()
        {
            var runtime = Create(true); yield return Done(runtime.InitializeAsync());
            var main = runtime.GetChannel(); var follower = runtime.GetChannel(voice);
            yield return Done(runtime.SwitchLanguageAsync(independent, "ru"));
            Assert.That(Text(runtime, independent), Is.EqualTo("main/ru")); Assert.That(runtime.GetChannel(), Is.SameAs(main)); Assert.That(runtime.GetChannel(voice), Is.SameAs(follower));
            Assert.Throws<InvalidOperationException>(() => runtime.SwitchLanguageAsync(voice, "ru"));
        }
        [UnityTest] public IEnumerator MissingFollowerLanguageUsesItsDefaultAndRecordsRequestedLanguage()
        {
            var runtime = Create(true); yield return Done(runtime.InitializeAsync()); yield return Done(runtime.SwitchLanguageAsync("fr"));
            var follower = runtime.GetChannel(voice);
            Assert.That(follower.RequestedLanguage, Is.EqualTo("fr")); Assert.That(follower.ResolvedLanguage, Is.EqualTo("en"));
            Assert.That(follower.TryGetText("key", out var value), Is.True);
            Assert.That(value.Language, Is.EqualTo("en")); Assert.That(value.RequestedLanguage, Is.EqualTo("fr"));
        }
        [UnityTest] public IEnumerator FailedFollowerKeepsEveryPublishedParticipantAndRetryWorks()
        {
            var runtime = Create(true); yield return Done(runtime.InitializeAsync());
            var main = runtime.GetChannel(); var follower = runtime.GetChannel(voice); int owners = provider.Active;
            provider.Failure = "voice/ru";
            var failed = runtime.SwitchLanguageAsync("ru"); yield return Done(failed, false);
            Assert.That(failed.Exception.ToString(), Does.Contain("injected"));
            Assert.That(runtime.GetChannel(), Is.SameAs(main)); Assert.That(runtime.GetChannel(voice), Is.SameAs(follower)); Assert.That(provider.Active, Is.EqualTo(owners));
            provider.Failure = null; yield return Done(runtime.SwitchLanguageAsync("ru")); Assert.That(Text(runtime), Is.EqualTo("main/ru"));
        }
        [UnityTest] public IEnumerator NewestRequestWinsWithoutStartingSupersededQueuedCandidates()
        {
            var runtime = Create(); yield return Done(runtime.InitializeAsync());
            provider.Delay = "main/ru"; var old = runtime.SwitchLanguageAsync("ru");
            var superseded = runtime.SwitchLanguageAsync("fr"); var latest = runtime.SwitchLanguageAsync("en");
            Assert.That(old.IsCompleted, Is.False); Assert.That(latest.IsCompleted, Is.False);
            provider.Complete(); yield return Done(old, false); yield return Done(superseded, false); yield return Done(latest);
            Assert.That(old.IsCanceled, Is.True); Assert.That(superseded.IsCanceled, Is.True);
            Assert.That(provider.Loads, Does.Not.Contain("main/fr")); Assert.That(Text(runtime), Is.EqualTo("main/en"));
        }
        [UnityTest] public IEnumerator CancelledPreparationPreservesOldStateAndCleansLateResult()
        {
            var runtime = Create(); yield return Done(runtime.InitializeAsync()); var before = runtime.GetChannel(); int owners = provider.Active;
            provider.Delay = "main/ru"; using var cancellation = new CancellationTokenSource();
            var operation = runtime.SwitchLanguageAsync("ru", cancellation.Token); cancellation.Cancel();
            Assert.That(operation.IsCompleted, Is.False); provider.Complete(); yield return Done(operation, false);
            Assert.That(operation.IsCanceled, Is.True); Assert.That(runtime.GetChannel(), Is.SameAs(before)); Assert.That(provider.Active, Is.EqualTo(owners));
        }
        [UnityTest] public IEnumerator ReleaseWaitsForUncancelablePreparationAndIsIdempotent()
        {
            var runtime = Create(); yield return Done(runtime.InitializeAsync()); provider.Delay = "main/ru";
            var switching = runtime.SwitchLanguageAsync("ru"); var closing = runtime.ReleaseAsync();
            Assert.That(closing.IsCompleted, Is.False); Assert.That(runtime.ReleaseAsync(), Is.SameAs(closing));
            provider.Complete(); yield return Done(switching, false); yield return Done(closing);
            Assert.That(switching.IsCanceled, Is.True); Assert.That(provider.Active, Is.Zero);
            Assert.Throws<ObjectDisposedException>(() => runtime.GetChannel());
        }
        [UnityTest] public IEnumerator TwoRuntimesAndAuthoringChangesAreIsolated()
        {
            var first = Create(true); var second = CreateRuntime();
            yield return Done(first.InitializeAsync()); yield return Done(second.InitializeAsync());
            config.Sets[0].SetSources("ru", new LocalizationSourceReference("probe", "changed", LocalizationSourceKind.TextTable));
            config.Sets[0].RenameEntry(mainEntry.EntryId, "renamed");
            yield return Done(first.SwitchLanguageAsync("ru"));
            Assert.That(Text(first), Is.EqualTo("main/ru")); Assert.That(Text(second), Is.EqualTo("main/en"));
            Assert.That(first.TryGetText(mainEntry, out var value), Is.True); Assert.That(value.Text, Is.EqualTo("main/ru"));
            yield return Done(first.ReleaseAsync()); Assert.That(Text(second), Is.EqualTo("main/en"));
        }
        [UnityTest] public IEnumerator FailedInitializationPublishesNothingAndCanRetry()
        {
            var runtime = Create(true); provider.Failure = "voice/en"; int changes = 0; runtime.Changed += (_, _) => changes++;
            var failed = runtime.InitializeAsync(); yield return Done(failed, false); _ = failed.Exception;
            Assert.That(runtime.IsInitialized, Is.False); Assert.That(changes, Is.Zero); Assert.That(provider.Active, Is.Zero);
            Assert.Throws<InvalidOperationException>(() => runtime.GetChannel());
            provider.Failure = null; yield return Done(runtime.InitializeAsync()); Assert.That(changes, Is.EqualTo(1));
        }
        [UnityTest] public IEnumerator ObserverFailureDoesNotRollBackOrPreventOtherSubscribers()
        {
            var runtime = Create(); yield return Done(runtime.InitializeAsync()); int errors = 0; int observed = 0;
            runtime.ObserverError += _ => errors++;
            runtime.Changed += (_, _) => throw new InvalidOperationException("subscriber probe");
            runtime.Changed += (r, _) => { observed++; Assert.That(Text(r), Is.EqualTo("main/ru")); };
            yield return Done(runtime.SwitchLanguageAsync("ru")); Assert.That(errors, Is.EqualTo(1)); Assert.That(observed, Is.EqualTo(1));
        }
        [UnityTest] public IEnumerator RetainedOldStateSurvivesSwitchAndRuntimeRelease()
        {
            var runtime = Create(); yield return Done(runtime.InitializeAsync()); var owner = runtime.GetChannel().Retain(); retained.Add(owner);
            yield return Done(runtime.SwitchLanguageAsync("ru")); yield return Done(runtime.ReleaseAsync());
            Assert.That(provider.Active, Is.EqualTo(1)); Assert.That(owner.Snapshot.TryGetText("key", out var value), Is.True); Assert.That(value.Text, Is.EqualTo("main/en"));
            var before = owner.Snapshot; yield return Done(owner.ReleaseAsync());
            Assert.Throws<ObjectDisposedException>(() => before.TryGetText("key", out _)); Assert.That(provider.Active, Is.Zero);
        }
        [UnityTest] public IEnumerator FallbackResultKeepsItsLanguageAndReadyReadsNeverLoad()
        {
            var runtime = Create(); yield return Done(runtime.InitializeAsync("ru")); int loads = provider.Loads.Count;
            for (int i = 0; i < 100; i++)
            {
                Assert.That(runtime.GetChannel().TryGetText("fallback", out var fallback), Is.True);
                Assert.That(fallback.Language, Is.EqualTo("en")); Assert.That(fallback.RequestedLanguage, Is.EqualTo("ru"));
                Assert.That(runtime.TryGetText(mainEntry, out var primary), Is.True); Assert.That(primary.Language, Is.EqualTo("ru"));
            }
            Assert.That(provider.Loads.Count, Is.EqualTo(loads));
        }
        private string AddOptionalSet()
        {
            var set = config.AddSet("Optional"); set.AddEntry("key"); AddSources(set, "optional"); config.MainChannel.IncludeSet(set.Id); return set.Id;
        }
        [UnityTest] public IEnumerator OptionalSetsLoadOnAcquireAndLastReleaseRequiresNoIO()
        {
            Create(); string set = AddOptionalSet(); var runtime = CreateRuntime(); yield return Done(runtime.InitializeAsync());
            Assert.That(provider.Loads.Any(p => p.StartsWith("optional/")), Is.False);
            var first = runtime.AcquireSetAsync(null, set); yield return Done(first); registrations.Add(first.Result);
            int loads = provider.Loads.Count; var second = runtime.AcquireSetAsync(null, set); yield return Done(second); registrations.Add(second.Result);
            Assert.That(Text(runtime, set: set), Is.EqualTo("optional/en")); Assert.That(provider.Loads.Count, Is.EqualTo(loads));
            yield return Done(first.Result.ReleaseAsync()); Assert.That(runtime.GetChannel().ActiveSetIds, Does.Contain(set));
            yield return Done(second.Result.ReleaseAsync()); Assert.That(runtime.GetChannel().ActiveSetIds, Does.Not.Contain(set)); Assert.That(provider.Loads.Count, Is.EqualTo(loads));
        }
        [UnityTest] public IEnumerator SetChangesSerializeWithLanguagePreparation()
        {
            Create(); string set = AddOptionalSet(); var runtime = CreateRuntime(); yield return Done(runtime.InitializeAsync());
            provider.Delay = "main/ru"; var switching = runtime.SwitchLanguageAsync("ru"); var acquiring = runtime.AcquireSetAsync(null, set);
            Assert.That(acquiring.IsCompleted, Is.False); provider.Complete(); yield return Done(switching); yield return Done(acquiring); registrations.Add(acquiring.Result);
            Assert.That(Text(runtime, set: set), Is.EqualTo("optional/ru"));
            provider.Delay = "main/fr"; var next = runtime.SwitchLanguageAsync("fr"); var closing = acquiring.Result.ReleaseAsync();
            Assert.That(closing.IsCompleted, Is.False); provider.Complete(); yield return Done(next); yield return Done(closing);
            Assert.That(runtime.GetChannel().ActiveSetIds, Does.Not.Contain(set)); Assert.That(Text(runtime), Is.EqualTo("main/fr"));
        }
        [UnityTest] public IEnumerator FailedOptionalAcquireDoesNotRegisterOrDamageExistingState()
        {
            Create(); string set = AddOptionalSet(); var runtime = CreateRuntime(); yield return Done(runtime.InitializeAsync());
            var before = runtime.GetChannel(); int owners = provider.Active; provider.Failure = "optional/en";
            var failed = runtime.AcquireSetAsync(null, set); yield return Done(failed, false); _ = failed.Exception;
            Assert.That(runtime.GetChannel(), Is.SameAs(before)); Assert.That(provider.Active, Is.EqualTo(owners));
            provider.Failure = null; var retry = runtime.AcquireSetAsync(null, set); yield return Done(retry); registrations.Add(retry.Result);
        }
        [UnityTest] public IEnumerator UnsupportedLanguageDoesNotCancelAnAlreadyValidSwitch()
        {
            var runtime = Create(); yield return Done(runtime.InitializeAsync()); provider.Delay = "main/ru";
            var switching = runtime.SwitchLanguageAsync("ru"); Assert.Throws<ArgumentException>(() => runtime.SwitchLanguageAsync("de"));
            provider.Complete(); yield return Done(switching); Assert.That(Text(runtime), Is.EqualTo("main/ru"));
        }
        [UnityTest] public IEnumerator BudgetsCoverTheEntireSetAcrossItsFallbackSources()
        {
            var runtime = Create(options: new LocalizationContextOptions { MaxTextEntries = 2 }); yield return Done(runtime.InitializeAsync());
            var before = runtime.GetChannel(); var failed = runtime.SwitchLanguageAsync("ru"); yield return Done(failed, false);
            Assert.That(failed.Exception.ToString(), Does.Contain("limits")); Assert.That(runtime.GetChannel(), Is.SameAs(before));
        }
        [UnityTest] public IEnumerator ReloadUpdatesContentButKeepsDefinitionsFrozen()
        {
            var runtime = Create(); yield return Done(runtime.InitializeAsync()); provider.Suffix = " updated";
            config.Sets[0].SetSources("en", new LocalizationSourceReference("probe", "unpublished", LocalizationSourceKind.TextTable));
            yield return Done(runtime.ReloadAsync()); Assert.That(Text(runtime), Is.EqualTo("main/en updated"));
        }

        [UnityTest] public IEnumerator IndependentChannelCanFinishWhileMainProviderIsStillPending()
        {
            var runtime = Create(true); yield return Done(runtime.InitializeAsync());
            provider.Delay = "voice/ru"; var main = runtime.SwitchLanguageAsync("ru");
            var other = runtime.SwitchLanguageAsync(independent, "ru"); yield return Done(other);
            Assert.That(main.IsCompleted, Is.False); Assert.That(Text(runtime), Is.EqualTo("main/en"));
            Assert.That(Text(runtime, independent), Is.EqualTo("main/ru")); provider.Complete(); yield return Done(main);
        }
        [UnityTest] public IEnumerator CancellationDuringInitializationDrainsAndAllowsRetry()
        {
            var runtime = Create(); provider.Delay = "main/en"; using var cancellation = new CancellationTokenSource();
            var initial = runtime.InitializeAsync(cancellationToken: cancellation.Token); cancellation.Cancel(); provider.Complete();
            yield return Done(initial, false); Assert.That(initial.IsCanceled, Is.True); Assert.That(runtime.IsInitialized, Is.False); Assert.That(provider.Active, Is.Zero);
            yield return Done(runtime.InitializeAsync());
        }
        [UnityTest] public IEnumerator QueuedAcquireCancellationDoesNotLeaveASetRegistered()
        {
            Create(); string set = AddOptionalSet(); var runtime = CreateRuntime(); yield return Done(runtime.InitializeAsync());
            provider.Delay = "main/ru"; var switching = runtime.SwitchLanguageAsync("ru");
            using var cancellation = new CancellationTokenSource(); var acquire = runtime.AcquireSetAsync(null, set, cancellation.Token);
            cancellation.Cancel(); yield return Done(acquire, false); Assert.That(acquire.IsCanceled, Is.True);
            provider.Complete(); yield return Done(switching);
            Assert.That(runtime.GetChannel().ActiveSetIds, Does.Not.Contain(set)); Assert.That(provider.Loads.Any(s => s.StartsWith("optional/")), Is.False);
        }
        [UnityTest] public IEnumerator EmptyChannelDoesNotLoadContentUntilASetIsAcquired()
        {
            Create(); config.MainChannel.SetStartup(mainSet, false); var runtime = CreateRuntime();
            yield return Done(runtime.InitializeAsync()); yield return Done(runtime.SwitchLanguageAsync("ru")); Assert.That(provider.Loads, Is.Empty);
            var acquire = runtime.AcquireSetAsync(null, mainSet); yield return Done(acquire); registrations.Add(acquire.Result);
            Assert.That(Text(runtime), Is.EqualTo("main/ru")); yield return Done(acquire.Result.ReleaseAsync()); Assert.That(provider.Active, Is.Zero);
        }
        [UnityTest] public IEnumerator PostCommitCleanupFailureIsReportedWithoutMisreportingSwitchFailure()
        {
            var runtime = Create(); yield return Done(runtime.InitializeAsync()); int errors = 0;
            runtime.ObserverError += e => { Assert.That(e.ToString(), Does.Contain("cleanup probe")); errors++; };
            provider.FailNextCleanup = true; yield return Done(runtime.SwitchLanguageAsync("ru"));
            Assert.That(Text(runtime), Is.EqualTo("main/ru")); Assert.That(errors, Is.EqualTo(1));
        }
        [UnityTest] public IEnumerator ChannelWithNoSetBindingsHasAnEmptyReadableState()
        {
            Create(); var empty = config.AddChannel("Empty", new[] { "en" }); var runtime = CreateRuntime();
            yield return Done(runtime.InitializeAsync());
            Assert.That(runtime.GetChannel(empty.Id).TryGetText("key", out _), Is.False);
            Assert.That(runtime.GetChannel(empty.Id).TryGetAsset("key", out _), Is.False);
        }
        [UnityTest] public IEnumerator PreparationAndCleanupErrorsBothRemainObservable()
        {
            var runtime = Create(true); yield return Done(runtime.InitializeAsync()); var before = runtime.GetChannel();
            provider.Failure = "voice/ru"; provider.FailNextCleanup = true;
            var failed = runtime.SwitchLanguageAsync("ru"); yield return Done(failed, false);
            Assert.That(failed.Exception.ToString(), Does.Contain("injected source failure").And.Contain("cleanup probe"));
            Assert.That(runtime.GetChannel(), Is.SameAs(before));
        }
        [UnityTest] public IEnumerator SetReleasePreservesItsCleanupFailureForTheCaller()
        {
            Create(); string set = AddOptionalSet(); var runtime = CreateRuntime(); yield return Done(runtime.InitializeAsync());
            var acquire = runtime.AcquireSetAsync(null, set); yield return Done(acquire);
            provider.FailNextCleanup = true; var release = acquire.Result.ReleaseAsync(); yield return Done(release, false);
            Assert.That(release.Exception.ToString(), Does.Contain("cleanup probe")); Assert.That(acquire.Result.ReleaseAsync(), Is.SameAs(release));
            Assert.That(runtime.GetChannel().ActiveSetIds, Does.Not.Contain(set));
        }

        private sealed class Probe : ILocalizationSourceProvider
        {
            internal readonly List<string> Loads = new();
            internal int Active;
            internal string Failure, Delay, Suffix = "";
            internal bool FailNextCleanup;
            private TaskCompletionSource<LocalizationSourceLease> pending;
            private LocalizationSourceRequest delayed;
            public LocalizationProviderCapabilities GetCapabilities(LocalizationSourceKind kind) => new(LocalizationLoadUnit.Table, false, false);
            public Task<LocalizationSourceLease> LoadAsync(LocalizationSourceRequest request, CancellationToken token)
            {
                Loads.Add(request.Source.Address);
                if (request.Source.Address == Failure) return Task.FromException<LocalizationSourceLease>(new InvalidOperationException("injected source failure"));
                if (request.Source.Address == Delay) { delayed = request; pending = new(); return pending.Task; }
                return Task.FromResult(Data(request));
            }
            private LocalizationSourceLease Data(LocalizationSourceRequest request)
            {
                var entries = new Dictionary<string, string> { ["key"] = request.Source.Address + Suffix };
                if (request.Language == "en") entries.Add("fallback", "English fallback");
                Active++; return new(LocalizationSourceData.FromText(entries), () =>
                {
                    Active--;
                    if (FailNextCleanup) { FailNextCleanup = false; return Task.FromException(new InvalidOperationException("cleanup probe")); }
                    return Task.CompletedTask;
                });
            }
            internal void Complete() { var completion = pending; pending = null; Delay = null; completion.SetResult(Data(delayed)); }
        }
    }
}
