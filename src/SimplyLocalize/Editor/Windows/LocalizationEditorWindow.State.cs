using UnityEngine;
using UnityEngine.UIElements;

namespace SimplyLocalize.Editor.Windows
{
    public partial class LocalizationEditorWindow
    {
        [SerializeField] private SetupSelectionState setupState;
        [SerializeField] private ProjectToolsState toolsState;
        [SerializeField] private ProjectAutoState autoState;
        private bool AuxiliaryPending => toolsState?.Pending == true || autoState?.Candidates?.Count > 0;
        private void CaptureAuxiliaryState()
        {
            var setup = rootVisualElement.Q<SetupLanguagePicker>(); if (setup != null) setupState = setup.Capture();
            if (toolsView != null) toolsState = toolsView.CaptureState();
            if (autoView != null) autoState = autoView.CaptureState();
        }
        private void RestoreAuxiliaryState()
        {
            rootVisualElement.Q<SetupLanguagePicker>()?.Restore(setupState);
            if (toolsView != null) { toolsView.DraftChanged += UpdateUnsaved; toolsView.RestoreState(toolsState); toolsState = null; }
            if (autoView != null) { autoView.DraftChanged += UpdateUnsaved; autoView.RestoreState(autoState); autoState = null; }
        }
        private void ClearAuxiliaryDrafts() { toolsState = null; autoState = null; }
        private void ShowRecovery(string reason)
        {
            if (entryDraft?.IsPending == true)
            {
                _tabContent.Add(new ProjectDraftRecoveryView(entryDraft, reason, BuildUI, DiscardChanges));
                return;
            }
            var recovery = new ScrollView { name = "auxiliaryRecovery" };
            recovery.Add(new HelpBox(reason + " Your import or scan input is preserved. Restore the original configuration and retry; applying requires a new preview.", HelpBoxMessageType.Warning));
            if (toolsState?.Pending == true)
                recovery.Add(new TextField("Recovered CSV") { name = "recoveredCsv", value = toolsState.Csv, multiline = true, isReadOnly = true });
            if (autoState?.Candidates != null)
                foreach (var item in autoState.Candidates)
                    recovery.Add(new TextField(item.Key) { value = item.Text, multiline = true, isReadOnly = true });
            recovery.Add(new Button(BuildUI) { name = "retryRecoveredDraft", text = "Retry restoring input" });
            recovery.Add(new Button(DiscardChanges) { name = "discardRecoveredDraft", text = "Discard input and reload" });
            _tabContent.Add(recovery);
        }
    }
}
