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
        private static bool applying;
        public static event Action Changed;
        static LocalizationProjectUndo()
        {
            Undo.undoRedoPerformed += ApplyHistory;
            EditorApplication.quitting += ClearOnQuit;
        }
        /// <summary>Returns null for a no-op. Dispose explicitly discards this operation's Undo record only.</summary>
        public static LocalizationProjectUndo Record(LocalizationProjectChange change, string label)
        {
            if (change == null || !change.IsApplied) throw new ArgumentException("Record an applied project change.", nameof(change));
            if (string.IsNullOrWhiteSpace(label)) throw new ArgumentException("Give the operation a readable Undo label.", nameof(label));
            if (!change.HasChanges) return null;
            if (Resources.FindObjectsOfTypeAll<LocalizationProjectUndo>().Any(r => ReferenceEquals(r.change, change)))
                throw new InvalidOperationException("This change is already registered in Unity Undo.");
            var record = CreateInstance<LocalizationProjectUndo>(); record.hideFlags = HideFlags.HideAndDontSave; record.name = label;
            record.change = change;
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
            if (applying) return;
            var pending = Resources.FindObjectsOfTypeAll<LocalizationProjectUndo>()
                .Where(r => r.proxy != null && r.change != null && r.proxy.Applied != r.change.IsApplied)
                .OrderBy(r => r.proxy.Applied ? 1 : 0).ThenBy(r => r.proxy.Applied ? r.order : -r.order).ToArray();
            if (pending.Length == 0) return;
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
                // A file changed outside this operation. Never force a stale history entry over it.
                foreach (var record in pending)
                {
                    record.proxy.Applied = record.change.IsApplied;
                    Undo.ClearUndo(record.proxy);
                }
                Debug.LogError("[SimplyLocalize Editor] Undo/Redo stopped: " + error.Message + " Reload the window before editing again.");
            }
            finally { applying = false; }
            if (Changed != null)
                foreach (Action observer in Changed.GetInvocationList())
                    try { observer(); } catch (Exception error) { Debug.LogException(error); }
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
