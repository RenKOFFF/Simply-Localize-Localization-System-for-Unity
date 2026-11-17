using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace SimplyLocalize.Editor.Data
{
    /// <summary>Bridges a committed file/config transaction to Unity Undo. Only the lightweight proxy is
    /// recorded by Unity; the serialized transaction retains its current state across assembly reloads.</summary>
    [InitializeOnLoad]
    public sealed class LocalizationProjectUndo : ScriptableObject, IDisposable
    {
        [SerializeField] private LocalizationProjectChange change;
        [SerializeField] private LocalizationProjectUndoProxy proxy;
        [SerializeField] private long order;
        [SerializeField] private bool linkedSceneUndo;
        private static bool applying;
        private static long historyRevision;
        public static event Action Changed;
        static LocalizationProjectUndo()
        {
            Undo.undoRedoPerformed += ApplyHistory;
            Undo.postprocessModifications += modifications =>
            { if (!applying && modifications.Length > 0) historyRevision++; return modifications; };
            EditorApplication.quitting += ClearOnQuit;
        }
        /// <summary>Returns null for a no-op. Dispose explicitly discards this operation's Undo record only.</summary>
        public static LocalizationProjectUndo Record(LocalizationProjectChange change, string label)
            => Record(change, label, false);
        internal static LocalizationProjectUndo Record(LocalizationProjectChange change, string label, bool linkedSceneUndo)
        {
            if (change == null || !change.IsApplied) throw new ArgumentException("Record an applied project change.", nameof(change));
            if (string.IsNullOrWhiteSpace(label)) throw new ArgumentException("Give the operation a readable Undo label.", nameof(label));
            if (!change.HasChanges) return null;
            historyRevision++;
            if (Resources.FindObjectsOfTypeAll<LocalizationProjectUndo>().Any(r => ReferenceEquals(r.change, change)))
                throw new InvalidOperationException("This change is already registered in Unity Undo.");
            var record = CreateInstance<LocalizationProjectUndo>(); record.hideFlags = HideFlags.HideAndDontSave; record.name = label;
            record.change = change;
            record.linkedSceneUndo = linkedSceneUndo;
            record.order = Resources.FindObjectsOfTypeAll<LocalizationProjectUndo>().Select(r => r.order).DefaultIfEmpty(0).Max() + 1;
            record.proxy = CreateInstance<LocalizationProjectUndoProxy>(); record.proxy.hideFlags = HideFlags.HideAndDontSave;
            record.proxy.name = label; record.proxy.Record = record; record.proxy.Applied = false;
            Undo.IncrementCurrentGroup(); Undo.SetCurrentGroupName(label);
            Undo.RegisterCompleteObjectUndo(record.proxy, label); record.proxy.Applied = true;
            EditorUtility.SetDirty(record.proxy); Undo.IncrementCurrentGroup();
            return record;
        }
        private static void ApplyHistory()
        {
            historyRevision++;
            if (applying) return;
            var pending = Resources.FindObjectsOfTypeAll<LocalizationProjectUndo>()
                .Where(r => r.proxy != null && r.change != null && r.proxy.Applied != r.change.IsApplied)
                .OrderBy(r => r.proxy.Applied ? 1 : 0).ThenBy(r => r.proxy.Applied ? r.order : -r.order).ToArray();
            if (pending.Length == 0) return;
            var previous = pending.Select(r => r.change.IsApplied).ToArray();
            applying = true;
            try
            {
                foreach (var record in pending)
                {
                    if (record.proxy.Applied) record.change.Redo(); else record.change.Undo();
                }
            }
            catch (Exception error)
            {
                Exception reported = error;
                // A file changed outside this operation. Never force a stale history entry over it.
                if (pending.Any(r => r.linkedSceneUndo))
                {
                    // A caller may have collapsed multiple operations into one native step.
                    // Restore file changes already processed before reversing that scene step.
                    try
                    {
                        for (int i = pending.Length - 1; i >= 0; i--)
                            if (pending[i].change.IsApplied != previous[i])
                            { if (previous[i]) pending[i].change.Redo(); else pending[i].change.Undo(); }
                        RestoreLinkedHistory(pending);
                    }
                    catch (Exception restoreError) { reported = new AggregateException("Could not restore every file in the grouped operation. Resolve the file conflicts before editing again.", error, restoreError); }
                }
                else foreach (var record in pending)
                {
                    record.proxy.Applied = record.change.IsApplied;
                    Undo.ClearUndo(record.proxy);
                }
                Debug.LogError("[SimplyLocalize Editor] Undo/Redo stopped: " + reported.Message + " Resolve the conflict before retrying.");
            }
            finally { applying = false; }
            if (Changed != null)
                foreach (Action observer in Changed.GetInvocationList())
                    try { observer(); } catch (Exception error) { Debug.LogException(error); }
        }
        private static void RestoreLinkedHistory(LocalizationProjectUndo[] records)
        {
            // Unity invokes callbacks after restoring scene objects. Reverse that native step on
            // the next editor update so a rejected file edit cannot leave dangling scene bindings.
            bool requested = records[0].proxy.Applied; long revision = historyRevision;
            string groupName = Undo.GetCurrentGroupName();
            EditorApplication.delayCall += () =>
            {
                if (records.Any(r => r == null || r.proxy == null) || records.All(r => r.proxy.Applied == r.change.IsApplied)) return;
                // Group numbers advance during ordinary editor updates even without new edits.
                // Check history events, recorded property edits and the operation label instead.
                if (historyRevision != revision || Undo.GetCurrentGroupName() != groupName)
                {
                    Debug.LogError("[SimplyLocalize Editor] Undo history changed before the scene could be restored. Resolve the file conflict, then reverse the last Auto-Localize history step.");
                    return;
                }
                applying = true;
                try { if (requested) Undo.PerformUndo(); else Undo.PerformRedo(); }
                finally { applying = false; }
                if (records.Any(r => r.proxy.Applied != r.change.IsApplied))
                    Debug.LogError("[SimplyLocalize Editor] Could not restore the Auto-Localize scene history.");
            };
        }
        public void Dispose()
        {
            if (this == null) return;
            if (proxy != null) { Undo.ClearUndo(proxy); DestroyImmediate(proxy); }
            DestroyImmediate(this);
        }
        private static void ClearOnQuit()
        { foreach (var record in Resources.FindObjectsOfTypeAll<LocalizationProjectUndo>()) record.Dispose(); }
    }
}
