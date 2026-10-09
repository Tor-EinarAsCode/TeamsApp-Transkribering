using System.Net.Mail;
namespace TeamsAgent.Core;
public static class TenantGuard
{
    public static void Require(Guid expected, Guid actual) { if (expected == Guid.Empty || expected != actual) throw new UnauthorizedAccessException("Organisasjonen har ikke tilgang."); }
}
public record Mention(string MentionedId);
public static class MentionRouter
{
    public static bool IsInvoked(string botId, IReadOnlyList<Mention> mentions) => !string.IsNullOrWhiteSpace(botId) && mentions.Any(m => string.Equals(m.MentionedId, botId, StringComparison.Ordinal));
}
public sealed class RecordingGate
{
    private int confirmed;
    public bool CanPersist => Volatile.Read(ref confirmed) == 1;
    public void Confirm() => Interlocked.Exchange(ref confirmed, 1);
    public void Revoke() => Interlocked.Exchange(ref confirmed, 0);
}
public record ActionItem
{
    public string Task { get; init; }
    public string Owner { get; init; }
    public string Deadline { get; init; }
    public string? Evidence { get; init; }
    public ActionItem(string task, string? owner, string? deadline, string? evidence = null) { Task = task; Evidence = evidence; Owner = string.IsNullOrWhiteSpace(owner) ? "ikke oppgitt" : owner; Deadline = string.IsNullOrWhiteSpace(deadline) ? "ikke oppgitt" : deadline; }
}
public record Attendee(string ParticipantId, Guid? TenantId, string? VerifiedEmail, bool ActuallyAttended, string? DirectoryObjectId = null);
public record RecipientExclusion(string ParticipantId, string Reason);
public record RecipientResolution(IReadOnlyList<string> Emails, IReadOnlyList<RecipientExclusion> Exclusions);
public static class RecipientResolver
{
    public static RecipientResolution Resolve(Guid tenant, IReadOnlyList<Attendee> attendees)
    {
        if (tenant == Guid.Empty) throw new ArgumentException("Tenant required");
        var emails = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var exclusions = new List<RecipientExclusion>();
        foreach (var a in attendees.Where(a => a.ActuallyAttended))
        {
            if (a.TenantId != tenant) { exclusions.Add(new(a.ParticipantId, "Ekstern eller anonym deltaker")); continue; }
            if (a.VerifiedEmail is null || !MailAddress.TryCreate(a.VerifiedEmail, out var address) || address.Address != a.VerifiedEmail) { exclusions.Add(new(a.ParticipantId, "Ingen verifisert e-postadresse")); continue; }
            emails.Add(address.Address);
        }
        return new(emails.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray(), exclusions);
    }
}
public static class Retention { public static bool IsExpired(DateTimeOffset created, DateTimeOffset now) => now >= created.AddDays(30); }
