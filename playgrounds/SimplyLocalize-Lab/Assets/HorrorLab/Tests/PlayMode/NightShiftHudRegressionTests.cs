using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
namespace SimplyLocalize.Tests
{
    public class NightShiftHudRegressionTests
    {
        private const string Menu = "Assets/HorrorLab/NightShiftMenu.unity";
        private object app;
        private bool loadedMenu;
        private readonly Dictionary<string, (bool exists, string text, int number)> prefs = new();
        private static object Property(object target, string name) => target.GetType().GetProperty(name).GetValue(target);
        private static Task Call(object target, string name, params object[] args) => (Task)target.GetType().GetMethod(name).Invoke(target, args);
        private static IEnumerator Done(Task task) { float end = Time.realtimeSinceStartup + 20; while (!task.IsCompleted && Time.realtimeSinceStartup < end) yield return null; Assert.That(task.IsCompletedSuccessfully, Is.True, task.Exception?.ToString()); }
        [UnitySetUp] public IEnumerator Setup()
        {
            foreach (var suffix in new[] { "text", "voice", "visual", "inscriptions", "subtitles" }) { string key = "SimplyLocalize.HorrorLab.v2." + suffix; prefs[key] = (PlayerPrefs.HasKey(key), PlayerPrefs.GetString(key), PlayerPrefs.GetInt(key)); }
            loadedMenu = !SceneManager.GetSceneByPath(Menu).isLoaded;
            if (loadedMenu) yield return SceneManager.LoadSceneAsync(Menu, LoadSceneMode.Additive);
            var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("SimplyLocalize.HorrorLab.HorrorApp")).First(t => t != null);
            app = type.GetProperty("Instance").GetValue(null); Assert.That(app, Is.Not.Null); yield return Done((Task)Property(app, "Initialization"));
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            if (app != null) yield return Done(Call(app, "LeaveWorldAsync"));
            if (loadedMenu && SceneManager.GetSceneByPath(Menu).isLoaded) yield return SceneManager.UnloadSceneAsync(Menu);
            foreach (var pair in prefs) { if (!pair.Value.exists) PlayerPrefs.DeleteKey(pair.Key); else if (pair.Key.EndsWith("subtitles") || pair.Key.EndsWith("inscriptions")) PlayerPrefs.SetInt(pair.Key, pair.Value.number); else PlayerPrefs.SetString(pair.Key, pair.Value.text); } PlayerPrefs.Save(); prefs.Clear(); app = null;
        }
        [UnityTest] public IEnumerator Dropdowns_HaveRealIconsAndSelectedMarkForAllLanguages()
        {
            var hud = Property(app, "Hud"); var dropdown = (TMP_Dropdown)Property(hud, "TextLanguage");
            foreach (var code in new[] { "en", "ru", "pl" })
            {
                yield return Done(Call(app, "ChangeTextAsync", code)); dropdown.Show(); yield return null; Canvas.ForceUpdateCanvases();
                var arrow = dropdown.transform.Find("Arrow").GetComponentsInChildren<Graphic>(true).Single(g => g.GetType().Name == "HorrorDropdownIcon");
                Assert.That(arrow.enabled && arrow.gameObject.activeInHierarchy, Is.True); Assert.That(dropdown.transform.Find("Arrow").GetComponent<Image>().enabled, Is.False);
                var mesh = arrow.canvasRenderer.GetMesh(); Assert.That(mesh.vertexCount, Is.EqualTo(3));
                var selected = UnityEngine.Object.FindObjectsByType<Toggle>(FindObjectsSortMode.None).Single(t => t.name.StartsWith("Item ") && t.isOn);
                Assert.That(selected.graphic.GetType().Name, Is.EqualTo("HorrorDropdownIcon")); Assert.That(selected.graphic.gameObject.activeInHierarchy, Is.True);
                dropdown.Hide(); yield return null;
            }
        }
        [UnityTest] public IEnumerator MenuAndSubtitles_DoNotOverlapAndLongTranslationsRemainVisible()
        {
            yield return Done(Call(app, "StartWorldAsync"));
            var component = (Component)app; var hud = Property(app, "Hud"); var subtitle = (TMP_Text)Property(hud, "Subtitle");
            var menu = component.GetComponentsInChildren<RectTransform>(true).Single(t => t.name == "Settings");
            var replay = component.GetComponentsInChildren<Button>(true).Single(b => b.name == "Replay");
            app.GetType().GetMethod("SetSubtitles").Invoke(app, new object[] { true });
            foreach (var code in new[] { "en", "ru", "pl" })
            {
                yield return Done(Call(app, "ChangeTextAsync", code)); replay.onClick.Invoke(); yield return null; Canvas.ForceUpdateCanvases();
                Assert.That(subtitle.gameObject.activeInHierarchy, Is.True); Assert.That(subtitle.text, Is.Not.Empty);
                var left = new Vector3[4]; var right = new Vector3[4]; menu.GetWorldCorners(left); ((RectTransform)subtitle.transform.parent).GetWorldCorners(right);
                Assert.That(right[0].x, Is.GreaterThan(left[2].x), "Subtitle panel must sit to the right of the open settings menu.");
                subtitle.ForceMeshUpdate(); Assert.That(subtitle.isTextOverflowing, Is.False, code + " subtitle must fit its panel.");
                var presentation = Property(app, "Presentation"); var dialogue = Property(presentation, "Dialogue"); dialogue.GetType().GetMethod("Stop").Invoke(dialogue, null);
            }
        }
    }
}
