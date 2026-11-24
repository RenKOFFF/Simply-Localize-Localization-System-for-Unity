using System;
using System.IO;
using System.Linq;
using SimplyLocalize;
using SimplyLocalize.Editor;
using SimplyLocalize.Editor.Windows.Tabs;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.UIElements;
[LocalizationEditorTab("External Game")]
public sealed class ExternalGameTab : IEditorTab {
 public void Build(VisualElement root) {root.Add(new Label("External extension"));}
}
public static class InstallSmoke {
 public static void Build() {
  var setup=LocalizationProjectSetup.Configure(new[]{"en","ru"},"en","en");
  if(setup.ValidationErrors.Count!=0 || setup.CreatedPaths.Count!=5) throw new Exception("Unexpected setup result");
  string guid=AssetDatabase.AssetPathToGUID(LocalizationProjectSetup.ConfigAssetPath);
  string json=setup.CreatedPaths.Single(p=>p.EndsWith("/en/text/main.json"));
  File.WriteAllText(json,"{\"translations\":{\"Menu/Play\":\"External translation\"}}");
  var repeat=LocalizationProjectSetup.Configure(new[]{"en","ru"},"en","en");
  if(repeat.CreatedPaths.Count!=0 || AssetDatabase.AssetPathToGUID(LocalizationProjectSetup.ConfigAssetPath)!=guid) throw new Exception("Setup is not idempotent");
  if(!TypeCache.GetTypesWithAttribute<LocalizationEditorTabAttribute>().Contains(typeof(ExternalGameTab))) throw new Exception("External tab not discovered");
  var root=new VisualElement();new ExternalGameTab().Build(root); if(root.childCount!=1) throw new Exception("External tab did not build");
  if(AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetName().Name.StartsWith("SimplyLocalize.Tests"))) throw new Exception("Internal tests installed");
  AssetDatabase.Refresh();
  EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
  new GameObject("Startup verification").AddComponent<ExternalStartupProbe>();
  EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(),"Assets/Startup.unity");
  var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{"Assets/Startup.unity"},locationPathName="Build/Startup.exe",target=BuildTarget.StandaloneWindows64,options=BuildOptions.None});
  File.WriteAllText("install-result.json",JsonUtility.ToJson(new Result{passed=report.summary.result==BuildResult.Succeeded,errors=(int)report.summary.totalErrors,created=setup.CreatedPaths.Count,repeated=repeat.CreatedPaths.Count,externalEditorTab=true,internalTests=false}));
  if(report.summary.result!=BuildResult.Succeeded) throw new Exception("Build failed");
 }
 [Serializable] private class Result {public bool passed,externalEditorTab,internalTests;public int errors,created,repeated;}
}