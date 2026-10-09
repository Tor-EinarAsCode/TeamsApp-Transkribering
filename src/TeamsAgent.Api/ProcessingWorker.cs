using TeamsAgent.Core;
using TeamsAgent.Persistence;
using TeamsAgent.Worker;
namespace TeamsAgent.Api;
public sealed class ProcessingWorker(IServiceProvider services, IConfiguration config, ILogger<ProcessingWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!config.GetValue<bool>("Jobs:Enabled")) return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(20));
        do
        {
            try
            {
                using var scope = services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<SqlStore>();
                await new RetentionJob(db).RunAsync(DateTimeOffset.UtcNow, ct);
                foreach (var active in await db.ActiveAsync(ct)) { if (SessionRecovery.IsStale(active, DateTimeOffset.UtcNow)) await db.UpdateAsync(active.Key, s => Task.FromResult(s is not null && !SessionRecovery.IsStale(s, DateTimeOffset.UtcNow) ? s : SessionCallbacks.Existing(s, DateTimeOffset.UtcNow) with { State = MeetingState.Failed, Incomplete = true, ReadyForFinalization = true }), ct); }
                foreach (var key in await db.PendingAsync(ct))
                {
                    try
                    {
                        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromMinutes(9)); var finalize = scope.ServiceProvider.GetRequiredService<FinalizeMeetingJob>(); await finalize.RunAsync(key, deadline.Token);
                        if (await db.GetMinutesAsync(key, ct) is null) continue;
                        await new DeliveryBatch(db).RunAsync(key, async deliveryToken =>
                        {
                            var graph = scope.ServiceProvider.GetRequiredService<GraphClient>();
                            var session = await db.GetAsync(key, deliveryToken);
                            if (session is not null) { var resolved = new List<Attendee>(); foreach (var attendee in session.Attendees) { var email = attendee.DirectoryObjectId is null ? attendee.VerifiedEmail : await graph.VerifyInternalEmailAsync(attendee.DirectoryObjectId, deliveryToken); resolved.Add(attendee.DirectoryObjectId is null ? attendee : attendee with { TenantId = email is null ? null : key.TenantId, VerifiedEmail = email }); } await db.UpdateAsync(key, s => Task.FromResult(SessionCallbacks.Existing(s, DateTimeOffset.UtcNow) with { Attendees = resolved }), deliveryToken); }
                            var report = await scope.ServiceProvider.GetRequiredService<DeliverMinutesJob>().RunAsync(key, deliveryToken);
                            var s = await db.GetAsync(key, deliveryToken); if (s is not null && await db.ReserveDeliveryAsync(key, "@organizer-status", deliveryToken) is null) { await scope.ServiceProvider.GetRequiredService<MinutesSender>().NotifyOrganizerAsync(s, report, deliveryToken); await db.CompleteDeliveryAsync(key, "@organizer-status", DeliveryOutcome.Sent, deliveryToken); }
                            await db.MarkDeliveredAsync(key, report, deliveryToken);
                        }, ct);
                    }
                    catch (Exception ex) when (!ct.IsCancellationRequested) { logger.LogWarning("Møtejobb feilet: {ErrorType}", ex.GetType().Name); try { var failed = await db.GetAsync(key, ct); if (failed is not null && await db.ReserveDeliveryAsync(key, "@organizer-error", ct) is null) { await scope.ServiceProvider.GetRequiredService<MinutesSender>().NotifyProblemAsync(failed, ct); await db.CompleteDeliveryAsync(key, "@organizer-error", DeliveryOutcome.Sent, ct); } } catch (Exception notifyError) when (!ct.IsCancellationRequested) { logger.LogWarning("Arrangørvarsel feilet: {ErrorType}", notifyError.GetType().Name); } }
                }
            }
            catch (Exception ex) when (!ct.IsCancellationRequested) { logger.LogWarning("Bakgrunnsbehandling feilet: {ErrorType}", ex.GetType().Name); }
        } while (await timer.WaitForNextTickAsync(ct));
    }
}
