using TeamsAgent.Core;
using Xunit;
namespace TeamsAgent.Tests;
public class BoundedAudioTests
{
    [Fact] public void QueueOverflowRejectsAndClearsAudio() { var q = new BoundedAudioBuffer(1); Assert.True(q.TryWrite([1, 2])); byte[] rejected = [3, 4]; Assert.False(q.TryWrite(rejected)); Assert.All(rejected, b => Assert.Equal(0, b)); }
    [Fact] public async Task QueuedPcmIsDrainedBeforeCompletion() { var q = new BoundedAudioBuffer(2); q.TryWrite([1]); q.TryWrite([2]); q.Complete(); var bytes = new List<byte>(); await foreach (var pcm in q.ReadAllAsync()) bytes.AddRange(pcm); Assert.Equal(new byte[] { 1, 2 }, bytes); }
    [Fact] public void StoppedQueueAcceptsNothing() { var q = new BoundedAudioBuffer(2); q.Complete(); byte[] b = [1]; Assert.False(q.TryWrite(b)); Assert.Equal(0, b[0]); }
}
