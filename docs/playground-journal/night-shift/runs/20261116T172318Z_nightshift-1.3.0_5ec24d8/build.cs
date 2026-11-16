{
    string receipt = "TestResults/Stage92Scope/build.json";
    try {
        SimplyLocalize.HorrorLab.Editor.HorrorBuildIdentity.Write();
        var report = UnityEditor.BuildPipeline.BuildPlayer(new UnityEditor.BuildPlayerOptions {
            scenes = new [] { "Assets/HorrorLab/NightShiftMenu.unity", "Assets/HorrorLab/NightShift.unity" },
            locationPathName = "Builds/Stage92-Scoped/NightShift.exe",
            target = UnityEditor.BuildTarget.StandaloneWindows64,
            options = UnityEditor.BuildOptions.None
        });
        System.IO.File.WriteAllText(receipt, Newtonsoft.Json.JsonConvert.SerializeObject(new {
            result = report.summary.result.ToString(), errors = report.summary.totalErrors,
            warnings = report.summary.totalWarnings, bytes = report.summary.totalSize,
            seconds = report.summary.totalTime.TotalSeconds, outputPath = report.summary.outputPath
        }, Newtonsoft.Json.Formatting.Indented));
    } catch (System.Exception e) {
        System.IO.File.WriteAllText(receipt, Newtonsoft.Json.JsonConvert.SerializeObject(new { error = e.ToString() }));
    }
}
System.IO.File.ReadAllText("TestResults/Stage92Scope/build.json")
