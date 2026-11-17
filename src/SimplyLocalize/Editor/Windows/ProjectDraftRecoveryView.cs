using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace SimplyLocalize.Editor.Windows
{
    /// <summary>Last-resort presentation when an entry/source was removed or became unreadable.
    /// It keeps entered values available to copy; it never guesses how to recreate missing data.</summary>
    internal sealed class ProjectDraftRecoveryView : VisualElement
    {
        internal ProjectDraftRecoveryView(ProjectViewDraft draft, string reason, Action retry, Action discard)
        {
            name = "draftRecovery";
            Add(new HelpBox("Your unsaved draft is preserved. " + reason + "\nRestore the source and retry, or copy the values before discarding.", HelpBoxMessageType.Warning));
            if (draft.Kind != ProjectViewDraft.DraftKind.Appearance)
            {
                Add(new TextField("Key") { name = "recoveredKey", value = draft.Key, isReadOnly = true });
                if (draft.Kind == ProjectViewDraft.DraftKind.Text) Add(new TextField("Translator note") { value = draft.Description, multiline = true, isReadOnly = true });
                foreach (var item in draft.Values)
                {
                    if (draft.Kind == ProjectViewDraft.DraftKind.Text)
                        Add(new TextField(item.Language + (item.Removed ? " · remove direct translation" : "")) { name = "recovered_" + item.Language, value = item.Text, multiline = true, isReadOnly = true });
                    else
                    {
                        var field = new ObjectField(item.Language + (item.Removed ? " · remove direct entry" : "")) { value = item.Asset, allowSceneObjects = false }; field.SetEnabled(false); Add(field);
                    }
                }
            }
            else if (draft.Appearance != null)
            {
                var editor = UnityEditor.Editor.CreateEditor(draft.Appearance);
                Add(new IMGUIContainer(() => { using (new EditorGUI.DisabledScope(true)) if (editor != null) editor.OnInspectorGUI(); }));
                RegisterCallback<DetachFromPanelEvent>(_ => { if (editor != null) UnityEngine.Object.DestroyImmediate(editor); });
            }
            Add(new Button(retry) { name = "retryRecoveredDraft", text = "Retry restoring draft" });
            Add(new Button(discard) { name = "discardRecoveredDraft", text = "Discard draft and reload" });
        }
    }
}
