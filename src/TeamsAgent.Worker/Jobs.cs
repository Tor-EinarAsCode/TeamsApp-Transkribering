using TeamsAgent.Core;
namespace TeamsAgent.Worker;
public sealed class FinalizeMeetingJob(IMeetingStore meetings, ITranscriptStore transcripts, IContentStore content, IMinutesGenerator generator, Guid tenant)
{
    public async Task<MeetingMinutes?> RunAsync(MeetingKey key, CancellationToken ct)
    {
        TenantGuard.Require(tenant, key.TenantId); var session = await meetings.GetAsync(key, ct) ?? throw new KeyNotFoundException();
        if (Retention.IsExpired(session.Created, DateTimeOffset.UtcNow)) throw new InvalidOperationException("Utløpt møte.");
        if (!session.ReadyForFinalization || session.State is not (MeetingState.Stopped or MeetingState.Failed)) throw new InvalidOperationException("Møtet er ikke avsluttet.");
        var existing = await content.GetMinutesAsync(key, ct); if (existing is not null) return existing;
        var owner = Guid.NewGuid().ToString(); if (!await content.TryLeaseAsync(key, owner, ct)) return null;
        try
        {
            existing = await content.GetMinutesAsync(key, ct); if (existing is not null) return existing;
            var segments = await transcripts.ReadAsync(key, ct);
            var result = segments.Count == 0 ? new MeetingMinutes("Ingen tale ble fanget. Referat kunne ikke lages.", [], [], true, "Ingen tale fanget") : MinutesValidator.Validate(await generator.GenerateAsync(segments, session.Incomplete, ct), segments, session.Incomplete);
            await content.SaveMinutesAsync(key, result, ct); return result;
        }
        finally { await content.ReleaseAsync(key, owner, CancellationToken.None); }
    }
}
public sealed class DeliverMinutesJob(IMeetingStore meetings, IContentStore content, IMinutesSender sender, Guid tenant)
{
    public async Task<DeliveryReport> RunAsync(MeetingKey key, CancellationToken ct)
    {
        TenantGuard.Require(tenant, key.TenantId); var s = await meetings.GetAsync(key, ct) ?? throw new KeyNotFoundException();
        if (Retention.IsExpired(s.Created, DateTimeOffset.UtcNow)) throw new InvalidOperationException("Utløpt møte.");
        var m = await content.GetMinutesAsync(key, ct) ?? throw new InvalidOperationException("Referatet er ikke klart.");
        var recipients = RecipientResolver.Resolve(tenant, s.Attendees); var outcomes = new Dictionary<string, DeliveryOutcome>();
        async Task<DeliveryOutcome> Send(string recipient)
        {
            var prior = await content.ReserveDeliveryAsync(key, recipient, ct); if (prior is not null) return prior.Value;
            DeliveryOutcome result; try { result = await sender.SendAsync(s, m, recipient, ct); } catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { result = DeliveryOutcome.Unknown; }
            await content.CompleteDeliveryAsync(key, recipient, result, CancellationToken.None); return result;
        }
        foreach (var email in recipients.Emails) outcomes[email] = await Send(email);
        var chat = await Send("@meeting-chat"); return new(outcomes, chat, recipients.Exclusions);
    }
}
public sealed class RetentionJob(IContentStore content) { public Task<int> RunAsync(DateTimeOffset now, CancellationToken ct) => content.DeleteExpiredAsync(now, ct); }
public sealed class DeliveryBatch(IContentStore content)
{
    public async Task<bool> RunAsync(MeetingKey key, Func<CancellationToken, Task> deliverAndReport, CancellationToken ct) { var owner = Guid.NewGuid().ToString(); if (!await content.TryLeaseAsync(key, owner, ct)) return false; try { using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromMinutes(9)); await deliverAndReport(deadline.Token); return true; } finally { await content.ReleaseAsync(key, owner, CancellationToken.None); } }
}
