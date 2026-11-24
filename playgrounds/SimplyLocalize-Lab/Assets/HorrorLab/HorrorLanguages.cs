using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace SimplyLocalize.HorrorLab
{
    public enum InscriptionMode { Off, Auto, Always }

    /// <summary>Game preferences. Available choices come from the project channels.</summary>
    public sealed class HorrorPreferences
    {
        private const string Prefix = "SimplyLocalize.HorrorLab.v2.";
        private readonly bool persist;
        public string TextLocale { get; private set; }
        public string VoiceLocale { get; private set; }
        public string VisualLocale { get; private set; }
        public InscriptionMode Inscriptions { get; private set; }
        public bool Subtitles { get; private set; }
        public HorrorPreferences(LocalizationChannelDefinition text, LocalizationChannelDefinition voice, LocalizationChannelDefinition visual, bool persist = true)
        {
            this.persist = persist;
            string Saved(string field, LocalizationChannelDefinition channel)
            {
                var code = persist ? PlayerPrefs.GetString(Prefix + field, channel.DefaultLanguage) : channel.DefaultLanguage;
                return channel.Languages.Contains(code) ? code : channel.DefaultLanguage;
            }
            TextLocale = Saved("text", text); VoiceLocale = Saved("voice", voice); VisualLocale = Saved("visual", visual);
            Inscriptions = (InscriptionMode)Mathf.Clamp(persist ? PlayerPrefs.GetInt(Prefix + "inscriptions", 1) : 1, 0, 2);
            Subtitles = !persist || PlayerPrefs.GetInt(Prefix + "subtitles", 1) != 0;
        }
        public void SetText(string code) { TextLocale = code; Save(); }
        public void SetVoice(string code, LocalizationChannelDefinition channel) { Validate(channel, code); VoiceLocale = code; Save(); }
        public void SetVisual(string code, LocalizationChannelDefinition channel) { Validate(channel, code); VisualLocale = code; Save(); }
        public void SetInscriptions(InscriptionMode mode) { if (!Enum.IsDefined(typeof(InscriptionMode), mode)) throw new ArgumentOutOfRangeException(nameof(mode)); Inscriptions = mode; Save(); }
        public void SetSubtitles(bool value) { Subtitles = value; Save(); }
        public static void Validate(LocalizationChannelDefinition channel, string code)
        { if (channel == null || !channel.Languages.Contains(code)) throw new ArgumentException("Unsupported locale: " + code); }
        private void Save()
        {
            if (!persist) return;
            PlayerPrefs.SetString(Prefix + "text", TextLocale); PlayerPrefs.SetString(Prefix + "voice", VoiceLocale); PlayerPrefs.SetString(Prefix + "visual", VisualLocale);
            PlayerPrefs.SetInt(Prefix + "inscriptions", (int)Inscriptions); PlayerPrefs.SetInt(Prefix + "subtitles", Subtitles ? 1 : 0); PlayerPrefs.Save();
        }
    }

    [Serializable]
    public sealed class HorrorLoadMetrics
    {
        public int textReads, assetTableReads;
        public double lastTextPrepareMs, lastWorldPrepareMs, lastFirstPlayCallMs;
        public string lastAudioStateBeforePlay;
        public int TotalReads => textReads + assetTableReads;
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    // Measurement only; resource behavior and ownership belong to the stock provider.
    internal sealed class ObservedResourcesProvider : ILocalizationSourceProvider
    {
        private readonly ResourcesLocalizationSourceProvider inner = new();
        private readonly HorrorLoadMetrics metrics;
        public ObservedResourcesProvider(HorrorLoadMetrics metrics) => this.metrics = metrics;
        public LocalizationProviderCapabilities GetCapabilities(LocalizationSourceKind kind) => inner.GetCapabilities(kind);
        public Task<LocalizationSourceLease> LoadAsync(LocalizationSourceRequest request, CancellationToken token)
        {
            if (request.Source.Kind == LocalizationSourceKind.TextTable) metrics.textReads++; else metrics.assetTableReads++;
            return inner.LoadAsync(request, token);
        }
    }
#endif
}
