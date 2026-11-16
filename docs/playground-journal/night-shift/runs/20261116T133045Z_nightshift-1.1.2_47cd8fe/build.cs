using UnityEditor;
using UnityEditor.SceneManagement;
var setup = EditorSceneManager.GetSceneManagerSetup();
try {
    var checks = SimplyLocalize.HorrorLab.Editor.HorrorLabChecks.Data();
    EditorSceneManager.OpenScene("Assets/HorrorLab/NightShift.unity");
    checks += "\n" + SimplyLocalize.HorrorLab.Editor.HorrorInteractionChecks.Run();
    checks += "\n" + SimplyLocalize.HorrorLab.Editor.HorrorInteractionChecks.SceneTargets();
    System.IO.File.WriteAllText("TestResults/Stage61/Final-editor-checks.txt", checks);
    EditorSceneManager.OpenScene("Assets/HorrorLab/NightShiftMenu.unity");
    SimplyLocalize.Editor.Data.LocalizationConfigCatalogBuilder.Rebuild();
    SimplyLocalize.HorrorLab.Editor.HorrorBuildIdentity.Write();
    var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
        scenes = new[] {"Assets/HorrorLab/NightShiftMenu.unity", "Assets/HorrorLab/NightShift.unity"},
        locationPathName = "Builds/Stage61-Final/NightShift.exe", target = BuildTarget.StandaloneWindows64,
        options = BuildOptions.None
    });
    System.IO.File.WriteAllText("TestResults/Stage61/Final-build.txt", report.summary.result + "\nErrors: " + report.summary.totalErrors + "\nBytes: " + report.summary.totalSize + "\nDuration: " + report.summary.totalTime);
    if(report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded) throw new System.Exception("Build failed");
    return new { result = report.summary.result.ToString(), errors = report.summary.totalErrors, checks };
} finally { EditorSceneManager.RestoreSceneManagerSetup(setup); }
