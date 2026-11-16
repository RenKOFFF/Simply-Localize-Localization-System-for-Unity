using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace SimplyLocalize.HorrorLab
{
    public interface IHorrorWorldPreparer
    {
        Task<HorrorWorldSession> PrepareAsync(HorrorWorldDefinition definition, string voiceLocale, string visualLocale,
            CancellationToken cancellation, Action<string> progress);
    }

    /// <summary>Game context with independent plugin managers, not another resource cache/loader.</summary>
    public sealed class HorrorWorldSession : IDisposable
    {
        private LocalizationManager voice, visual;
        private HorrorWorldDefinition definition;
        public string VoiceLocale { get; }
        public string VisualLocale { get; }
        public bool IsDisposed { get; private set; }
        internal HorrorWorldSession(HorrorWorldDefinition definition, LocalizationManager voice, LocalizationManager visual)
        { this.definition = definition; this.voice = voice; this.visual = visual; VoiceLocale = voice.CurrentLanguage; VisualLocale = visual.CurrentLanguage; }
        public AudioClip Clip(string id)
        {
            if (IsDisposed) throw new ObjectDisposedException(nameof(HorrorWorldSession));
            return voice.GetAsset<AudioClip>(id) ?? throw new InvalidOperationException("Missing voice: " + id);
        }
        public HorrorVisualVariant Visual(string id)
        {
            if (IsDisposed) throw new ObjectDisposedException(nameof(HorrorWorldSession));
            return visual.GetAsset<HorrorVisualVariant>(id) ?? throw new InvalidOperationException("Missing visual: " + id);
        }
        public HorrorRadioSequence Dialogue(string id)
        {
            if (IsDisposed) throw new ObjectDisposedException(nameof(HorrorWorldSession));
            return definition.dialogues.FirstOrDefault(s => s.sequenceId == id) ?? throw new InvalidOperationException("Missing dialogue: " + id);
        }
        // Release game references only. Native resource/caching policy is not implemented here.
        public void Dispose() { IsDisposed = true; voice = visual = null; definition = null; }
    }

    public sealed class SimplyLocalizeWorldPreparer : IHorrorWorldPreparer
    {
        private readonly HorrorLoadMetrics metrics;
        public SimplyLocalizeWorldPreparer(HorrorLoadMetrics metrics) { this.metrics = metrics; }
        public async Task<HorrorWorldSession> PrepareAsync(HorrorWorldDefinition definition, string voiceLocale, string visualLocale,
            CancellationToken cancellation, Action<string> progress)
        {
            HorrorPreferences.Validate(definition.voiceConfig, voiceLocale);
            HorrorPreferences.Validate(definition.visualConfig, visualLocale);
            progress?.Invoke("UI/LoadingTables"); await Task.Yield();
            cancellation.ThrowIfCancellationRequested();
            var timer = Stopwatch.StartNew();
            var voice = new LocalizationManager(definition.voiceConfig);
            voice.SetDataProvider(new ObservedResourcesProvider(definition.voiceConfig.resourcesBasePath, metrics));
            voice.SetLanguage(voiceLocale);
            var visual = new LocalizationManager(definition.visualConfig);
            visual.SetDataProvider(new ObservedResourcesProvider(definition.visualConfig.resourcesBasePath, metrics));
            visual.SetLanguage(visualLocale);
            // No audio decode, asset sweep, font warmup, or cache clearing.
            // Consumers call the plugin when they actually need an asset.
            metrics.lastWorldPrepareMs = timer.Elapsed.TotalMilliseconds;
            return new HorrorWorldSession(definition, voice, visual);
        }
    }
}

