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
        private IDisposable voiceProvider, visualProvider;
        private HorrorWorldDefinition definition;
        public string VoiceLocale { get; }
        public string VisualLocale { get; }
        public bool IsDisposed { get; private set; }
        internal HorrorWorldSession(HorrorWorldDefinition definition, LocalizationManager voice, LocalizationManager visual,
            IDisposable voiceProvider, IDisposable visualProvider)
        { this.definition = definition; this.voice = voice; this.visual = visual; this.voiceProvider = voiceProvider; this.visualProvider = visualProvider; VoiceLocale = voice.CurrentLanguage; VisualLocale = visual.CurrentLanguage; }
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
        public void Dispose()
        {
            if (IsDisposed) return;
            IsDisposed = true; voiceProvider?.Dispose(); visualProvider?.Dispose();
            voiceProvider = visualProvider = null; voice = visual = null; definition = null;
        }
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
            var voiceProvider = new ObservedScopedResourcesProvider(definition.voiceConfig, "Radio", metrics);
            var visualProvider = new ObservedScopedResourcesProvider(definition.visualConfig, "Signs", metrics);
            try
            {
                var voice = new LocalizationManager(definition.voiceConfig); voice.SetDataProvider(voiceProvider);
                using var preparedVoice = await voice.PrepareLanguageAsync(voiceLocale, cancellationToken: cancellation);
                var visual = new LocalizationManager(definition.visualConfig); visual.SetDataProvider(visualProvider);
                using var preparedVisual = await visual.PrepareLanguageAsync(visualLocale, cancellationToken: cancellation);
                cancellation.ThrowIfCancellationRequested();
                voice.ApplyPreparedLanguage(preparedVoice, cancellation);
                visual.ApplyPreparedLanguage(preparedVisual, cancellation);
                metrics.lastWorldPrepareMs = timer.Elapsed.TotalMilliseconds;
                return new HorrorWorldSession(definition, voice, visual, voiceProvider, visualProvider);
            }
            catch { voiceProvider.Dispose(); visualProvider.Dispose(); throw; }
        }
    }
}

