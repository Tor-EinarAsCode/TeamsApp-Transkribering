using System.Threading.Channels;
namespace TeamsAgent.Core;
public sealed class BoundedAudioBuffer
{
    readonly Channel<byte[]> channel;
    public BoundedAudioBuffer(int capacity) { channel = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(capacity) { SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.Wait }); }
    public bool TryWrite(byte[] pcm) { if (channel.Writer.TryWrite(pcm)) return true; Array.Clear(pcm); return false; }
    public IAsyncEnumerable<byte[]> ReadAllAsync() => channel.Reader.ReadAllAsync();
    public void Complete() => channel.Writer.TryComplete();
}
