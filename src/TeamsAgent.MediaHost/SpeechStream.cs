using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using TeamsAgent.Core;
namespace TeamsAgent.MediaHost;
public sealed class SpeechStream : IAsyncDisposable
{
    public event Action? Faulted;
    readonly BoundedAudioBuffer queue = new(256); readonly RecognitionCompletion completion = new();
    readonly PushAudioInputStream input; readonly AudioConfig audio; readonly SpeechRecognizer recognizer;
    readonly RecordingGate gate; readonly MeetingKey key; readonly ITranscriptStore store; readonly AudioPipeline pipeline;
    readonly object sync = new(); Task? pump; Task? completeTask; int started; int stopped; long bytes;
    public SpeechStream(MeetingKey key, RecordingGate gate, ITranscriptStore store, string speechKey, string region)
    {
        this.key = key; this.gate = gate; this.store = store; pipeline = new(key, gate, store);
        input = AudioInputStream.CreatePushStream(AudioStreamFormat.GetWaveFormatPCM(16000, 16, 1)); audio = AudioConfig.FromStreamInput(input);
        var config = SpeechConfig.FromSubscription(speechKey, region); config.SpeechRecognitionLanguage = "nb-NO"; recognizer = new SpeechRecognizer(config, audio);
        recognizer.Recognized += (_, e) => { if (e.Result.Reason != ResultReason.RecognizedSpeech) return; var start = TimeSpan.FromTicks((long)e.Result.OffsetInTicks); completion.Add(() => pipeline.PersistAsync(new(e.Result.ResultId, key, start, start + e.Result.Duration, "ukjent taler", e.Result.Text), default)); };
        recognizer.Canceled += (_, e) => { if (e.Reason == CancellationReason.Error) { completion.Add(() => store.MarkGapAsync(key, Offset(), default)); completion.End(); Faulted?.Invoke(); } else completion.End(); };
        recognizer.SessionStopped += (_, _) => completion.End();
    }
    TimeSpan Offset() => TimeSpan.FromSeconds(Interlocked.Read(ref bytes) / 32000d);
    public async Task StartAsync() { Interlocked.Exchange(ref started, 1); await recognizer.StartContinuousRecognitionAsync(); pump = Task.Run(async () => { await foreach (var pcm in queue.ReadAllAsync()) { input.Write(pcm); Array.Clear(pcm); } }); }
    public bool Receive(byte[] pcm) { if (!gate.CanPersist || Volatile.Read(ref stopped) != 0) { Array.Clear(pcm); return false; } Interlocked.Add(ref bytes, pcm.Length); if (queue.TryWrite(pcm)) return true; completion.Add(() => store.MarkGapAsync(key, Offset(), default)); return false; }
    public Task CompleteAsync() { lock (sync) { return completeTask ??= CompleteCore(); } }
    async Task CompleteCore()
    {
        Interlocked.Exchange(ref stopped, 1); queue.Complete(); if (pump is not null) await pump; input.Close();
        if (Volatile.Read(ref started) == 0) { completion.End(); await completion.CompleteAsync(default); return; }
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await completion.CompleteAsync(timeout.Token); }
        catch (OperationCanceledException) { gate.Revoke(); await store.MarkGapAsync(key, Offset(), default); throw new InvalidOperationException("De siste talesegmentene kunne ikke bekreftes."); }
        // EOF/SessionStopped has already occurred and all final result writes are drained.
        await recognizer.StopContinuousRecognitionAsync();
    }
    public async ValueTask DisposeAsync() { try { await CompleteAsync(); } finally { gate.Revoke(); recognizer.Dispose(); audio.Dispose(); input.Dispose(); } }
}
