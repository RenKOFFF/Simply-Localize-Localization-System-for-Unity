using System;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace SimplyLocalize.HorrorLab.Editor
{
    /// <summary>The demo is authored as checked-in scenes and assets, not regenerated prototype geometry.</summary>
    public static class HorrorLabSetup
    {
        public const string Root = "Assets/HorrorLab";
        public const string ScenePath = Root + "/NightShift.unity";

        [MenuItem("Tools/Simply Localize Lab/Open Night Shift")]
        public static void Open()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode first.");
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(HorrorArchitectureSetup.MenuPath);
        }
    }
}
