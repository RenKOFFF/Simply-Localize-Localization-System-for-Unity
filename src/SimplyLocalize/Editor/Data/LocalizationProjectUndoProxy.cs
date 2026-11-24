using UnityEngine;

namespace SimplyLocalize.Editor.Data
{
    // Separate Unity object: Undo restores the requested direction without restoring stale transaction bytes.
    internal sealed class LocalizationProjectUndoProxy : ScriptableObject
    {
        [SerializeField] internal LocalizationProjectUndo Record;
        [SerializeField] internal bool Applied;
    }
}
