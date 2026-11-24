using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace SimplyLocalize
{
    public enum LocalizationResourceStage { Loading, PreparingAudio, Ready, Releasing, WaitingForAudio, Released }
    public enum LocalizationResourceEventKind { Preparing, Ready, ReleaseRequested, Unloaded, BorrowedReleased, Retained, Released, Failed, Cancelled }

    /// <summary>Immutable values only. No snapshot or history entry retains a Unity object.</summary>
    public sealed class LocalizationResourceOwnerInfo
    {
        public long ContextId { get; }
        public long SetId { get; }
        public string ContextName { get; }
        public string RequestedLanguage { get; }
        public string SourceLanguage { get; }
        public string Address { get; }
        public LocalizationResourceStage Stage { get; }
        public LocalizationResourceOwnership Ownership { get; }
        internal LocalizationResourceOwnerInfo(LocalizationResourceLease lease, string address, string sourceLanguage)
        {
            ContextId = lease.ContextId; SetId = lease.SetId; ContextName = lease.ContextName;
            RequestedLanguage = lease.Language; SourceLanguage = sourceLanguage; Address = address;
            Stage = lease.Stage; Ownership = lease.Ownership;
        }
    }
    public sealed class LocalizationResourceInfo
    {
        public int InstanceId { get; }
        public string Name { get; }
        public string Type { get; }
        public int ReferenceCount { get; }
        public bool UnloadWhenUnused { get; }
        public ReadOnlyCollection<LocalizationResourceOwnerInfo> Owners { get; }
        internal LocalizationResourceInfo(int id, string name, string type, int count, bool unload, IEnumerable<LocalizationResourceOwnerInfo> owners)
        { InstanceId = id; Name = name; Type = type; ReferenceCount = count; UnloadWhenUnused = unload; Owners = Array.AsReadOnly(owners.ToArray()); }
    }
    public sealed class LocalizationResourceSetInfo
    {
        public LocalizationResourceOwnerInfo Owner { get; }
        public ReadOnlyCollection<string> PendingAddresses { get; }
        public int ResourceCount { get; }
        public double PreparationMilliseconds { get; }
        public double ReleaseMilliseconds { get; }
        internal LocalizationResourceSetInfo(LocalizationResourceLease lease, int count, IEnumerable<string> pending)
        { Owner = new LocalizationResourceOwnerInfo(lease, null, null); ResourceCount = count; PendingAddresses = Array.AsReadOnly(pending.ToArray()); PreparationMilliseconds = lease.PreparationMilliseconds; ReleaseMilliseconds = lease.ReleaseMilliseconds; }
    }
    public sealed class LocalizationResourceEvent
    {
        public long Sequence { get; }
        public DateTime Utc { get; }
        public long ContextId { get; }
        public string ContextName { get; }
        public string Language { get; }
        public long SetId { get; }
        public int InstanceId { get; }
        public string ResourceName { get; }
        public LocalizationResourceEventKind Kind { get; }
        public string Detail { get; }
        internal LocalizationResourceEvent(long sequence, LocalizationResourceLease lease, LocalizationResourceEventKind kind, int id, string name, string detail)
        { Sequence = sequence; Utc = DateTime.UtcNow; ContextId = lease.ContextId; ContextName = lease.ContextName; Language = lease.Language; SetId = lease.SetId; InstanceId = id; ResourceName = name; Kind = kind; Detail = detail; }
    }
    public sealed class LocalizationResourceSnapshot
    {
        public int Session { get; }
        public bool RecordingEnabled { get; }
        public int DroppedEvents { get; }
        public ReadOnlyCollection<LocalizationResourceSetInfo> Sets { get; }
        public ReadOnlyCollection<LocalizationResourceInfo> Resources { get; }
        public ReadOnlyCollection<LocalizationResourceEvent> History { get; }
        internal LocalizationResourceSnapshot(IEnumerable<LocalizationResourceSetInfo> sets, IEnumerable<LocalizationResourceInfo> resources, IEnumerable<LocalizationResourceEvent> history, int dropped)
        { Session = LocalizationResourceLease.Generation; RecordingEnabled = LocalizationResourceDiagnostics.RecordingEnabled; DroppedEvents = dropped; Sets = Array.AsReadOnly(sets.ToArray()); Resources = Array.AsReadOnly(resources.ToArray()); History = Array.AsReadOnly(history.ToArray()); }
    }

    /// <summary>Unity-thread-only diagnostics for tracked Resources source leases, not all Unity resources.
    /// Live sets are weakly registered once; snapshots allocate only on request. Recording is opt-in and bounded.
    /// Released/BorrowedReleased do not mean process RAM decreased. Unloaded records a completed Resources.UnloadAsset call.</summary>
    public static class LocalizationResourceDiagnostics
    {
        public const int HistoryCapacity = 256;
        public static bool RecordingEnabled { get; set; }
        private static readonly List<WeakReference<LocalizationResourceLease>> leases = new();
        private static readonly Queue<LocalizationResourceEvent> history = new();
        private static long sequence, nextId;
        private static int dropped;
        internal static long NextId() => ++nextId;
        internal static void Register(LocalizationResourceLease lease)
        {
            leases.RemoveAll(w => !w.TryGetTarget(out var target) || target.Stage == LocalizationResourceStage.Released);
            leases.Add(new WeakReference<LocalizationResourceLease>(lease));
        }
        internal static void Forget(LocalizationResourceLease lease) => leases.RemoveAll(w => !w.TryGetTarget(out var target) || ReferenceEquals(target, lease));
        internal static void ResetSession() { leases.Clear(); ClearHistory(); }
        public static void ClearHistory() { history.Clear(); dropped = 0; }
        internal static void Record(LocalizationResourceLease lease, LocalizationResourceEventKind kind, int id = 0, string name = null, string detail = null)
        {
            if (!RecordingEnabled || !lease.IsCurrentSession) return;
            if (history.Count == HistoryCapacity) { history.Dequeue(); dropped++; }
            history.Enqueue(new LocalizationResourceEvent(++sequence, lease, kind, id, name, detail));
        }
        public static LocalizationResourceSnapshot Capture()
        {
            var live = new List<LocalizationResourceLease>();
            leases.RemoveAll(w => !w.TryGetTarget(out var target) || !target.IsCurrentSession || target.Stage == LocalizationResourceStage.Released);
            foreach (var weak in leases) if (weak.TryGetTarget(out var lease)) live.Add(lease);
            return LocalizationResourceLease.CaptureDiagnostics(live, history, dropped);
        }
    }
}
