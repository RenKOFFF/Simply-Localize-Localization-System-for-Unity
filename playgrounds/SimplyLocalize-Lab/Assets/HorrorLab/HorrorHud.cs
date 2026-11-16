using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SimplyLocalize.HorrorLab
{
    public sealed class HorrorHud : IDisposable
    {
        private readonly HorrorApp app;
        private readonly TMP_FontAsset font;
        private readonly List<(TMP_Text text, string key, ProfileApplier profile)> labels = new();
        private readonly Dictionary<TMP_Text, ProfileApplier> dynamicProfiles = new();
        private readonly RectTransform canvas, backdrop, menu, document, interactionPanel, translationPanel, loading, intro;
        private readonly TMP_Text objective, prompt, translation, subtitle, message, documentBody, documentTitle, subtitlesValue, progress, hint;
        private readonly TMP_Dropdown textLanguage, voiceLanguage, visualLanguage, inscriptionMode;
        private readonly UnityEngine.UI.Button start, resume, leave, replay, cancel;
        private readonly Color cream = new Color(.9f, .9f, .85f);
        public TMP_Text Subtitle => subtitle;
        public TMP_Dropdown TextLanguage => textLanguage;
        public TMP_Dropdown VoiceLanguage => voiceLanguage;
        public TMP_Dropdown VisualLanguage => visualLanguage;
        public TMP_Dropdown InscriptionModeDropdown => inscriptionMode;
        public HorrorHud(HorrorApp app, TMP_FontAsset font)
        {
            this.app = app; this.font = font;
            var obj = new GameObject("Night Shift UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            obj.transform.SetParent(app.transform, false); canvas = (RectTransform)obj.transform;
            obj.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = obj.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 900); scaler.matchWidthOrHeight = .5f;
            backdrop = Panel("Menu Background", canvas, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Color(.022f, .035f, .04f));
            var header = Panel("Header", canvas, Vector2.up, Vector2.one, new Vector2(0, -110), Vector2.zero, new Color(.025f, .035f, .04f, .87f));
            Label(header, "UI/Title", 43, 40, 14, 900, 58); Label(header, "UI/Chapter", 17, 44, 72, 1000, 26);
            intro = Panel("Menu Introduction", canvas, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-790, -730), new Vector2(-50, -155), new Color(.055f, .075f, .075f));
            Label(intro, "UI/IntroTitle", 38, 35, 35, 670, 70); Label(intro, "UI/Intro", 25, 35, 135, 670, 320);
            Label(intro, "UI/Demo", 17, 35, 490, 670, 45);
            objective = Text(canvas, 21, Vector2.one, Vector2.one, new Vector2(-540, -185), new Vector2(-45, -125)); objective.alignment = TextAlignmentOptions.TopRight;
            menu = Panel("Settings", canvas, Vector2.up, Vector2.up, new Vector2(40, -822), new Vector2(710, -140), new Color(.025f, .035f, .04f, .96f));
            Label(menu, "UI/Settings", 29, 28, 20, 610, 44);
            Label(menu, "UI/TextLanguage", 19, 28, 84, 285, 38);
            textLanguage = Dropdown(menu, 340, 82, app.textConfig, code => app.Observe(app.ChangeTextAsync(code)));
            Label(menu, "UI/VoiceLanguage", 19, 28, 142, 285, 38);
            voiceLanguage = Dropdown(menu, 340, 140, app.worldDefinition.voiceConfig, app.SetVoice);
            Label(menu, "UI/VisualLanguage", 19, 28, 200, 285, 44);
            visualLanguage = Dropdown(menu, 340, 198, app.worldDefinition.visualConfig, app.SetVisual);
            Label(menu, "UI/Signs", 19, 28, 262, 285, 45);
            inscriptionMode = Dropdown(menu, 340, 260, app.textConfig, _ => { });
            inscriptionMode.onValueChanged.RemoveAllListeners(); inscriptionMode.onValueChanged.AddListener(index => app.SetInscriptions((InscriptionMode)index));
            Label(menu, "UI/Subtitles", 19, 28, 324, 340, 45);
            subtitlesValue = Button(menu, "Subtitles", 450, 326, 180, 42, null, () => app.SetSubtitles(!app.Preferences.Subtitles));
            hint = PlainLabel(menu, 17, 28, 386, 600, 68);
            start = Button(menu, "StartWorld", 28, 482, 602, 48, "UI/Start", () => app.Observe(app.StartWorldAsync())).GetComponentInParent<UnityEngine.UI.Button>();
            resume = Button(menu, "Resume", 28, 482, 290, 48, "UI/Resume", () => app.Game?.ResumeGame()).GetComponentInParent<UnityEngine.UI.Button>();
            replay = Button(menu, "Replay", 340, 482, 290, 48, "UI/Replay", () => { if (app.Presentation != null) app.Observe(app.Presentation.Dialogue.PlayAsync(app.worldDefinition.dialogues[0].sequenceId)); }).GetComponentInParent<UnityEngine.UI.Button>();
            leave = Button(menu, "LeaveWorld", 28, 548, 602, 48, "UI/LeaveWorld", () => app.Observe(app.LeaveWorldAsync())).GetComponentInParent<UnityEngine.UI.Button>();
            Label(menu, "UI/Demo", 14, 28, 620, 600, 30);
            var bottom = Panel("Controls", canvas, Vector2.zero, Vector2.right, Vector2.zero, new Vector2(0, 48), new Color(.025f, .035f, .04f, .9f));
            var controls = Text(bottom, 16, Vector2.zero, Vector2.one, new Vector2(35, 8), new Vector2(-35, -8)); Bind(controls, "UI/Controls");
            interactionPanel = Panel("Interaction Prompt", canvas, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(440, 60), new Color(.02f, .025f, .03f, .88f)); interactionPanel.pivot = new Vector2(.5f, 0);
            prompt = Text(interactionPanel, 21, Vector2.zero, Vector2.one, new Vector2(12, 8), new Vector2(-12, -8)); prompt.alignment = TextAlignmentOptions.Center;
            translationPanel = Panel("Sign Translation", canvas, new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(-440, 192), new Vector2(440, 282), new Color(.02f, .025f, .03f, .88f));
            translation = Text(translationPanel, 21, Vector2.zero, Vector2.one, new Vector2(18, 8), new Vector2(-18, -8)); translation.alignment = TextAlignmentOptions.Center;
            var subPanel = Panel("Subtitles", canvas, new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(-570, 65), new Vector2(570, 178), new Color(.02f, .025f, .03f, .88f));
            subtitle = Text(subPanel, 25, Vector2.zero, Vector2.one, new Vector2(25, 12), new Vector2(-25, -12)); subtitle.alignment = TextAlignmentOptions.Center;
            message = Text(canvas, 25, Vector2.one, Vector2.one, new Vector2(-790, -300), new Vector2(-45, -205)); message.alignment = TextAlignmentOptions.Center;
            document = Panel("Document", canvas, new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(-420, -285), new Vector2(420, 285), new Color(.065f, .075f, .07f, .99f));
            documentTitle = PlainLabel(document, 31, 40, 30, 760, 75); documentBody = PlainLabel(document, 25, 40, 125, 760, 300);
            Button(document, "CloseDocument", 40, 465, 760, 60, "UI/Close", () => app.Game?.CloseDocument());
            loading = Panel("Loading Curtain", canvas, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Color(.022f, .035f, .04f, 1));
            progress = Text(loading, 30, new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(-500, -20), new Vector2(500, 90)); progress.alignment = TextAlignmentOptions.Center;
            cancel = Button(loading, "Cancel", 80, 100, 260, 50, "UI/Cancel", app.CancelPreparation).GetComponentInParent<UnityEngine.UI.Button>();
            foreach (var text in new[] { objective, prompt, translation, subtitle, message, documentBody, documentTitle, subtitlesValue, progress, hint })
            { var profile = new ProfileApplier(); profile.CacheOriginals(text); dynamicProfiles.Add(text, profile); }
            Refresh();
        }
        private RectTransform Panel(string name, Transform parent, Vector2 min, Vector2 max, Vector2 from, Vector2 to, Color color)
        {
            var obj = new GameObject(name, typeof(RectTransform), typeof(UnityEngine.UI.Image)); obj.transform.SetParent(parent, false);
            var rect = (RectTransform)obj.transform; Place(rect, min, max, from, to);
            obj.GetComponent<UnityEngine.UI.Image>().color = color; return rect;
        }
        private static void Place(RectTransform rect, Vector2 min, Vector2 max, Vector2 from, Vector2 to)
        { rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = from; rect.offsetMax = to; }
        private TMP_Text Text(Transform parent, int size, Vector2 min, Vector2 max, Vector2 from, Vector2 to)
        {
            var obj = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI)); obj.transform.SetParent(parent, false);
            var text = obj.GetComponent<TMP_Text>(); Place(text.rectTransform, min, max, from, to);
            text.font = font; text.fontSize = size; text.color = cream; text.raycastTarget = false;
            text.enableAutoSizing = true; text.fontSizeMin = size * .72f; text.fontSizeMax = size;
            text.textWrappingMode = TextWrappingModes.Normal;
            return text;
        }
        private TMP_Text PlainLabel(Transform parent, int size, float x, float y, float w, float h)
            => Text(parent, size, Vector2.up, Vector2.up, new Vector2(x, -y - h), new Vector2(x + w, -y));
        private TMP_Text Label(Transform parent, string key, int size, float x, float y, float w, float h)
        { var text = PlainLabel(parent, size, x, y, w, h); Bind(text, key); return text; }
        private void Bind(TMP_Text text, string key)
        { var profile = new ProfileApplier(); profile.CacheOriginals(text); labels.Add((text, key, profile)); }
        private TMP_Text Button(Transform parent, string name, float x, float y, float w, float h, string key, Action action)
        {
            var rect = Panel(name, parent, Vector2.up, Vector2.up, new Vector2(x, -y - h), new Vector2(x + w, -y), new Color(.18f, .22f, .21f));
            var button = rect.gameObject.AddComponent<UnityEngine.UI.Button>(); button.targetGraphic = rect.GetComponent<UnityEngine.UI.Image>();
            var colors = button.colors; colors.highlightedColor = new Color(.9f, .77f, .53f); colors.pressedColor = new Color(.6f, .5f, .34f); button.colors = colors;
            button.onClick.AddListener(() => action());
            var label = Text(rect, 19, Vector2.zero, Vector2.one, new Vector2(10, 4), new Vector2(-10, -4)); label.alignment = TextAlignmentOptions.Center;
            if (key != null) Bind(label, key);
            return label;
        }
        private TMP_Dropdown Dropdown(Transform parent, float x, float y, LocalizationConfig config, Action<string> changed)
        {
            var obj = TMP_DefaultControls.CreateDropdown(new TMP_DefaultControls.Resources()); obj.transform.SetParent(parent, false);
            var rect = (RectTransform)obj.transform; Place(rect, Vector2.up, Vector2.up, new Vector2(x, -y - 44), new Vector2(x + 290, -y));
            var drop = obj.GetComponent<TMP_Dropdown>(); drop.ClearOptions();
            foreach (var text in obj.GetComponentsInChildren<TMP_Text>(true)) { text.font = font; text.fontSize = 20; text.color = new Color(.08f, .1f, .1f); }
            var options = new List<string>(); foreach (var language in config.languages) options.Add(language.displayName);
            drop.AddOptions(options); drop.onValueChanged.AddListener(index => changed(config.languages[index].Code)); return drop;
        }
        public void Refresh()
        {
            var text = app.Text; var settings = app.Preferences; var game = app.Game;
            foreach (var binding in labels) { binding.profile.Apply(binding.text, text.Profile); Assign(binding.text, text.Get(binding.key)); }
            foreach (var binding in dynamicProfiles) binding.Value.Apply(binding.Key, text.Profile);
            bool inWorld = app.State == HorrorAppState.World && game != null;
            backdrop.gameObject.SetActive(!inWorld); intro.gameObject.SetActive(!inWorld);
            menu.gameObject.SetActive(!inWorld || game.MenuOpen);
            objective.gameObject.SetActive(inWorld);
            textLanguage.SetValueWithoutNotify(Mathf.Max(0, app.textConfig.languages.FindIndex(p => p.Code == (text.Locale ?? settings.TextLocale))));
            voiceLanguage.SetValueWithoutNotify(app.worldDefinition.voiceConfig.languages.FindIndex(p => p.Code == settings.VoiceLocale));
            visualLanguage.SetValueWithoutNotify(app.worldDefinition.visualConfig.languages.FindIndex(p => p.Code == settings.VisualLocale));
            textLanguage.interactable = !app.Busy;
            voiceLanguage.interactable = visualLanguage.interactable = app.State == HorrorAppState.Menu && !app.Busy;
            inscriptionMode.ClearOptions(); inscriptionMode.AddOptions(new List<string> { text.Get("UI/Off"), text.Get("UI/Auto"), text.Get("UI/Always") });
            inscriptionMode.SetValueWithoutNotify((int)settings.Inscriptions);
            Assign(subtitlesValue, text.Get(settings.Subtitles ? "UI/On" : "UI/Off"));
            Assign(hint, text.Get(inWorld ? "UI/HeavyLocked" : "UI/HeavyMenuHint"));
            start.gameObject.SetActive(!inWorld); start.interactable = !app.Busy && text.Profile != null;
            resume.gameObject.SetActive(inWorld); replay.gameObject.SetActive(inWorld); leave.gameObject.SetActive(inWorld);
            resume.interactable = replay.interactable = leave.interactable = !app.Busy;
            bool doc = inWorld && (game.NoteOpen || game.InventoryOpen); document.gameObject.SetActive(doc);
            if (doc)
            {
                Assign(documentTitle, text.Get(game.NoteOpen ? "World/Note" : "UI/Inventory"));
                Assign(documentBody, text.Get(game.NoteOpen ? "Document/Body" : game.HasKey ? "Game/InventoryKey" : "Game/InventoryEmpty"));
            }
            if (inWorld)
            {
                Assign(objective, text.Get(game.DoorOpen ? "Game/ObjectiveDone" : game.HasKey ? "Game/ObjectiveDoor" : "Game/ObjectiveKey"));
                if (game.Focused != null) Assign(prompt, text.Format("UI/InteractPrompt", text.Get(game.Focused.textKey)));
            }
            var sign = inWorld ? game.InspectedSign : null;
            bool translate = inWorld && !game.MenuOpen && !doc && app.Presentation.ShouldTranslate(sign, text.Locale, settings.Inscriptions);
            translationPanel.gameObject.SetActive(translate);
            if (translate) Assign(translation, text.Format("UI/InscriptionText", text.Get(sign.textKey)));
            var line = app.Presentation != null ? app.Presentation.Dialogue?.CurrentLine : null;
            subtitle.transform.parent.gameObject.SetActive(inWorld && settings.Subtitles && line != null);
            if (line != null) Assign(subtitle, text.Format("UI/SubtitleLine", text.Get(line.speakerKey), text.Get(line.textKey)));
            string notice = app.ErrorKey;
            if (notice == null && game != null)
            {
                switch (game.Notice)
                {
                    case HorrorNotice.KeyFound: notice = "Game/KeyFound"; break;
                    case HorrorNotice.DoorLocked: notice = "Game/DoorLocked"; break;
                    case HorrorNotice.Complete: notice = "Game/Complete"; break;
                }
            }
            Assign(message, notice == null ? "" : text.Get(notice));
            loading.gameObject.SetActive(app.Busy);
            Assign(progress, text.Get(app.TextBusy ? "UI/LoadingText" : app.ProgressKey));
            cancel.interactable = app.State == HorrorAppState.PreparingWorld || app.TextBusy;
            UpdatePosition();
        }
        private static void Assign(TMP_Text label, string value) { if (label.text != value) label.text = value; }
        public void UpdatePosition()
        {
            var game = app.Game;
            bool visible = app.State == HorrorAppState.World && !app.Busy && game != null && !game.MenuOpen && !game.NoteOpen && !game.InventoryOpen && game.Focused != null && game.Focused.IsAvailable;
            if (visible)
            {
                var viewport = game.view.WorldToViewportPoint(game.Focused.InteractionPoint + Vector3.up * .15f);
                visible = viewport.z > 0 && viewport.x > 0 && viewport.x < 1 && viewport.y > 0 && viewport.y < 1;
                var size = canvas.rect.size;
                interactionPanel.anchoredPosition = new Vector2(Mathf.Clamp(viewport.x * size.x, 230, Mathf.Max(230, size.x - 230)), Mathf.Clamp(viewport.y * size.y + 24, 64, Mathf.Max(64, size.y - 180)));
            }
            interactionPanel.gameObject.SetActive(visible);
        }
        public void Dispose() { foreach (var binding in labels) binding.profile.Dispose(); foreach (var profile in dynamicProfiles.Values) profile.Dispose(); }
    }
}
