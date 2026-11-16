using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using SimplyLocalize.HorrorLab;
public static class VisualAcceptance {
 public static async Task<string> Main(){
  var a=HorrorApp.Instance;await a.StartWorldAsync();var g=a.Game;g.player.enabled=false;g.player.transform.position=new Vector3(3.7f,.1f,8);g.player.enabled=true;
  var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
  typeof(HorrorPlayground).GetField("yaw",flags).SetValue(g,0f);typeof(HorrorPlayground).GetField("pitch",flags).SetValue(g,0f);
  var rows=new List<object>();
  foreach(var mode in new[]{InscriptionMode.Off,InscriptionMode.Auto,InscriptionMode.Always}){
   EditorWindow.GetWindow(Type.GetType("UnityEditor.GameView,UnityEditor")).Focus();g.ResumeGame();a.SetInscriptions(mode);
   for(int i=0;i<10;i++)await Task.Yield();
   var panel=a.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="Sign Translation");
   rows.Add(new {mode=mode.ToString(),focus=Application.isFocused,sign=g.InspectedSign?.name,menu=g.MenuOpen,visible=panel.gameObject.activeInHierarchy,text=panel.GetComponentInChildren<TMPro.TMP_Text>(true).text,source=a.Session.Visual("Signs/Warning").contentLanguage.Code});
   ScreenCapture.CaptureScreenshot("D:/Unity/Projects/SimplyLocalize/docs/context-lifecycle/manual-20261116/warning-"+mode+".png");for(int i=0;i<3;i++)await Task.Yield();
  }
  g.OpenMenu();await a.LeaveWorldAsync();var json=Newtonsoft.Json.JsonConvert.SerializeObject(rows,Newtonsoft.Json.Formatting.Indented);File.WriteAllText("D:/Unity/Projects/SimplyLocalize/docs/context-lifecycle/manual-20261116/visual-overlay.json",json);return json;
 }
}
