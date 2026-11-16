using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SimplyLocalize.Data;
using TMPro;
using UnityEngine;

namespace SimplyLocalize.HorrorLab
{
    public enum InscriptionMode { Off, Auto, Always }

    /// <summary>Sample preferences only. Heavy selections cannot mutate a live session.</summary>
    public sealed class HorrorPreferences
    {
        private const string Prefix = "SimplyLocalize.HorrorLab.v2.";
        private readonly bool persist;
        public string TextLocale { get; private set; }
        public string VoiceLocale { get; private set; }
        public string VisualLocale { get; private set; }
        public InscriptionMode Inscriptions { get; private set; }
        public bool Subtitles { get; private set; }
        public HorrorPreferences(LocalizationConfig text, HorrorWorldDefinition world, bool persist = true)
        {
            this.persist = persist;
            string Saved(string field, LocalizationConfig config, string legacy)
            {
                var code = persist ? PlayerPrefs.GetString(Prefix + field, PlayerPrefs.GetString("SimplyLocalize.HorrorLab.v1." + legacy, config.DefaultLanguageCode)) : config.DefaultLanguageCode;
                return config.HasLanguage(code) ? code : config.DefaultLanguageCode;
            }
            TextLocale = Saved("text", text, "text"); VoiceLocale = Saved("voice", world.voiceConfig, "voice");
            string originalVisual = world.visualConfig.DefaultLanguageCode;
            if (persist && PlayerPrefs.GetInt("SimplyLocalize.HorrorLab.v1.textures", 0) != 0 && world.visualConfig.HasLanguage(TextLocale)) originalVisual = TextLocale;
            VisualLocale = persist ? PlayerPrefs.GetString(Prefix + "visual", originalVisual) : originalVisual;
            if (!world.visualConfig.HasLanguage(VisualLocale)) VisualLocale = world.visualConfig.DefaultLanguageCode;
            int oldSigns = persist ? PlayerPrefs.GetInt("SimplyLocalize.HorrorLab.v1.signs", 1) : 1;
            Inscriptions = (InscriptionMode)Mathf.Clamp(persist ? PlayerPrefs.GetInt(Prefix + "inscriptions", oldSigns == 0 ? 0 : 1) : 1, 0, 2);
            Subtitles = !persist || PlayerPrefs.GetInt(Prefix + "subtitles", PlayerPrefs.GetInt("SimplyLocalize.HorrorLab.v1.subtitles", 1)) != 0;
        }
        public void SetText(string code) { TextLocale = code; Save(); }
        public void SetVoice(string code, LocalizationConfig config) { Validate(config, code); VoiceLocale = code; Save(); }
        public void SetVisual(string code, LocalizationConfig config) { Validate(config, code); VisualLocale = code; Save(); }
        public void SetInscriptions(InscriptionMode mode) { if (!Enum.IsDefined(typeof(InscriptionMode), mode)) throw new ArgumentOutOfRangeException(nameof(mode)); Inscriptions = mode; Save(); }
        public void SetSubtitles(bool value) { Subtitles = value; Save(); }
        public static void Validate(LocalizationConfig config, string code)
        { if (config == null || !config.HasLanguage(code)) throw new ArgumentException("Unsupported locale: " + code); }
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
        private readonly List<LocalizationContext> contexts = new();
        public int TotalReads => textReads + assetTableReads + contexts.Sum(c => c.ResourceRequestCount);
        internal void Track(LocalizationContext context) => contexts.Add(context);
        internal void Finish(LocalizationContext context) { if (contexts.Remove(context)) assetTableReads += context.ResourceRequestCount; }
    }

    // Instrumentation only: every operation is forwarded unchanged to the stock provider.
    internal sealed class ObservedResourcesProvider : ILocalizationDataProvider
    {
        private readonly ResourcesDataProvider inner;
        private readonly HorrorLoadMetrics metrics;
        public ObservedResourcesProvider(string path, HorrorLoadMetrics metrics)
        { inner = new ResourcesDataProvider(path); this.metrics = metrics; }
        public Dictionary<string, string> LoadTextData(string code)
        { metrics.textReads++; return inner.LoadTextData(code); }
        public bool HasTextData(string code) => inner.HasTextData(code);
        public List<LocalizationAssetTable> LoadAssetTables(string code)
        { metrics.assetTableReads++; return inner.LoadAssetTables(code); }
    }

    public sealed class HorrorTextService : IDisposable
    {
        private LocalizationManager manager;
        private readonly HorrorLoadMetrics metrics;
        private bool disposed;
        public string Locale => manager?.CurrentLanguage;
        public LanguageProfile Profile => manager?.CurrentProfile;
        public event Action Changed;
        public HorrorTextService(LocalizationConfig config, HorrorLoadMetrics metrics)
        {
            this.metrics = metrics;
            manager = new LocalizationManager(config);
            manager.SetDataProvider(new ObservedResourcesProvider(config.resourcesBasePath, metrics));
        }
        public string Get(string key) => Profile == null ? key : manager.Get(key);
        public string Format(string key, params object[] args) => Profile == null ? key : manager.Get(key, args);
        public async Task ChangeAsync(string locale, CancellationToken cancellation = default)
        {
            if (disposed) throw new ObjectDisposedException(nameof(HorrorTextService));
            HorrorPreferences.Validate(manager.Config, locale);
            await Task.Yield();
            cancellation.ThrowIfCancellationRequested();
            if (disposed) throw new ObjectDisposedException(nameof(HorrorTextService));
            var timer = Stopwatch.StartNew();
            // All language caches, fallback, validation and state publication belong to the plugin.
            // This synchronous call cannot be cancelled midway; no fake rollback is layered over it.
            manager.SetLanguage(locale);
            metrics.lastTextPrepareMs = timer.Elapsed.TotalMilliseconds;
            Changed?.Invoke();
        }
        public void Dispose() { disposed = true; Changed = null; manager = null; }
    }
}
