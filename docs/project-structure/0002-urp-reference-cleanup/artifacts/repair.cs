if (UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode) throw new System.Exception("Stop Play Mode first");
const string profilePath="Assets/Settings/DefaultVolumeProfile.asset";
const string rendererPath="Assets/Settings/PC_Renderer.asset";
int missingBefore=UnityEditor.AssetDatabase.GetScriptableObjectsWithMissingScriptCount(profilePath);
if (missingBefore!=9) throw new System.Exception("Expected exactly nine missing-script subassets, found "+missingBefore);
var renderer=UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.Universal.UniversalRendererData>(rendererPath);
var so=new UnityEditor.SerializedObject(renderer);
string[] fields={"probeVolumeDebugShader","probeVolumeFragmentationDebugShader","probeVolumeOffsetDebugShader","probeVolumeSamplingDebugShader","probeSamplingDebugMesh","probeSamplingDebugTexture","probeVolumeBlendStatesCS"};
foreach(var field in fields) {
 var p=so.FindProperty("probeVolumeResources."+field);
 if(p==null) throw new System.Exception("Missing obsolete property: "+field);
 if(p.objectReferenceValue!=null) throw new System.Exception("Refusing to clear a working reference: "+field);
 p.objectReferenceValue=null;
}
so.ApplyModifiedPropertiesWithoutUndo();
UnityEditor.EditorUtility.SetDirty(renderer);
int removed=UnityEditor.AssetDatabase.RemoveScriptableObjectsWithMissingScript(profilePath);
UnityEditor.AssetDatabase.SaveAssets();
UnityEditor.AssetDatabase.ForceReserializeAssets(new[]{profilePath,rendererPath});
UnityEditor.AssetDatabase.Refresh();
int remaining=UnityEditor.AssetDatabase.GetScriptableObjectsWithMissingScriptCount(profilePath);
var result=new { missingBefore,removed,remaining,clearedObsoleteFields=fields };
System.IO.File.WriteAllText("../../docs/project-structure/0002-urp-reference-cleanup/artifacts/repair.json",Newtonsoft.Json.JsonConvert.SerializeObject(result,Newtonsoft.Json.Formatting.Indented));
if(remaining!=0) throw new System.Exception("Missing subassets remain");
return result;