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

    /// <summary>A world's registrations in the app runtime. Presentation detaches before these registrations close.</summary>
    public sealed class HorrorWorldSession : IDisposable
    {
        private LocalizationRuntime runtime;
        private readonly string voiceChannel, visualChannel;
        private readonly LocalizationSetLease voiceSet, visualSet;
        private Task release;
        private HorrorWorldDefinition definition;
        public string VoiceLocale { get; }
        public string VisualLocale { get; }
        public bool IsDisposed { get; private set; }
        internal HorrorWorldSession(HorrorWorldDefinition definition, LocalizationRuntime runtime, string voiceChannel, string visualChannel,
            LocalizationSetLease voiceSet, LocalizationSetLease visualSet)
        {
            this.definition = definition; this.runtime = runtime; this.voiceChannel = voiceChannel; this.visualChannel = visualChannel;
            this.voiceSet = voiceSet; this.visualSet = visualSet;
            VoiceLocale = runtime.GetChannel(voiceChannel).RequestedLanguage; VisualLocale = runtime.GetChannel(visualChannel).RequestedLanguage;
        }
        public AudioClip Clip(string id)
        {
            if (IsDisposed) throw new ObjectDisposedException(nameof(HorrorWorldSession));
            return runtime.GetAsset<AudioClip>(id, voiceChannel, definition.voiceSetId) ?? throw new InvalidOperationException("Missing voice: " + id);
        }
        public HorrorVisualVariant Visual(string id)
        {
            if (IsDisposed) throw new ObjectDisposedException(nameof(HorrorWorldSession));
            return runtime.GetAsset<HorrorVisualVariant>(id, visualChannel, definition.visualSetId) ?? throw new InvalidOperationException("Missing visual: " + id);
        }
        public HorrorRadioSequence Dialogue(string id)
        {
            if (IsDisposed) throw new ObjectDisposedException(nameof(HorrorWorldSession));
            return definition.dialogues.FirstOrDefault(s => s.sequenceId == id) ?? throw new InvalidOperationException("Missing dialogue: " + id);
        }
        public Task ReleaseAsync()
        {
            if (release != null) return release;
            IsDisposed = true; definition = null; runtime = null;
            return release = Task.WhenAll(voiceSet.ReleaseAsync(), visualSet.ReleaseAsync());
        }
        public void Dispose() { var cleanup = ReleaseAsync(); if (!cleanup.IsCompletedSuccessfully) Observe(cleanup); }
        private static async void Observe(Task task) { try { await task; } catch (Exception error) { UnityEngine.Debug.LogException(error); } }
    }

    public sealed class SimplyLocalizeWorldPreparer : IHorrorWorldPreparer
    {
        private readonly LocalizationRuntime runtime;
        private readonly string voiceChannel, visualChannel;
        private readonly HorrorLoadMetrics metrics;
        public SimplyLocalizeWorldPreparer(LocalizationRuntime runtime, string voiceChannel, string visualChannel, HorrorLoadMetrics metrics)
        { this.runtime = runtime; this.voiceChannel = voiceChannel; this.visualChannel = visualChannel; this.metrics = metrics; }
        public async Task<HorrorWorldSession> PrepareAsync(HorrorWorldDefinition definition, string voiceLocale, string visualLocale,
            CancellationToken cancellation, Action<string> progress)
        {
            if (!runtime.GetAvailableLanguages(voiceChannel).Contains(voiceLocale) || !runtime.GetAvailableLanguages(visualChannel).Contains(visualLocale))
                throw new ArgumentException("Unsupported world locale.");
            if (runtime.GetChannel(voiceChannel).ActiveSetIds.Count != 0 || runtime.GetChannel(visualChannel).ActiveSetIds.Count != 0)
                throw new InvalidOperationException("Close the active world before preparing another world.");
            progress?.Invoke("UI/LoadingTables"); cancellation.ThrowIfCancellationRequested();
            var timer = metrics == null ? null : Stopwatch.StartNew();
            LocalizationSetLease voice = null, visual = null;
            try
            {
                // Empty channels can choose a language without loading the world's content.
                await runtime.SwitchLanguageAsync(voiceChannel, voiceLocale, cancellation);
                await runtime.SwitchLanguageAsync(visualChannel, visualLocale, cancellation);
                voice = await runtime.AcquireSetAsync(voiceChannel, definition.voiceSetId, cancellation);
                visual = await runtime.AcquireSetAsync(visualChannel, definition.visualSetId, cancellation);
                cancellation.ThrowIfCancellationRequested();
                if (timer != null) metrics.lastWorldPrepareMs = timer.Elapsed.TotalMilliseconds;
                return new HorrorWorldSession(definition, runtime, voiceChannel, visualChannel, voice, visual);
            }
            catch (Exception error)
            {
                try { await Task.WhenAll(voice?.ReleaseAsync() ?? Task.CompletedTask, visual?.ReleaseAsync() ?? Task.CompletedTask); }
                catch (Exception cleanup) { throw new AggregateException("World preparation and release failed.", error, cleanup); }
                throw;
            }
        }
    }
}
