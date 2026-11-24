using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SimplyLocalize.HorrorLab
{
    /// <summary>Presentation boundary: gameplay emits IDs and never receives localization managers.</summary>
    public sealed class HorrorWorldPresentation : MonoBehaviour
    {
        public AudioSource voiceSource;
        public HorrorPlayground Game { get; private set; }
        public HorrorDialoguePlayer Dialogue { get; private set; }
        private HorrorWorldSession session;
        private Action changed;
        public int VisualApplyCount { get; private set; }
        private void Start()
        {
            // Opening the world scene directly still leads to the proper bootstrap/menu.
            if (HorrorApp.Instance == null) SceneManager.LoadSceneAsync("NightShiftMenu", LoadSceneMode.Single);
        }
        public void Bind(HorrorPlayground game, HorrorWorldSession content, HorrorLoadMetrics metrics, Action changed)
        {
            Game = game; session = content; this.changed = changed;
            Dialogue = new HorrorDialoguePlayer(voiceSource, session, metrics);
            Dialogue.Changed += changed; Game.Changed += changed; Game.DialogueRequested += OnDialogue;
            foreach (var target in Game.interactables)
            {
                if (target.surface == null || string.IsNullOrEmpty(target.textureKey)) continue;
                var variant = session.Visual(target.textureKey);
                var block = new MaterialPropertyBlock(); target.surface.GetPropertyBlock(block);
                block.SetTexture("_BaseMap", variant.texture); block.SetTexture("_MainTex", variant.texture);
                target.surface.SetPropertyBlock(block); VisualApplyCount++;
            }
            Game.SetReady(true);
        }
        private async void OnDialogue(string id)
        {
            try { await Dialogue.PlayAsync(id); }
            catch (OperationCanceledException) { }
            catch (Exception error) { Debug.LogException(error); }
        }
        public bool ShouldTranslate(HorrorInteractable sign, string textLocale, InscriptionMode mode)
        {
            if (session == null || sign == null || string.IsNullOrEmpty(sign.textureKey) || string.IsNullOrEmpty(sign.textKey) || mode == InscriptionMode.Off) return false;
            var variant = session.Visual(sign.textureKey);
            return variant.containsText && (mode == InscriptionMode.Always || variant.contentLanguage != textLocale);
        }
        public void Detach()
        {
            if (Game != null)
            {
                Game.SetReady(false); Game.Changed -= changed; Game.DialogueRequested -= OnDialogue;
                foreach (var target in Game.interactables) if (target != null && target.surface != null) target.surface.SetPropertyBlock(null);
            }
            if (Dialogue != null) { Dialogue.Changed -= changed; Dialogue.Dispose(); Dialogue = null; }
            session = null; changed = null; Game = null;
        }
        private void OnDestroy() => Detach();
    }
}
