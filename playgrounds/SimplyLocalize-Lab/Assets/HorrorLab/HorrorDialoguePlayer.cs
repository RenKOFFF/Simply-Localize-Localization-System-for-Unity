using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace SimplyLocalize.HorrorLab
{
    public sealed class HorrorDialoguePlayer : IDisposable
    {
        private readonly AudioSource source;
        private HorrorWorldSession session;
        private readonly HorrorLoadMetrics metrics;
        private CancellationTokenSource playback;
        private int generation;
        public HorrorRadioSequence.Line CurrentLine { get; private set; }
        public AudioClip CurrentClip => source != null ? source.clip : null;
        public event Action Changed;
        public HorrorDialoguePlayer(AudioSource source, HorrorWorldSession session, HorrorLoadMetrics metrics)
        { this.source = source; this.session = session; this.metrics = metrics; }
        public async Task PlayAsync(string sequenceId, CancellationToken cancellation = default)
        {
            if (session == null) throw new ObjectDisposedException(nameof(HorrorDialoguePlayer));
            Stop(); int request = ++generation;
            var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellation); playback = operation;
            try
            {
                var sequence = session.Dialogue(sequenceId);
                foreach (var line in sequence.lines)
                {
                    operation.Token.ThrowIfCancellationRequested();
                    var clip = session.Clip(line.audioKey);
                    if (metrics != null) metrics.lastAudioStateBeforePlay = clip.loadState.ToString();
                    CurrentLine = line; source.clip = clip;
                    var timer = metrics == null ? null : Stopwatch.StartNew();
                    source.Play();
                    if (timer != null) metrics.lastFirstPlayCallMs = timer.Elapsed.TotalMilliseconds;
                    Changed?.Invoke();
                    double end = Time.realtimeSinceStartupAsDouble + clip.length + .35;
                    while (Time.realtimeSinceStartupAsDouble < end) { operation.Token.ThrowIfCancellationRequested(); await Task.Yield(); }
                }
            }
            finally
            {
                operation.Dispose();
                if (request == generation) { playback = null; source.Stop(); source.clip = null; CurrentLine = null; Changed?.Invoke(); }
            }
        }
        public void Stop()
        {
            generation++; playback?.Cancel(); playback = null;
            if (source != null) { source.Stop(); source.clip = null; }
            CurrentLine = null; Changed?.Invoke();
        }
        public void Dispose() { Stop(); Changed = null; session = null; }
    }
}
