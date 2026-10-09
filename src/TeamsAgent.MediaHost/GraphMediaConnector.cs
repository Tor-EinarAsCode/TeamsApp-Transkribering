using System.Collections.Concurrent;
using System.Net;
using System.Runtime.InteropServices;
using Microsoft.Graph.Models;
using Microsoft.Graph.Communications.Calls;
using Microsoft.Graph.Communications.Calls.Media;
using Microsoft.Graph.Communications.Client;
using Microsoft.Graph.Communications.Common.Telemetry;
using Microsoft.Skype.Bots.Media;
using TeamsAgent.Core;
using TeamsAgent.Persistence;
using TeamsAgent.Worker;
namespace TeamsAgent.MediaHost;
public sealed class GraphMediaConnector : IMeetingConnector
{
    public ICommunicationsClient Client { get; }
    public GraphAuthentication Authentication { get; }
    public RecordingConfirmation Recording { get; }
    readonly SqlStore store; readonly IConfiguration config; readonly ILogger<GraphMediaConnector> logger;
    readonly ConcurrentDictionary<string, CallRuntime> calls = [];
    public GraphMediaConnector(SqlStore store, IConfiguration config, ILogger<GraphMediaConnector> logger)
    {
        this.store = store; this.config = config; this.logger = logger;
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Teams-møtelyd krever Windows Server.");
        var appId = config["ClientId"]!; var builder = new CommunicationsClientBuilder("Intern møteagent", appId, new GraphLogger());
        Authentication = new GraphAuthentication(config["TenantId"]!, appId, config["ClientSecret"]!); builder.SetAuthentication(appId, Authentication);
        Recording = new(new GraphRecordingTransport(new HttpClient(), _ => Authentication.AcquireTokenAsync(config["TenantId"]!)));
        builder.SetNotificationUrl(new Uri(config["Media:NotificationUrl"]!)); builder.SetServiceBaseUrl(new Uri("https://graph.microsoft.com/v1.0"));
        builder.SetMediaPlatformSettings(new MediaPlatformSettings { ApplicationId = appId, MediaPlatformInstanceSettings = new MediaPlatformInstanceSettings { CertificateThumbprint = config["Media:CertificateThumbprint"]!, InstanceInternalPort = config.GetValue<int>("Media:Port", 8445), InstancePublicPort = config.GetValue<int>("Media:Port", 8445), InstancePublicIPAddress = IPAddress.Parse(config["Media:PublicIp"]!), ServiceFqdn = config["Media:Fqdn"]! } });
        Client = builder.Build();
    }
    public async Task<string> JoinAsync(MeetingSession session, CancellationToken ct)
    {
        TenantGuard.Require(Guid.Parse(config["TenantId"]!), session.Key.TenantId);
        if (!config.GetValue<bool>("Media:RecordingPolicyVerified")) throw new InvalidOperationException("Opptakspolicy er ikke verifisert. Kjør gjennomførbarhetstesten først.");
        var gate = new RecordingGate(); var local = Client.CreateMediaSession(new AudioSocketSettings { StreamDirections = StreamDirection.Recvonly, SupportedAudioFormat = AudioFormat.Pcm16K }, mediaSessionId: Guid.NewGuid());
        var speech = new SpeechStream(session.Key, gate, store, config["Speech:Key"]!, config["Speech:Region"]!);
        local.AudioSocket.AudioMediaReceived += (_, e) => { try { if (gate.CanPersist) { var pcm = new byte[e.Buffer.Length]; Marshal.Copy(e.Buffer.Data, pcm, 0, pcm.Length); speech.Receive(pcm); } } finally { e.Buffer.Dispose(); } };
        var join = new JoinMeetingParameters(new ChatInfo { ThreadId = session.Join.ThreadId, MessageId = "0" }, new OrganizerMeetingInfo { Organizer = new IdentitySet { User = new Identity { Id = session.OrganizerId.ToString() } } }, local) { TenantId = session.Key.TenantId.ToString() };
        try
        {
            var call = await Client.Calls().AddAsync(join, Guid.NewGuid()); var r = new CallRuntime(call, local, gate, speech, session); calls[call.Id] = r; speech.Faulted += () => { _ = FinishAsync(r, false, true); };
            r.Heartbeat = new Timer(timerState => { _ = HeartbeatAsync(r); }, null, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(15));
            call.OnUpdated += (sender, e) => { _ = UpdateAsync(r, e.NewResource.State); };
            call.Participants.OnUpdated += (sender, e) => { foreach (var participant in e.AddedResources) ObserveParticipant(r, participant); };
            foreach (var participant in call.Participants) ObserveParticipant(r, participant);
            if (call.Resource.State == CallState.Established) _ = UpdateAsync(r, CallState.Established);
            return call.Id;
        }
        catch { gate.Revoke(); try { await speech.DisposeAsync(); } finally { local.Dispose(); } throw; }
    }
    void ObserveParticipant(CallRuntime r, IParticipant participant)
    {
        if (r.Lifecycle.Closing) return;
        CaptureParticipant(r, participant.Id, participant.Resource);
        participant.OnUpdated += (sender, e) => CaptureParticipant(r, participant.Id, e.NewResource);
    }
    void CaptureParticipant(CallRuntime r, string id, Participant participant)
    {
        if (r.Lifecycle.Closing) return; var identity = participant.Info?.Identity;
        if (identity?.Application is not null) return;
        bool attended = Attendance.ActuallyAttended(participant.IsInLobby, false);
        r.Roster.Add(async () => { await store.UpdateAsync(r.Session.Key, s => { var v = SessionCallbacks.Existing(s, DateTimeOffset.UtcNow); var old = v.Attendees.FirstOrDefault(a => a.ParticipantId == id); v.Attendees.RemoveAll(a => a.ParticipantId == id); v.Attendees.Add(new(id, null, null, attended || old?.ActuallyAttended == true, identity?.User?.Id)); return Task.FromResult(v); }, default); });
    }
    async Task HeartbeatAsync(CallRuntime r) { if (r.Lifecycle.Closing) return; try { await store.UpdateAsync(r.Session.Key, s => { var v = SessionCallbacks.Existing(s, DateTimeOffset.UtcNow); if (v.State is MeetingState.Stopped or MeetingState.Failed) throw new InvalidOperationException("Møtet er avsluttet."); return Task.FromResult(v with { LastSeen = DateTimeOffset.UtcNow }); }, default); } catch { r.Gate.Revoke(); await FinishAsync(r, false, true); } }
    async Task UpdateAsync(CallRuntime r, CallState? state)
    {
        try
        {
            if (state == CallState.Established) await r.Lifecycle.StartAsync(async ct =>
            {
                var session = SessionCallbacks.Existing(await store.GetAsync(r.Session.Key, ct), DateTimeOffset.UtcNow);
                if (session.State is MeetingState.Stopped or MeetingState.Failed) throw new InvalidOperationException("Møtet er avsluttet.");
                await Recording.StartAsync(r.Call.Id, r.Gate, ct); ct.ThrowIfCancellationRequested(); await r.Speech.StartAsync();
                await store.UpdateAsync(r.Session.Key, s => { var v = SessionCallbacks.Existing(s, DateTimeOffset.UtcNow); if (v.State is MeetingState.Stopped or MeetingState.Failed) throw new InvalidOperationException(); v.State = MeetingState.Transcribing; return Task.FromResult(v); }, ct);
            });
            if (state == CallState.Terminated) await FinishAsync(r, false, false);
        }
        catch { await FinishAsync(r, false, true); }
    }
    Task FinishAsync(CallRuntime r, bool userStop, bool failed) => r.Lifecycle.FinishAsync(async () =>
    {
        r.Heartbeat?.Dispose(); bool incomplete = failed || userStop; if (failed) r.Gate.Revoke();
        try
        {
            try { await r.Speech.CompleteAsync(); } catch { incomplete = true; r.Gate.Revoke(); }
            try { await r.Roster.SealAndDrainAsync(); } catch { incomplete = true; }
            r.Gate.Revoke();
            if (r.Call.Resource.State != CallState.Terminated) { try { await Recording.StopAsync(r.Call.Id, default); } catch { incomplete = true; } try { await r.Call.DeleteAsync(); } catch { incomplete = true; } }
        }
        finally
        {
            r.Gate.Revoke(); try { await r.Speech.DisposeAsync(); } catch { incomplete = true; }
            r.Local.Dispose(); calls.TryRemove(r.Call.Id, out _);
        }
        try { await store.UpdateAsync(r.Session.Key, s => { var v = SessionCallbacks.Existing(s, DateTimeOffset.UtcNow); return Task.FromResult(v with { State = failed ? MeetingState.Failed : MeetingState.Stopped, Incomplete = v.Incomplete || incomplete, ReadyForFinalization = true }); }, default); }
        catch (Exception ex) { logger.LogWarning("Avslutning kunne ikke oppdatere møtestatus: {ErrorType}", ex.GetType().Name); }
    });
    public async Task StopAsync(MeetingSession s, CancellationToken ct) { if (s.CallId is not null && calls.TryGetValue(s.CallId, out var r)) await FinishAsync(r, true, false); else throw new InvalidOperationException("Aktiv samtale finnes ikke på denne medieverten."); }
    sealed class CallRuntime(ICall call, ILocalMediaSession local, RecordingGate gate, SpeechStream speech, MeetingSession session) { public ICall Call = call; public ILocalMediaSession Local = local; public RecordingGate Gate = gate; public SpeechStream Speech = speech; public MeetingSession Session = session; public RuntimeLifecycle Lifecycle = new(); public AsyncResultDrain Roster = new(); public Timer? Heartbeat; }
}
