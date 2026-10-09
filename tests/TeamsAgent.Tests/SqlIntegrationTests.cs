using TeamsAgent.Core;
using TeamsAgent.Persistence;
using Xunit;
namespace TeamsAgent.Tests;
public sealed class SqlFactAttribute : FactAttribute { public SqlFactAttribute() { if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TEAMSAGENT_TEST_SQL"))) Skip = "No local SQL Server connection configured; not verified."; } }
public class SqlIntegrationTests
{
    SqlStore Store(Guid tenant) => new(Environment.GetEnvironmentVariable("TEAMSAGENT_TEST_SQL")!, tenant);
    [SqlFact] public async Task DuplicateFinalSegmentIsStoredOnce() { var t = Guid.NewGuid(); var db = Store(t); await db.InitializeAsync(default); var key = new MeetingKey(t, Guid.NewGuid().ToString()); await db.UpdateAsync(key, _ => Task.FromResult(Session(key)), default); var seg = new TranscriptSegment("s", key, TimeSpan.Zero, TimeSpan.FromSeconds(1), "ukjent taler", "Hei"); await db.AppendAsync(seg, default); await db.AppendAsync(seg, default); Assert.Single(await db.ReadAsync(key, default)); }
    [SqlFact] public async Task DatabaseStateSurvivesRestart() { var t = Guid.NewGuid(); var db = Store(t); await db.InitializeAsync(default); var key = new MeetingKey(t, Guid.NewGuid().ToString()); await db.UpdateAsync(key, _ => Task.FromResult(Session(key)), default); Assert.NotNull(await Store(t).GetAsync(key, default)); }
    [SqlFact] public async Task ConcurrentDeliveryReservationIsUnique() { var t = Guid.NewGuid(); var db = Store(t); await db.InitializeAsync(default); var key = new MeetingKey(t, Guid.NewGuid().ToString()); await db.UpdateAsync(key, _ => Task.FromResult(Session(key)), default); var results = await Task.WhenAll(db.ReserveDeliveryAsync(key, "a@example.com", default), db.ReserveDeliveryAsync(key, "a@example.com", default)); Assert.Single(results, r => r is null); Assert.Single(results, r => r == DeliveryOutcome.Unknown); }
    [SqlFact] public async Task RetentionDeletesContentAndDeniesExpiredRead() { var t = Guid.NewGuid(); var db = Store(t); await db.InitializeAsync(default); var expired = new MeetingKey(t, Guid.NewGuid().ToString()); await db.UpdateAsync(expired, _ => Task.FromResult(Session(expired) with { Created = DateTimeOffset.UtcNow.AddDays(-31) }), default); Assert.Null(await db.GetAsync(expired, default)); Assert.True(await db.DeleteExpiredAsync(DateTimeOffset.UtcNow, default) > 0); }
    static MeetingSession Session(MeetingKey key) => new() { Key = key, OrganizerId = Guid.NewGuid(), Join = new("url", "thread", Guid.NewGuid()), State = MeetingState.Stopped, ReadyForFinalization = true };
}
