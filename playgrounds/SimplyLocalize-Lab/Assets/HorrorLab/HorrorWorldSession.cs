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
        private LocalizationContext voice, visual;
        private readonly HorrorLoadMetrics metrics;
        private Task release;
        private HorrorWorldDefinition definition;
        public string VoiceLocale { get; }
        public string VisualLocale { get; }
        public bool IsDisposed { get; private set; }
        internal HorrorWorldSession(HorrorWorldDefinition definition, LocalizationContext voice, LocalizationContext visual, HorrorLoadMetrics metrics)
        { this.definition = definition; this.voice = voice; this.visual = visual; this.metrics = metrics; VoiceLocale = voice.CurrentLanguage; VisualLocale = visual.CurrentLanguage; }
        public AudioClip Clip(string id)
        {
            if (IsDisposed) throw new ObjectDisposedException(nameof(HorrorWorldSession));
            var result = voice.ResolveAsset<AudioClip>(id);
            return result.Value ?? throw new InvalidOperationException("Voice is " + result.Status + ": " + id);
        }
        public HorrorVisualVariant Visual(string id)
        {
            if (IsDisposed) throw new ObjectDisposedException(nameof(HorrorWorldSession));
            var result = visual.ResolveAsset<HorrorVisualVariant>(id);
            return result.Value ?? throw new InvalidOperationException("Visual is " + result.Status + ": " + id);
        }
        public HorrorRadioSequence Dialogue(string id)
        {
            if (IsDisposed) throw new ObjectDisposedException(nameof(HorrorWorldSession));
            return definition.dialogues.FirstOrDefault(s => s.sequenceId == id) ?? throw new InvalidOperationException("Missing dialogue: " + id);
        }
        public Task ReleaseAsync()
        {
            if (release != null) return release;
            IsDisposed = true; definition = null;
            var oldVoice = voice; var oldVisual = visual; voice = visual = null;
            return release = ReleaseContexts(oldVoice, oldVisual);
        }
        private async Task ReleaseContexts(LocalizationContext oldVoice, LocalizationContext oldVisual)
        {
            try { await Task.WhenAll(oldVoice.ReleaseAsync(), oldVisual.ReleaseAsync()); }
            finally { metrics.Finish(oldVoice); metrics.Finish(oldVisual); }
        }
        public void Dispose() { var cleanup = ReleaseAsync(); if (!cleanup.IsCompletedSuccessfully) Observe(cleanup); }
        private static async void Observe(Task task) { try { await task; } catch (Exception error) { UnityEngine.Debug.LogException(error); } }
    }

    public sealed class SimplyLocalizeWorldPreparer : IHorrorWorldPreparer
    {
        private static LocalizationContext Context(LocalizationConfig config, string table) => new(config,
            config.languages.Where(p => p != null).Select(p => new SimplyLocalize.Data.ResourcesLanguageSources(p.Code,
                assetTables: new[] { config.resourcesBasePath + "/" + p.Code + "/" + table })),
            new LocalizationContextOptions { Ownership = LocalizationResourceOwnership.Owned, PrepareAudio = true });
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
            var voice = Context(definition.voiceConfig, "Radio");
            var visual = Context(definition.visualConfig, "Signs");
            metrics.Track(voice); metrics.Track(visual);
            try
            {
                await voice.ChangeLanguageAsync(voiceLocale, cancellation);
                await visual.ChangeLanguageAsync(visualLocale, cancellation);
                cancellation.ThrowIfCancellationRequested();
                metrics.lastWorldPrepareMs = timer.Elapsed.TotalMilliseconds;
                return new HorrorWorldSession(definition, voice, visual, metrics);
            }
            catch
            {
                try { await Task.WhenAll(voice.ReleaseAsync(), visual.ReleaseAsync()); }
                finally { metrics.Finish(voice); metrics.Finish(visual); }
                throw;
            }
        }
    }
}

