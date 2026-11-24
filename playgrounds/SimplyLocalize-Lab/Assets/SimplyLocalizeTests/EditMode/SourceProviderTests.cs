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
    public class SourceProviderTests
    {
        private static LocalizationSourceReference Source(LocalizationSourceKind kind = LocalizationSourceKind.TextTable) => new("custom", "data", kind);
        private static LocalizationSourceLease Text(string value, Func<Task> cleanup = null) => new(LocalizationSourceData.FromText(new Dictionary<string, string> { ["key"] = value }), cleanup);
        private static IEnumerator Done(Task task)
        {
            float deadline = Time.realtimeSinceStartup + 10;
            while (!task.IsCompleted && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(task.IsCompleted, Is.True, "Provider operation timed out.");
        }

        [Test] public void RetainedOwnersShareDataAndOnlyLastReleaseCleansUp()
        {
            int releases = 0;
            var first = Text("value", () => { releases++; return Task.CompletedTask; });
            var second = first.Retain();
            Assert.That(first.Data, Is.SameAs(second.Data));
            var firstRelease = first.ReleaseAsync();
            Assert.That(releases, Is.Zero);
            Assert.That(second.Data.Text["key"], Is.EqualTo("value"));
            Assert.That(first.ReleaseAsync(), Is.SameAs(firstRelease));
            Assert.Throws<ObjectDisposedException>(() => first.Retain());
            Assert.Throws<ObjectDisposedException>(() => _ = first.Data);
            Assert.That(second.ReleaseAsync().IsCompletedSuccessfully, Is.True);
            Assert.That(releases, Is.EqualTo(1));
        }

        [UnityTest] public IEnumerator LastReleaseWaitsForProviderCleanupAndPreservesItsFailure()
        {
            var finish = new TaskCompletionSource<bool>();
            var lease = Text("value", () => finish.Task);
            var release = lease.ReleaseAsync();
            Assert.That(release.IsCompleted, Is.False);
            Assert.That(lease.ReleaseAsync(), Is.SameAs(release));
            finish.SetException(new InvalidOperationException("cleanup probe"));
            yield return Done(release);
            Assert.That(release.Exception.ToString(), Does.Contain("cleanup probe"));
            Assert.That(lease.ReleaseAsync(), Is.SameAs(release));
        }

        [Test] public void ReentrantReleaseObservesTheSameTask()
        {
            LocalizationSourceLease lease = null; Task reentrant = null;
            lease = Text("value", () => { reentrant = lease.ReleaseAsync(); return Task.CompletedTask; });
            var release = lease.ReleaseAsync();
            Assert.That(reentrant, Is.SameAs(release));
            Assert.That(release.IsCompletedSuccessfully, Is.True);
        }

        [Test] public void PayloadContainersAreCopiedAndReadOnly()
        {
            var original = new Dictionary<string, string> { ["key"] = "before" };
            var data = LocalizationSourceData.FromText(original); original["key"] = "after";
            Assert.That(data.Text["key"], Is.EqualTo("before"));
            Assert.Throws<NotSupportedException>(() => ((IDictionary<string, string>)data.Text).Clear());
        }

        [UnityTest] public IEnumerator RegistriesAndCopiesDoNotConfuseProvidersWithTheSameAddress()
        {
            var a = new LocalizationSourceRegistry(false); var b = new LocalizationSourceRegistry(false);
            a.Register("custom", new ImmediateProvider("a")); b.Register("custom", new ImmediateProvider("b"));
            var snapshot = a.Copy(); a.Register("custom", new ImmediateProvider("replacement"));
            var first = snapshot.LoadAsync(new(Source(), "en")); var second = b.LoadAsync(new(Source(), "en")); var third = a.LoadAsync(new(Source(), "en"));
            yield return Done(first); yield return Done(second); yield return Done(third);
            using var x = first.GetAwaiter().GetResult(); using var y = second.GetAwaiter().GetResult(); using var z = third.GetAwaiter().GetResult();
            Assert.That(x.Data.Text["key"], Is.EqualTo("a")); Assert.That(y.Data.Text["key"], Is.EqualTo("b")); Assert.That(z.Data.Text["key"], Is.EqualTo("replacement"));
        }

        [UnityTest] public IEnumerator CancellationOfLateResultDoesNotReleaseAnotherRetainedOwner()
        {
            var pending = new DelayedProvider(); var registry = new LocalizationSourceRegistry(false); registry.Register("custom", pending);
            using var cancellation = new CancellationTokenSource();
            var first = registry.LoadAsync(new(Source(), "en"), cancellation.Token);
            var second = registry.LoadAsync(new(Source(), "en"));
            cancellation.Cancel(); Assert.That(first.IsCompleted, Is.False, "A provider ignoring cancellation still owns pending work.");
            int releases = 0; var original = Text("shared", () => { releases++; return Task.CompletedTask; });
            var retained = original.Retain();
            pending.Requests[0].SetResult(original); pending.Requests[1].SetResult(retained);
            yield return Done(first); yield return Done(second);
            Assert.That(first.IsCanceled, Is.True); Assert.That(releases, Is.Zero);
            Assert.That(second.Result.Data.Text["key"], Is.EqualTo("shared"));
            yield return Done(second.Result.ReleaseAsync()); Assert.That(releases, Is.EqualTo(1));
        }

        [UnityTest] public IEnumerator WrongKindAndOverBudgetResultsAreReleasedBeforeFailureReturns()
        {
            foreach (bool wrongKind in new[] { true, false })
            {
                int releases = 0;
                var data = wrongKind ? LocalizationSourceData.FromAssets(new Dictionary<string, UnityEngine.Object>())
                    : LocalizationSourceData.FromText(new Dictionary<string, string> { ["key"] = "value" }, sourceEntryCount: 20);
                var registry = new LocalizationSourceRegistry(false);
                registry.Register("custom", new ResultProvider(() => new(data, () => { releases++; return Task.CompletedTask; })));
                var operation = registry.LoadAsync(new(Source(), "en", options: new LocalizationRuntimeOptions { MaxTextEntries = 1 }));
                yield return Done(operation);
                Assert.That(operation.IsFaulted, Is.True); _ = operation.Exception;
                Assert.That(releases, Is.EqualTo(1));
            }
        }

        [UnityTest] public IEnumerator FailedRequestCanBeRetriedAndPrecancelDoesNotStartProvider()
        {
            var pending = new DelayedProvider(); var registry = new LocalizationSourceRegistry(false); registry.Register("custom", pending);
            var failed = registry.LoadAsync(new(Source(), "en")); pending.Requests[0].SetException(new InvalidOperationException("load probe"));
            yield return Done(failed); Assert.That(failed.Exception.ToString(), Does.Contain("load probe"));
            var retry = registry.LoadAsync(new(Source(), "en")); pending.Requests[1].SetResult(Text("retry"));
            yield return Done(retry); using var value = retry.Result; Assert.That(value.Data.Text["key"], Is.EqualTo("retry"));
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            var cancelled = registry.LoadAsync(new(Source(), "en"), cancellation.Token); yield return Done(cancelled);
            Assert.That(cancelled.IsCanceled, Is.True); Assert.That(pending.Requests.Count, Is.EqualTo(2));
        }

        [UnityTest] public IEnumerator OptionalMetadataAndSingleAssetLoadingNeedNoPackageFork()
        {
            var provider = new LazyAssets(); var registry = new LocalizationSourceRegistry(false); registry.Register("custom", provider);
            var source = Source(LocalizationSourceKind.AssetTable);
            Assert.That(registry.HasSeparateMetadata(source), Is.True);
            var metadata = registry.ReadMetadataAsync(source, "en"); yield return Done(metadata);
            Assert.That(metadata.Result.Keys, Is.EquivalentTo(new[] { "one", "two" }));
            Assert.That(provider.Created, Is.Zero, "Reading the catalog must not create/load localized content.");
            Assert.That(registry.GetCapabilities(source).LoadUnit, Is.EqualTo(LocalizationLoadUnit.Asset));
            var load = registry.LoadAsync(new(source, "en", new[] { "two" })); yield return Done(load);
            Assert.That(provider.Created, Is.EqualTo(1)); Assert.That(load.Result.Data.Assets.Keys, Is.EquivalentTo(new[] { "two" }));
            yield return Done(load.Result.ReleaseAsync()); Assert.That(provider.Released, Is.EqualTo(1));
        }

        [Test] public void ResourcesDeclaresTableDependenciesAndDoesNotPretendToHaveSeparateMetadata()
        {
            var registry = new LocalizationSourceRegistry();
            var source = new LocalizationSourceReference("resources", "any/table", LocalizationSourceKind.AssetTable);
            var capabilities = registry.GetCapabilities(source);
            Assert.That(capabilities.LoadUnit, Is.EqualTo(LocalizationLoadUnit.Table));
            Assert.That(capabilities.LoadsDirectDependencies, Is.True); Assert.That(capabilities.CanCancelUnderlyingLoad, Is.False);
            Assert.That(registry.HasSeparateMetadata(source), Is.False);
            var metadata = registry.ReadMetadataAsync(source, "en"); Assert.That(metadata.IsFaulted, Is.True);
            Assert.That(metadata.Exception.InnerException, Is.TypeOf<NotSupportedException>());
        }

        [Test] public void RequestFreezesSelectionAndLimits()
        {
            var keys = new[] { "one" }; var options = new LocalizationRuntimeOptions { MaxAssetEntries = 2 };
            var request = new LocalizationSourceRequest(Source(), "RU", keys, options);
            keys[0] = "two"; options.MaxAssetEntries = 99;
            Assert.That(request.RequestedKeys.Single(), Is.EqualTo("one")); Assert.That(request.MaxAssetEntries, Is.EqualTo(2));
            Assert.That(request.Language, Is.EqualTo("ru"));
        }

        private sealed class ImmediateProvider : ILocalizationSourceProvider
        {
            private readonly string value; internal ImmediateProvider(string text) => value = text;
            public LocalizationProviderCapabilities GetCapabilities(LocalizationSourceKind kind) => new(LocalizationLoadUnit.Table, false, true);
            public Task<LocalizationSourceLease> LoadAsync(LocalizationSourceRequest request, CancellationToken token) => Task.FromResult(Text(value));
        }
        private sealed class DelayedProvider : ILocalizationSourceProvider
        {
            internal readonly List<TaskCompletionSource<LocalizationSourceLease>> Requests = new();
            public LocalizationProviderCapabilities GetCapabilities(LocalizationSourceKind kind) => new(LocalizationLoadUnit.Table, false, false);
            public Task<LocalizationSourceLease> LoadAsync(LocalizationSourceRequest request, CancellationToken token)
            { var pending = new TaskCompletionSource<LocalizationSourceLease>(); Requests.Add(pending); return pending.Task; }
        }
        private sealed class ResultProvider : ILocalizationSourceProvider
        {
            private readonly Func<LocalizationSourceLease> result; internal ResultProvider(Func<LocalizationSourceLease> result) => this.result = result;
            public LocalizationProviderCapabilities GetCapabilities(LocalizationSourceKind kind) => new(LocalizationLoadUnit.Table, false, true);
            public Task<LocalizationSourceLease> LoadAsync(LocalizationSourceRequest request, CancellationToken token) => Task.FromResult(result());
        }
        private sealed class LazyAssets : ILocalizationSourceProvider, ILocalizationSourceMetadataProvider
        {
            internal int Created, Released;
            public LocalizationProviderCapabilities GetCapabilities(LocalizationSourceKind kind) => new(LocalizationLoadUnit.Asset, false, true);
            public Task<LocalizationSourceMetadata> ReadMetadataAsync(LocalizationSourceReference source, string language, CancellationToken token) => Task.FromResult(new LocalizationSourceMetadata(new[] { "one", "two" }));
            public Task<LocalizationSourceLease> LoadAsync(LocalizationSourceRequest request, CancellationToken token)
            {
                var assets = new Dictionary<string, UnityEngine.Object>();
                foreach (string key in request.RequestedKeys) { assets.Add(key, new Texture2D(1, 1)); Created++; }
                return Task.FromResult(new LocalizationSourceLease(LocalizationSourceData.FromAssets(assets), () =>
                { foreach (var value in assets.Values) { UnityEngine.Object.DestroyImmediate(value); Released++; } return Task.CompletedTask; }));
            }
        }
    }
}
