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
            var result = voice.ResolvePreparedAsset<AudioClip>(id);
            return result.Value ?? throw new InvalidOperationException("Voice is " + result.Status + ": " + id);
        }
        public HorrorVisualVariant Visual(string id)
        {
            if (IsDisposed) throw new ObjectDisposedException(nameof(HorrorWorldSession));
            var result = visual.ResolvePreparedAsset<HorrorVisualVariant>(id);
            return result.Value ?? throw new InvalidOperationException("Visual is " + result.Status + ": " + id);
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
            using var preparedVoice = voice.PrepareLanguage(voiceLocale, cancellationToken: cancellation);
            var visual = new LocalizationManager(definition.visualConfig);
            visual.SetDataProvider(new ObservedResourcesProvider(definition.visualConfig.resourcesBasePath, metrics));
            using var preparedVisual = visual.PrepareLanguage(visualLocale, cancellationToken: cancellation);
            voice.ApplyPreparedLanguage(preparedVoice, cancellation);
            visual.ApplyPreparedLanguage(preparedVisual, cancellation);
            // Resources preparation is synchronous. Task.Yield above is progress scheduling, not background I/O.
            // No audio decode, asset sweep, font warmup, or cache clearing. Playback reads prepared plugin data.
            metrics.lastWorldPrepareMs = timer.Elapsed.TotalMilliseconds;
            return new HorrorWorldSession(definition, voice, visual);
        }
    }
}

