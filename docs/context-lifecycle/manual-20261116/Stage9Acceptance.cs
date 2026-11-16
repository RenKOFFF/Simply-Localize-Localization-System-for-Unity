using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SimplyLocalize;
using SimplyLocalize.Data;
using SimplyLocalize.HorrorLab;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
public static class Stage9Acceptance
{
    static string Root = "D:/Unity/Projects/SimplyLocalize/docs/context-lifecycle/manual-20261116/";
    static HorrorApp App => HorrorApp.Instance;
    static List<object> checks = new List<object>(), samples = new List<object>(), logs = new List<object>();
    static void Check(bool pass,string name) { checks.Add(new { pass,name }); }
    static void Log(string message,string stack,LogType type) { if(type!=LogType.Log)logs.Add(new {type=type.ToString(),message,stack}); }
    static object Registry() {
        var t=typeof(LocalizationContext).Assembly.GetType("SimplyLocalize.LocalizationResourceLease");
        var d=(IDictionary)t.GetField("Shared",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
        var rows=new List<object>();
        foreach(DictionaryEntry p in d) {var e=p.Value;var et=e.GetType();var a=(UnityEngine.Object)et.GetField("Asset",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(e);rows.Add(new {id=p.Key,name=a!=null?a.name:null,type=a!=null?a.GetType().Name:null,users=et.GetField("Users",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(e),owned=et.GetField("UnloadWhenUnused",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(e)});}
        return rows;
    }
    static void Sample(string name) {samples.Add(new {name,frame=Time.frameCount,state=App.State.ToString(),text=App.Text.Locale,voice=App.Preferences.VoiceLocale,visual=App.Preferences.VisualLocale,audio=Resources.FindObjectsOfTypeAll<AudioClip>().Select(c=>new {id=c.GetInstanceID(),c.name,state=c.loadState.ToString()}).ToArray(),registry=Registry()});}
    static async Task Until(Func<bool> done) {double end=Time.realtimeSinceStartupAsDouble+20;while(!done()) {if(Time.realtimeSinceStartupAsDouble>end)throw new TimeoutException("Scenario did not settle");await Task.Yield();}}
    static void Click(string name) {var b=UnityEngine.Object.FindObjectsByType<Button>(FindObjectsSortMode.None).Single(b=>b.name==name);if(!b.interactable)throw new Exception("Disabled button: "+name);ExecuteEvents.Execute(b.gameObject,new PointerEventData(EventSystem.current),ExecuteEvents.pointerClickHandler);}
    static async Task Select(TMP_Dropdown d,int index) {ExecuteEvents.Execute(d.gameObject,new PointerEventData(EventSystem.current),ExecuteEvents.pointerClickHandler);await Task.Yield();var t=UnityEngine.Object.FindObjectsByType<Toggle>(FindObjectsSortMode.None).Single(t=>t.name.StartsWith("Item "+index+":"));ExecuteEvents.Execute(t.gameObject,new PointerEventData(EventSystem.current),ExecuteEvents.pointerClickHandler);await Until(()=>!d.IsExpanded);await Until(()=>!App.Busy);}
    static async Task Enter(){Click("StartWorld");await Until(()=>App.State==HorrorAppState.World||(!App.Busy&&App.ErrorKey!=null));Check(App.State==HorrorAppState.World,"world published");}
    static async Task Leave(){Click("LeaveWorld");await Until(()=>App.State==HorrorAppState.Menu);}
    static async Task Capture(string name){ScreenCapture.CaptureScreenshot(Root+name+".png");for(int i=0;i<4;i++)await Task.Yield();}
    static LocalizationContext Voice(LocalizationResourceOwnership ownership) => new LocalizationContext(App.worldDefinition.voiceConfig,App.worldDefinition.voiceConfig.languages.Select(p=>new ResourcesLanguageSources(p.Code,assetTables:new[]{App.worldDefinition.voiceConfig.resourcesBasePath+"/"+p.Code+"/Radio"})),new LocalizationContextOptions{Ownership=ownership});
    public static async Task<string> Main(string mode)
    {
        checks.Clear();samples.Clear();logs.Clear();Application.logMessageReceived+=Log;string failure=null;
        try {
            await App.Initialization;Sample("before-"+mode);
            if(mode=="first") {
                await Select(App.Hud.VoiceLanguage,1);await Select(App.Hud.VisualLanguage,0);await Enter();
                var c=App.Session.Clip("Radio/01");Check(c.loadState==AudioDataLoadState.Loaded,"Russian first clip Loaded before playback");
                int requests=App.Metrics.TotalReads;Click("Replay");Check(App.Metrics.TotalReads==requests,"first dialogue has zero new provider reads");
                var clip=App.Presentation.Dialogue.CurrentClip;int applied=App.Presentation.VisualApplyCount;
                await Select(App.Hud.TextLanguage,0);
                Check(App.Presentation.Dialogue.CurrentClip==clip,"text selection preserves active voice clip");Check(App.Presentation.VisualApplyCount==applied,"text selection preserves visual application");
                var line=App.Presentation.Dialogue.CurrentLine;Check(line!=null&&App.Hud.Subtitle.text==App.Text.Format("UI/SubtitleLine",App.Text.Get(line.speakerKey),App.Text.Get(line.textKey)),"English subtitle follows text selection");
                await Capture("03-world-dialogue");Sample("world-ready");App.Presentation.Dialogue.Stop();await Leave();Sample("after-leave");
            }
            else if(mode=="fallback") {
                await Select(App.Hud.TextLanguage,2);await Select(App.Hud.VisualLanguage,2);await Enter();
                var sign=App.Game.interactables.Single(i=>i.textureKey=="Signs/Warning");var v=App.Session.Visual(sign.textureKey);
                Check(v.contentLanguage.Code=="en","Polish warning resolves English fallback");
                for(int i=0;i<3;i++){await Select(App.Hud.InscriptionModeDropdown,i);Check(App.Presentation.ShouldTranslate(sign,"pl",(InscriptionMode)i)==(i!=0),"warning translation mode "+(InscriptionMode)i);}
                Sample("fallback-ready");await Capture("04-polish-fallback-menu");
            }
            else if(mode=="cycles") {
                if(App.State==HorrorAppState.World)await Leave();
                for(int i=0;i<15;i++) {
                    await Select(App.Hud.VoiceLanguage,i%2);await Select(App.Hud.VisualLanguage,i%3);await Enter();
                    App.Presentation.voiceSource.mute=true;Click("Replay");Check(App.Presentation.Dialogue.CurrentClip!=null&&App.Presentation.Dialogue.CurrentClip.loadState==AudioDataLoadState.Loaded,"cycle "+i+" voice ready");
                    Check(App.Game.interactables.Where(t=>!string.IsNullOrEmpty(t.textureKey)).All(t=>App.Session.Visual(t.textureKey).texture!=null),"cycle "+i+" textures valid");
                    App.Presentation.Dialogue.Stop();await Leave();Sample("cycle-"+i+"-closed");Check(Resources.FindObjectsOfTypeAll<AudioClip>().Length==0,"cycle "+i+" audio released");
                }
            }
            else if(mode=="cancel") {
                Click("StartWorld");Check(App.State==HorrorAppState.PreparingWorld,"loading curtain entered");Click("Cancel");await Until(()=>App.State==HorrorAppState.Menu);
                Check(App.Session==null&&App.Game==null,"cancel leaves no partial world");Check(Resources.FindObjectsOfTypeAll<AudioClip>().Length==0,"cancel audio drained");Sample("cancelled");await Enter();await Leave();Sample("retry-closed");
            }
            else if(mode=="owners") {
                var a=Voice(LocalizationResourceOwnership.Owned);var b=Voice(LocalizationResourceOwnership.Owned);
                try {
                    await a.ChangeLanguageAsync("en");await b.ChangeLanguageAsync("en");var c=a.ResolveAsset<AudioClip>("Radio/01").Value;int id=c.GetInstanceID();
                    Check(b.ResolveAsset<AudioClip>("Radio/01").Value.GetInstanceID()==id,"owners share same native clip");Sample("two-owners");
                    await a.ReleaseAsync();Check(b.ResolveAsset<AudioClip>("Radio/01").Value.loadState==AudioDataLoadState.Loaded,"second owner remains usable");Sample("first-owner-closed");
                    await b.ReleaseAsync();Check(!Resources.FindObjectsOfTypeAll<AudioClip>().Any(x=>x.GetInstanceID()==id),"last owner releases clip");Sample("last-owner-closed");
                } finally {await a.ReleaseAsync();await b.ReleaseAsync();}
                var borrowed=Voice(LocalizationResourceOwnership.Borrowed);await borrowed.ChangeLanguageAsync("en");var external=borrowed.ResolveAsset<AudioClip>("Radio/01").Value;int externalId=external.GetInstanceID();await borrowed.ReleaseAsync();
                Check(Resources.FindObjectsOfTypeAll<AudioClip>().Any(c=>c.GetInstanceID()==externalId)&&external.loadState==AudioDataLoadState.Loaded,"Borrowed preserves external clip");Sample("borrowed-closed-external-keeps-clip");
                external=null;var cleanup=Voice(LocalizationResourceOwnership.Owned);await cleanup.ChangeLanguageAsync("en");await cleanup.ReleaseAsync();Sample("explicit-owned-context-closed");
            }
            else if(mode.StartsWith("session")) {await Enter();Check(App.Session.Clip("Radio/01").loadState==AudioDataLoadState.Loaded,"fresh Play session prepares audio");Sample("session-world");await Leave();Sample("session-closed");Check(Resources.FindObjectsOfTypeAll<AudioClip>().Length==0,"fresh session releases audio");}
        } catch(Exception e){failure=e.ToString();}
        finally {Application.logMessageReceived-=Log;}
        var result=new {mode,utc=DateTime.UtcNow.ToString("O"),unity=Application.unityVersion,failure,checks,samples,logs};
        string json=JsonConvert.SerializeObject(result,Formatting.Indented);File.WriteAllText(Root+mode+".json",json);return json;
    }
}
