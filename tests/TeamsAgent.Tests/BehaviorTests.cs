using Xunit;
using TeamsAgent.Core;
namespace TeamsAgent.Tests;
public class BehaviorTests
{
    readonly Guid tenant = Guid.NewGuid();
    [Fact] public void ForeignTenantIsRejected() => Assert.Throws<UnauthorizedAccessException>(() => TenantGuard.Require(tenant, Guid.NewGuid()));
    [Fact] public void OrdinaryMessageGetsNoReply() => Assert.False(MentionRouter.IsInvoked("bot", []));
    [Fact] public void AnotherBotMentionGetsNoReply() => Assert.False(MentionRouter.IsInvoked("bot", [new Mention("other")]));
    [Fact] public void DirectMentionGetsHelp() => Assert.True(MentionRouter.IsInvoked("bot", [new Mention("bot")]));
    [Fact] public void UnconfirmedRecordingPersistsNothing() { var gate = new RecordingGate(); Assert.False(gate.CanPersist); }
    [Fact] public void LostRecordingStateStopsPersistence() { var gate = new RecordingGate(); gate.Confirm(); gate.Revoke(); Assert.False(gate.CanPersist); }
    [Fact] public void MissingOwnerAndDeadlineAreNotSpecified() { var a = new ActionItem("Sjekk budsjett", null, null); Assert.Equal("ikke oppgitt", a.Owner); Assert.Equal("ikke oppgitt", a.Deadline); }
    [Fact] public void GuestAndAnonymousAreReported() { var r = RecipientResolver.Resolve(tenant, [new Attendee("1", null, null, true), new Attendee("2", Guid.NewGuid(), "guest@example.com", true)]); Assert.Empty(r.Emails); Assert.Equal(2, r.Exclusions.Count); }
    [Fact] public void InviteeWhoDidNotAttendIsExcluded() => Assert.Empty(RecipientResolver.Resolve(tenant, [new Attendee("1", tenant, "a@example.com", false)]).Emails);
    [Fact] public void LateActualAttendeeIsIncluded() => Assert.Single(RecipientResolver.Resolve(tenant, [new Attendee("1", tenant, "a@example.com", true)]).Emails);
    [Fact] public void DistinctVerifiedIdentitiesRemainSeparate() => Assert.Equal(2, RecipientResolver.Resolve(tenant, [new Attendee("1", tenant, "a@example.com", true), new Attendee("2", tenant, "b@example.com", true)]).Emails.Count);
    [Fact] public void ContentExpiresAtThirtyDays() => Assert.True(Retention.IsExpired(DateTimeOffset.UtcNow.AddDays(-30), DateTimeOffset.UtcNow));
    [Fact] public void TwentyNineDayContentIsRetained() => Assert.False(Retention.IsExpired(DateTimeOffset.UtcNow.AddDays(-29), DateTimeOffset.UtcNow));
}
