namespace TeamsAgent.Core;
public sealed class RuntimeLifecycle
{
    readonly SemaphoreSlim mutex = new(1); readonly CancellationTokenSource lifetime = new(); readonly object sync = new(); Task? finishTask; int closing; bool started;
    public bool Closing => Volatile.Read(ref closing) != 0;
    public async Task StartAsync(Func<CancellationToken, Task> start) { await mutex.WaitAsync(); try { if (Closing || started) return; started = true; await start(lifetime.Token); lifetime.Token.ThrowIfCancellationRequested(); } finally { mutex.Release(); } }
    public Task FinishAsync(Func<Task> finish) { lock (sync) { if (finishTask is not null) return finishTask; Interlocked.Exchange(ref closing, 1); lifetime.Cancel(); finishTask = RunFinish(finish); return finishTask; } }
    async Task RunFinish(Func<Task> finish) { await mutex.WaitAsync(); try { await finish(); } finally { mutex.Release(); } }
}
public sealed class AsyncResultDrain
{
    readonly object sync = new(); readonly List<Task> tasks = []; bool sealedForWrites;
    public bool Add(Func<Task> write) { lock (sync) { if (sealedForWrites) return false; try { tasks.Add(write()); } catch (Exception ex) { tasks.Add(Task.FromException(ex)); } return true; } }
    public Task SealAndDrainAsync() { lock (sync) { sealedForWrites = true; return Task.WhenAll(tasks.ToArray()); } }
}
public sealed class RecognitionCompletion
{
    readonly TaskCompletionSource ended = new(TaskCreationOptions.RunContinuationsAsynchronously); readonly AsyncResultDrain writes = new();
    public bool Add(Func<Task> write) => writes.Add(write);
    public void End() => ended.TrySetResult();
    public async Task CompleteAsync(CancellationToken ct) { await ended.Task.WaitAsync(ct); await writes.SealAndDrainAsync().WaitAsync(ct); }
}
