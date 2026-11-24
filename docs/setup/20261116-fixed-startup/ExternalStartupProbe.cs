using System;
using System.Collections.Generic;
using System.IO;
using SimplyLocalize;
using SimplyLocalize.Data;
using UnityEngine;
public sealed class ExternalProvider : ILocalizationDataProvider {
 public Dictionary<string,string> LoadTextData(string code) => new Dictionary<string,string>{{"Menu/Play", "custom:"+code}};
 public bool HasTextData(string code) => true;
 public List<LocalizationAssetTable> LoadAssetTables(string code) => new List<LocalizationAssetTable>();
}
public sealed class ExternalStartupProbe : MonoBehaviour {
 void Awake() {
  try {
   if(!Localization.IsInitialized || Localization.CurrentLanguage!="en" || Localization.Get("Menu/Play")!="External translation") throw new Exception("Automatic startup or external JSON failed");
   var manager=new LocalizationManager(Localization.Config);
   manager.SetDataProvider(new ExternalProvider()); manager.SetLanguage("ru");
   if(manager.Get("Menu/Play")!="custom:ru") throw new Exception("External provider failed");
   File.WriteAllText(Path.Combine(Application.dataPath,"../startup-result.json"),"{\"passed\":true,\"autoStartup\":true,\"externalJson\":true,\"externalProvider\":true}");
   Application.Quit(0);
  } catch(Exception error) {Debug.LogException(error);Application.Quit(3);}
 }
}