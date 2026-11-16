using System;
using System.Linq;
using SimplyLocalize.Components;
using TMPro;
using UnityEngine;

namespace SimplyLocalize.Lab.Editor
{
    public static class LocalizationLabChecks
    {
        // Integration smoke check: uses the saved scene's real buttons and components.
        // Run after Start in a fresh Play session. Throws on the first failed expectation.
        public static string Run()
        {
            if (!Application.isPlaying) throw new InvalidOperationException("Enter Play Mode first.");
            var lab = UnityEngine.Object.FindFirstObjectByType<LocalizationLab>();
            if (lab == null) throw new InvalidOperationException("Open the lab scene first.");
            var buttons = lab.GetComponentsInChildren<UnityEngine.UI.Button>(true)
                .ToDictionary(b => b.name, b => b);
            void Click(string action) => buttons[action].onClick.Invoke();
            void Check(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
            TMP_Text TextFor(string key) => lab.GetComponentsInChildren<LocalizedText>(true)
                .First(t => t.Key == key && t.GetComponent<TMP_Text>() != null).GetComponent<TMP_Text>();
            Check(lab.Count == 1 && !lab.AlternateProvider, "Start the checks in a fresh Play session.");
            Click("ru");
            Check(TextFor("Lab/Welcome").text == "Привет, мир!", "Russian TMP text");
            Check(lab.GetComponentInChildren<UnityEngine.UI.Text>().text == "Привет, мир!", "Russian legacy text");
            Check(TextFor("Lab/Fallback").text.Contains("only in English"), "Fallback text");
            Check(TextFor("Lab/Escapes").text == "Escaped braces: {name} / {0}", "Escaped braces");
            Check(lab.plural.GetComponent<TMP_Text>().text == "1 монета", "Russian singular");
            Click("plus");
            Check(lab.plural.GetComponent<TMP_Text>().text == "2 монеты", "Russian few");
            Click("next");
            Check(lab.plural.GetComponent<TMP_Text>().text == "5 монет", "Russian many");
            Click("pl");
            Check(lab.plural.GetComponent<TMP_Text>().text == "5 monet", "Polish plural");
            Click("en");
            Check(lab.plural.GetComponent<TMP_Text>().text == "5 coins", "English plural");
            Check(lab.named.GetComponent<TMP_Text>().text == "Player: Alex, items: 5", "Named arguments");
            var profileText = TextFor("Lab/Profile");
            float baseline = profileText.fontSize;
            Click("ru");
            Check(Mathf.Approximately(profileText.fontSize, baseline * 1.4f), "Local profile multiplier");
            for (int i = 0; i < 3; i++)
            {
                Click("override");
                Check(Mathf.Approximately(profileText.fontSize, baseline * 1.1f), "Global profile multiplier");
                Click("override");
                Check(Mathf.Approximately(profileText.fontSize, baseline * 1.4f), "Override must not accumulate");
            }
            Click("provider");
            Check(TextFor("Lab/Welcome").text.EndsWith("[revision 1]"), "Provider refresh");
            Click("revision");
            Check(TextFor("Lab/Welcome").text.EndsWith("[revision 1]"), "Cached source before Reload");
            int events = lab.Events;
            Click("reload");
            Check(TextFor("Lab/Welcome").text.EndsWith("[revision 2]"), "Reload refresh");
            Check(lab.Events == events + 1, "Exactly one event on Reload");
            events = lab.Events;
            Click("failure");
            Check(lab.Events == events && TextFor("Lab/Welcome").text.EndsWith("[revision 2]"), "Failed provider transaction");
            Check(!lab.diagnostic.text.Contains("ERROR"), "Controller reported an error");
            Click("provider");
            Click("components"); Click("en"); Click("components");
            Check(TextFor("Lab/Welcome").text == "Hello, world!", "Re-enabled component refresh");
            Check(Mathf.Approximately(profileText.fontSize, baseline), "Profile baseline restoration");
            Click("ru");
            Canvas.ForceUpdateCanvases();
            foreach (var text in lab.GetComponentsInChildren<TMP_Text>()) text.ForceMeshUpdate();
            return "PASS: UI buttons, TMP/legacy text, en/ru/pl plurals, named parameters, escapes, fallback, profile restoration, Reload, provider transaction and component re-enable";
        }
    }
}
