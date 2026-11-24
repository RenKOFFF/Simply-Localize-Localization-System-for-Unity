using System;
using System.Linq;
using SimplyLocalize;
using SimplyLocalize.Editor.Windows;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;
using UnityEngine.UIElements;

// Exercise the Settings button in a disposable Editor project, including the UPM-triggered reload.
public static class ChapterAudioInstall
{
    private const string Pending = "ChapterAudioAcceptance.WaitForAddressables";
    private sealed class Host : EditorWindow { }

    public static void Run()
    {
        var config = LocalizationProjectConfig.Create(new[] { "en" });
        var view = new ProjectSettingsView(config);
        var host = ScriptableObject.CreateInstance<Host>();
        try
        {
            host.Show();
            host.rootVisualElement.Add(view);
            var button = view.Q<Button>("installAddressables");
            if (button == null || !button.enabledInHierarchy || Installed() != null)
                throw new InvalidOperationException("Expected an unmodified project with the install button enabled.");
            using var click = NavigationSubmitEvent.GetPooled();
            click.target = button;
            button.SendEvent(click);
            if (button.enabledInHierarchy) throw new InvalidOperationException("UPM install did not start.");
            SessionState.SetBool(Pending, true);
        }
        finally
        {
            host.Close();
            UnityEngine.Object.DestroyImmediate(config);
        }
    }

    [InitializeOnLoadMethod]
    private static void AfterReload()
    {
        if (!SessionState.GetBool(Pending, false)) return;
        EditorApplication.delayCall += () =>
        {
            var installed = Installed();
            if (installed == null) return; // An unrelated recompile can precede UPM registration.
            var config = LocalizationProjectConfig.Create(new[] { "en" });
            try
            {
                var view = new ProjectSettingsView(config);
                if (view.Q<Button>("installAddressables") != null || view.Q<Button>("createAddressableTable_" + config.Sets[0].Id) == null)
                    throw new InvalidOperationException("Integration controls did not replace the installer after reload.");
                Debug.Log("PASS: installed Addressables " + installed.version + " from Simply Localize Settings");
                SessionState.EraseBool(Pending);
                EditorApplication.Exit(0);
            }
            catch (Exception failure) { Debug.LogException(failure); EditorApplication.Exit(1); }
            finally { UnityEngine.Object.DestroyImmediate(config); }
        };
    }

    private static UnityEditor.PackageManager.PackageInfo Installed() =>
        UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages().FirstOrDefault(p => p.name == "com.unity.addressables");
}
