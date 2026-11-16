using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SimplyLocalize
{
    /// <summary>Copied lookup containers. Unity objects remain valid only while their source lease is retained.</summary>
    public sealed class LocalizationSourceData
    {
        public LocalizationSourceKind Kind { get; }
        public IReadOnlyDictionary<string, string> Text { get; }
        public IReadOnlyDictionary<string, Object> Assets { get; }
        public LocalizationAppearance Appearance { get; }
        /// <summary>Entries loaded before selecting requested keys; not a byte/RAM measurement.</summary>
        public int SourceEntryCount { get; }
        private LocalizationSourceData(LocalizationSourceKind kind, IDictionary<string, string> text,
            IDictionary<string, Object> assets, LocalizationAppearance appearance, int sourceEntryCount)
        {
            Kind = kind;
            Text = new ReadOnlyDictionary<string, string>(text == null ? new Dictionary<string, string>() : new Dictionary<string, string>(text, StringComparer.Ordinal));
            Assets = new ReadOnlyDictionary<string, Object>(assets == null ? new Dictionary<string, Object>() : new Dictionary<string, Object>(assets, StringComparer.Ordinal));
            Appearance = appearance;
            int returned = Text.Count + Assets.Count + (appearance != null ? 1 : 0);
            SourceEntryCount = sourceEntryCount < 0 ? returned : sourceEntryCount;
            if (SourceEntryCount < returned) throw new ArgumentOutOfRangeException(nameof(sourceEntryCount));
        }
        public static LocalizationSourceData FromText(IDictionary<string, string> entries, int sourceEntryCount = -1) =>
            new(LocalizationSourceKind.TextTable, entries ?? throw new ArgumentNullException(nameof(entries)), null, null, sourceEntryCount);
        public static LocalizationSourceData FromAssets(IDictionary<string, Object> entries, int sourceEntryCount = -1) =>
            new(LocalizationSourceKind.AssetTable, null, entries ?? throw new ArgumentNullException(nameof(entries)), null, sourceEntryCount);
        public static LocalizationSourceData FromAppearance(LocalizationAppearance appearance) =>
            new(LocalizationSourceKind.Appearance, null, null, appearance != null ? appearance : throw new ArgumentNullException(nameof(appearance)), 1);
    }

    /// <summary>An explicit owner of loaded data. Release is idempotent; only the last retained owner invokes cleanup.
    /// Remove consumer references before releasing Owned resources. Borrowed cleanup must not destroy external data.</summary>
    public sealed class LocalizationSourceLease : IDisposable
    {
        private sealed class Shared
        {
            internal LocalizationSourceData Data;
            internal Func<Task> Cleanup;
            internal int Owners = 1;
            internal readonly int Session = LocalizationResourceLease.Generation;
        }
        private readonly Shared shared;
        private readonly int thread = Environment.CurrentManagedThreadId;
        private bool released;
        private Task release;
        public bool IsReleased => released;
        public LocalizationSourceData Data { get { EnsureAlive(); return shared.Data; } }
        public LocalizationSourceLease(LocalizationSourceData data, Func<Task> releaseAsync = null)
        { shared = new Shared { Data = data ?? throw new ArgumentNullException(nameof(data)), Cleanup = releaseAsync ?? (() => Task.CompletedTask) }; }
        private LocalizationSourceLease(Shared source) { shared = source; shared.Owners++; }
        public LocalizationSourceLease Retain()
        {
            EnsureAlive();
            return new LocalizationSourceLease(shared);
        }
        public Task ReleaseAsync()
        {
            EnsureThread();
            if (released) return release;
            released = true;
            if (--shared.Owners != 0) return release = Task.CompletedTask;
            // Detach payload before cleanup; a reentrant release cannot see usable data.
            shared.Data = null;
            var completion = new TaskCompletionSource<bool>();
            release = completion.Task;
            FinishCleanup(shared, completion);
            return release;
        }
        private static async void FinishCleanup(Shared state, TaskCompletionSource<bool> completion)
        {
            try
            {
                var cleanup = state.Cleanup; state.Cleanup = null;
                Task pending = cleanup();
                if (pending == null) throw new InvalidOperationException("Provider cleanup returned a null task.");
                await pending;
                completion.TrySetResult(true);
            }
            catch (Exception error) { completion.TrySetException(error); }
        }
        public void Dispose()
        {
            var task = ReleaseAsync();
            if (!task.IsCompletedSuccessfully) Observe(task);
        }
        private static async void Observe(Task task) { try { await task; } catch (Exception error) { Debug.LogException(error); } }
        private void EnsureThread()
        { if (thread != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Use and release source leases on their owning thread."); }
        private void EnsureAlive()
        { EnsureThread(); if (released || shared.Session != LocalizationResourceLease.Generation) throw new ObjectDisposedException(nameof(LocalizationSourceLease)); }
    }
}
