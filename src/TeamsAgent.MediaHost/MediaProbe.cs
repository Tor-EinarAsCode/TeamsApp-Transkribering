using Microsoft.Graph.Communications.Calls;
using Microsoft.Graph.Communications.Calls.Media;
using Microsoft.Graph.Communications.Client;
using Microsoft.Graph.Models;
using Microsoft.Skype.Bots.Media;
using TeamsAgent.Core;
namespace TeamsAgent.MediaHost;
public record MediaFeasibilityResult(bool Joined, bool RecordingConfirmed, bool AudioReceived, string EvidencePath);
public sealed class MediaProbe(ICommunicationsClient client, RecordingConfirmation recording)
{
    public async Task<MediaFeasibilityResult> RunAsync(MeetingSession session, CancellationToken ct)
    {
        using var local = client.CreateMediaSession(new AudioSocketSettings { StreamDirections = StreamDirection.Recvonly, SupportedAudioFormat = AudioFormat.Pcm16K }, mediaSessionId: Guid.NewGuid());
        long audioBytes = 0; int confirmed = 0; ICall? call = null;
        local.AudioSocket.AudioMediaReceived += (_, e) => { try { if (Volatile.Read(ref confirmed) == 1) Interlocked.Add(ref audioBytes, e.Buffer.Length); } finally { e.Buffer.Dispose(); } };
        try
        {
            var join = new JoinMeetingParameters(new ChatInfo { ThreadId = session.Join.ThreadId, MessageId = "0" }, new OrganizerMeetingInfo { Organizer = new IdentitySet { User = new Identity { Id = session.OrganizerId.ToString() } } }, local) { TenantId = session.Key.TenantId.ToString() };
            call = await client.Calls().AddAsync(join, Guid.NewGuid());
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromMinutes(2));
            while (call.Resource.State != CallState.Established) { timeout.Token.ThrowIfCancellationRequested(); if (call.Resource.State == CallState.Terminated) throw new InvalidOperationException("Samtalen ble avsluttet før tilkobling."); await Task.Delay(500, timeout.Token); }
            var gate = new RecordingGate(); await recording.StartAsync(call.Id, gate, ct); Interlocked.Exchange(ref confirmed, gate.CanPersist ? 1 : 0);
            await Task.Delay(TimeSpan.FromSeconds(15), ct);
            return new(true, true, Interlocked.Read(ref audioBytes) > 0, "Lagre denne tekniske responsen sammen med dato og testmøte-ID; ingen lyd eller transkripsjon ble lagret.");
        }
        finally { Interlocked.Exchange(ref confirmed, 0); if (call is not null) { try { await recording.StopAsync(call.Id, default); } finally { await call.DeleteAsync(); } } }
    }
}
