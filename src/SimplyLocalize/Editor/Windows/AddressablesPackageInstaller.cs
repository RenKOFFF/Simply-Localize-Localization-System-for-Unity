using System;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;
using UnityEngine.UIElements;

namespace SimplyLocalize.Editor.Windows
{
    // UPM requests are serializable. Keep the active request across window closure and assembly reload.
    internal sealed class AddressablesPackageInstaller : ScriptableSingleton<AddressablesPackageInstaller>
    {
        private const string PackageName = "com.unity.addressables";
        [SerializeField] private bool installing;
        [SerializeField] private AddRequest request;
        [SerializeField] private string failure;
        private event Action Changed;

        [InitializeOnLoadMethod]
        private static void Resume()
        {
            if (instance.installing) EditorApplication.update += instance.Poll;
        }

        internal static void AddControls(VisualElement parent)
        {
            var installer = instance;
            bool upgrade = UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/" + PackageName + "/package.json") != null;
            var button = new Button(installer.StartInstall) { name = "installAddressables" };
            var error = new HelpBox("", HelpBoxMessageType.Error);
            void Refresh()
            {
                button.text = installer.installing ? "Installing Addressables…" : upgrade ? "Update Addressables" : "Install Addressables";
                button.SetEnabled(!installer.installing);
                error.text = installer.failure ?? "";
                error.style.display = string.IsNullOrEmpty(installer.failure) ? DisplayStyle.None : DisplayStyle.Flex;
            }
            parent.Add(button);
            parent.Add(error);
            parent.RegisterCallback<AttachToPanelEvent>(_ => { installer.Changed += Refresh; Refresh(); });
            parent.RegisterCallback<DetachFromPanelEvent>(_ => installer.Changed -= Refresh);
            Refresh();
        }

        private void StartInstall()
        {
            if (installing) return;
            failure = null;
            try
            {
                request = Client.Add(PackageName);
                installing = true;
                EditorApplication.update += Poll;
            }
            catch (Exception error) { failure = error.Message; }
            Changed?.Invoke();
        }

        private void Poll()
        {
            if (request != null && !request.IsCompleted) return;
            EditorApplication.update -= Poll;
            failure = request == null ? "The package request was interrupted. Try again."
                : request.Status == StatusCode.Success ? null : request.Error?.message ?? "Package installation failed. Try again.";
            request = null;
            installing = false;
            Changed?.Invoke();
        }
    }
}
