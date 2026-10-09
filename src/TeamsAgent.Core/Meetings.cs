namespace TeamsAgent.Core;
public record MeetingKey(Guid TenantId, string MeetingId);
public enum MeetingState { Waiting, Transcribing, Stopped, Failed }
public record MeetingJoinDescriptor(string JoinUrl, string ThreadId, Guid OrganizerId);
public sealed record MeetingSession
{
    public required MeetingKey Key { get; init; }
    public required Guid OrganizerId { get; init; }
    public required MeetingJoinDescriptor Join { get; init; }
    public DateTimeOffset LastSeen { get; set; } = DateTimeOffset.UtcNow; public DateTimeOffset Created { get; init; } = DateTimeOffset.UtcNow; public MeetingState State { get; set; } = MeetingState.Waiting;
    public bool ReadyForFinalization { get; set; }
    public string? CallId { get; set; }
    public bool Incomplete { get; set; }
    public List<Attendee> Attendees { get; init; } = [];
}
public interface IMeetingConnector { Task<string> JoinAsync(MeetingSession session, CancellationToken ct); Task StopAsync(MeetingSession session, CancellationToken ct); }
public interface IMeetingStore { Task<MeetingSession?> GetAsync(MeetingKey key, CancellationToken ct); Task<MeetingSession> UpdateAsync(MeetingKey key, Func<MeetingSession?, Task<MeetingSession>> update, CancellationToken ct); }
public sealed class MeetingCoordinator(IMeetingStore store, IMeetingConnector connector, Guid tenant)
{
    public async Task<MeetingSession> StartAsync(MeetingKey key, Guid caller, MeetingJoinDescriptor join, CancellationToken ct)
    {
        TenantGuard.Require(tenant, key.TenantId); if (caller != join.OrganizerId || caller == Guid.Empty) throw new UnauthorizedAccessException();
        bool reserved = false;
        var session = await store.UpdateAsync(key, previous =>
        {
            if (previous is not null) { if (previous.OrganizerId != caller) throw new UnauthorizedAccessException(); if (Retention.IsExpired(previous.Created, DateTimeOffset.UtcNow)) throw new InvalidOperationException("Innholdet er utløpt."); return Task.FromResult(previous); }
            reserved = true; return Task.FromResult(new MeetingSession { Key = key, OrganizerId = caller, Join = join });
        }, ct);
        if (!reserved) return session;
        try { var callId = await connector.JoinAsync(session, ct); return await store.UpdateAsync(key, s => Task.FromResult(SessionCallbacks.Existing(s, DateTimeOffset.UtcNow) with { CallId = callId }), ct); }
        catch { await store.UpdateAsync(key, s => Task.FromResult(SessionCallbacks.Existing(s, DateTimeOffset.UtcNow) with { State = MeetingState.Failed, Incomplete = true, ReadyForFinalization = true }), CancellationToken.None); throw; }
    }
    public async Task StopAsync(MeetingKey key, Guid caller, CancellationToken ct)
    {
        TenantGuard.Require(tenant, key.TenantId); var session = await store.GetAsync(key, ct) ?? throw new KeyNotFoundException(); if (session.OrganizerId != caller) throw new UnauthorizedAccessException();
        if (session.State is MeetingState.Stopped or MeetingState.Failed) return;
        await connector.StopAsync(session, ct);
        await store.UpdateAsync(key, s => Task.FromResult(SessionCallbacks.Existing(s, DateTimeOffset.UtcNow) with { State = MeetingState.Stopped, Incomplete = true, ReadyForFinalization = true }), ct);
    }
    public async Task ApplyStateAsync(MeetingKey key, MeetingState state, CancellationToken ct)
    {
        TenantGuard.Require(tenant, key.TenantId);
        await store.UpdateAsync(key, previous => { var s = previous ?? throw new KeyNotFoundException(); if (s.State is not (MeetingState.Stopped or MeetingState.Failed)) s.State = state; return Task.FromResult(s); }, ct);
    }
}
public record TranscriptSegment(string Id, MeetingKey Meeting, TimeSpan Start, TimeSpan End, string Speaker, string Text);
public interface ITranscriptStore { Task AppendAsync(TranscriptSegment segment, CancellationToken ct); Task<IReadOnlyList<TranscriptSegment>> ReadAsync(MeetingKey key, CancellationToken ct); Task MarkGapAsync(MeetingKey key, TimeSpan offset, CancellationToken ct); }
public sealed class AudioPipeline
{
    readonly MeetingKey key; readonly RecordingGate gate; readonly ITranscriptStore store;
    public AudioPipeline(MeetingKey key, RecordingGate gate, ITranscriptStore store) { this.key = key; this.gate = gate; this.store = store; }
    public Task PersistAsync(TranscriptSegment segment, CancellationToken ct)
    {
        if (segment.Meeting != key) throw new UnauthorizedAccessException();
        if (!gate.CanPersist) return Task.CompletedTask;
        if (segment.End < segment.Start || string.IsNullOrWhiteSpace(segment.Text)) return Task.CompletedTask;
        return store.AppendAsync(segment with { Speaker = string.IsNullOrWhiteSpace(segment.Speaker) ? "ukjent taler" : segment.Speaker }, ct);
    }
}

public static class SessionRecovery { public static bool IsStale(MeetingSession session, DateTimeOffset now) => session.State is MeetingState.Waiting or MeetingState.Transcribing && session.LastSeen < now.AddMinutes(-5); }
