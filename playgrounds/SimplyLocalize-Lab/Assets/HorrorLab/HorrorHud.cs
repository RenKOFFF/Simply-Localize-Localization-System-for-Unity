using System.Collections.Generic;
using System.Linq;
using SimplyLocalize.Components;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SimplyLocalize.HorrorLab
{
    public sealed class HorrorHud : MonoBehaviour
    {
        private HorrorApp app;
        [SerializeField] private RectTransform canvas, backdrop, menu, document, interactionPanel, translationPanel, loading, intro, subtitlePanel;
        [SerializeField] private TMP_Text objective, prompt, translation, subtitle, message, documentBody, documentTitle, subtitlesValue, progress, hint;
        [SerializeField] private TMP_Dropdown textLanguage, voiceLanguage, visualLanguage, inscriptionMode;
        [SerializeField] private UnityEngine.UI.Button start, resume, leave, replay, cancel;
        [SerializeField] private Button toggleSubtitles, closeDocument;
        public TMP_Text Subtitle => subtitle;
        public TMP_Dropdown TextLanguage => textLanguage;
        public TMP_Dropdown VoiceLanguage => voiceLanguage;
        public TMP_Dropdown VisualLanguage => visualLanguage;
        public TMP_Dropdown InscriptionModeDropdown => inscriptionMode;

        public void Bind(HorrorApp owner)
        {
            app = owner;
            foreach (var component in GetComponentsInChildren<LocalizedComponentBase>(true))
                component.Bind(app.Runtime, component.ChannelId, component.SetId);
            ConfigureLanguages(textLanguage, app.localization.MainChannel, code => app.Observe(app.ChangeTextAsync(code)));
            ConfigureLanguages(voiceLanguage, app.VoiceChannel, app.SetVoice);
            ConfigureLanguages(visualLanguage, app.VisualChannel, app.SetVisual);
            inscriptionMode.onValueChanged.AddListener(index => app.SetInscriptions((InscriptionMode)index));
            toggleSubtitles.onClick.AddListener(() => app.SetSubtitles(!app.Preferences.Subtitles));
            start.onClick.AddListener(() => app.Observe(app.StartWorldAsync()));
            resume.onClick.AddListener(() => app.Game?.ResumeGame());
            leave.onClick.AddListener(() => app.Observe(app.LeaveWorldAsync()));
            replay.onClick.AddListener(() =>
            {
                if (app.Presentation != null)
                    app.Observe(app.Presentation.Dialogue.PlayAsync(app.worldDefinition.dialogues[0].sequenceId));
            });
            closeDocument.onClick.AddListener(() => app.Game?.CloseDocument());
            cancel.onClick.AddListener(app.CancelPreparation);
            Refresh();
        }

        private void ConfigureLanguages(TMP_Dropdown dropdown, LocalizationChannelDefinition channel, System.Action<string> select)
        {
            dropdown.ClearOptions();
            dropdown.AddOptions(channel.Languages.Select(code => app.localization.Languages.Single(l => l.Code == code).DisplayName).ToList());
            dropdown.onValueChanged.AddListener(index => select(channel.Languages[index]));
        }

        private static void Place(RectTransform rect, Vector2 min, Vector2 max, Vector2 from, Vector2 to)
        { rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = from; rect.offsetMax = to; }

        public void Refresh()
        {
            var runtime = app.Runtime;
            var settings = app.Preferences;
            var game = app.Game;
            var locale = runtime.IsInitialized ? runtime.GetChannel().RequestedLanguage : settings.TextLocale;
            string Get(string key) => runtime.IsInitialized ? runtime.Get(key) : key;
            bool inWorld = app.State == HorrorAppState.World && game != null;
            backdrop.gameObject.SetActive(!inWorld); intro.gameObject.SetActive(!inWorld);
            menu.gameObject.SetActive(!inWorld || game.MenuOpen);
            objective.gameObject.SetActive(inWorld);
            textLanguage.SetValueWithoutNotify(Mathf.Max(0, app.localization.MainChannel.Languages.ToList().IndexOf(locale)));
            voiceLanguage.SetValueWithoutNotify(app.VoiceChannel.Languages.ToList().IndexOf(settings.VoiceLocale));
            visualLanguage.SetValueWithoutNotify(app.VisualChannel.Languages.ToList().IndexOf(settings.VisualLocale));
            textLanguage.interactable = !app.Busy;
            voiceLanguage.interactable = visualLanguage.interactable = app.State == HorrorAppState.Menu && !app.Busy;
            inscriptionMode.ClearOptions(); inscriptionMode.AddOptions(new List<string> { Get("UI/Off"), Get("UI/Auto"), Get("UI/Always") });
            inscriptionMode.SetValueWithoutNotify((int)settings.Inscriptions);
            Assign(subtitlesValue, Get(settings.Subtitles ? "UI/On" : "UI/Off"));
            Assign(hint, Get(inWorld ? "UI/HeavyLocked" : "UI/HeavyMenuHint"));
            start.gameObject.SetActive(!inWorld); start.interactable = !app.Busy && runtime.IsInitialized;
            resume.gameObject.SetActive(inWorld); replay.gameObject.SetActive(inWorld); leave.gameObject.SetActive(inWorld);
            resume.interactable = replay.interactable = leave.interactable = !app.Busy;
            bool doc = inWorld && (game.NoteOpen || game.InventoryOpen); document.gameObject.SetActive(doc);
            if (doc)
            {
                Assign(documentTitle, Get(game.NoteOpen ? "World/Note" : "UI/Inventory"));
                Assign(documentBody, Get(game.NoteOpen ? "Document/Body" : game.HasKey ? "Game/InventoryKey" : "Game/InventoryEmpty"));
            }
            if (inWorld)
            {
                Assign(objective, Get(game.DoorOpen ? "Game/ObjectiveDone" : game.HasKey ? "Game/ObjectiveDoor" : "Game/ObjectiveKey"));
                if (game.Focused != null) Assign(prompt, runtime.Get("UI/InteractPrompt", Get(game.Focused.textKey)));
            }
            var sign = inWorld ? game.InspectedSign : null;
            bool translate = inWorld && !game.MenuOpen && !doc && app.Presentation.ShouldTranslate(sign, locale, settings.Inscriptions);
            translationPanel.gameObject.SetActive(translate);
            if (translate) Assign(translation, runtime.Get("UI/InscriptionText", Get(sign.textKey)));
            var line = app.Presentation != null ? app.Presentation.Dialogue?.CurrentLine : null;
            subtitlePanel.gameObject.SetActive(inWorld && settings.Subtitles && line != null);
            if (inWorld && game.MenuOpen)
                Place(subtitlePanel, new Vector2(.54f, 0), new Vector2(1, 0), new Vector2(24, 65), new Vector2(-40, 265));
            else
                Place(subtitlePanel, new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(-570, 65), new Vector2(570, 178));
            if (line != null) Assign(subtitle, runtime.Get("UI/SubtitleLine", Get(line.speakerKey), Get(line.textKey)));
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
            Assign(message, notice == null ? "" : Get(notice));
            loading.gameObject.SetActive(app.Busy);
            Assign(progress, Get(app.TextBusy ? "UI/LoadingText" : app.ProgressKey));
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
    }
}
