namespace TeamsAgent.Core;
public record MeetingMinutes(string Summary, IReadOnlyList<string> Decisions, IReadOnlyList<ActionItem> Actions, bool Incomplete, string CapturedPeriod);
public interface IMinutesGenerator { Task<MeetingMinutes> GenerateAsync(IReadOnlyList<TranscriptSegment> segments, bool incomplete, CancellationToken ct); }
public static class MinutesValidator
{
    public static MeetingMinutes Validate(MeetingMinutes minutes, IReadOnlyList<TranscriptSegment> segments, bool incomplete)
    {
        if (string.IsNullOrWhiteSpace(minutes.Summary) || minutes.Summary.Length > 20000 || minutes.Actions.Count > 200 || minutes.Decisions.Count > 200) throw new InvalidOperationException("Ugyldig referat.");
        var text = string.Join(" ", segments.Select(s => s.Text));
        var actions = minutes.Actions.Select(a => new ActionItem(a.Task, a.Owner != "ikke oppgitt" && a.Evidence is not null && text.Contains(a.Evidence, StringComparison.Ordinal) && a.Evidence.Contains(a.Owner, StringComparison.OrdinalIgnoreCase) ? a.Owner : null, a.Deadline != "ikke oppgitt" && a.Evidence is not null && text.Contains(a.Evidence, StringComparison.Ordinal) && a.Evidence.Contains(a.Deadline, StringComparison.OrdinalIgnoreCase) ? a.Deadline : null, a.Evidence)).ToArray();
        var period = segments.Count == 0 ? "Ingen tale fanget" : $"{segments.Min(s => s.Start):c}–{segments.Max(s => s.End):c}";
        return minutes with { Actions = actions, Incomplete = incomplete, CapturedPeriod = period };
    }
}
public enum DeliveryOutcome { Sent, Failed, Unknown }
public record DeliveryReport(IReadOnlyDictionary<string, DeliveryOutcome> EmailOutcomes, DeliveryOutcome ChatOutcome, IReadOnlyList<RecipientExclusion> Exclusions);
public interface IContentStore { Task<MeetingMinutes?> GetMinutesAsync(MeetingKey key, CancellationToken ct); Task SaveMinutesAsync(MeetingKey key, MeetingMinutes minutes, CancellationToken ct); Task<IReadOnlyList<MeetingKey>> PendingAsync(CancellationToken ct); Task<bool> TryLeaseAsync(MeetingKey key, string owner, CancellationToken ct); Task ReleaseAsync(MeetingKey key, string owner, CancellationToken ct); Task<DeliveryOutcome?> ReserveDeliveryAsync(MeetingKey key, string recipient, CancellationToken ct); Task CompleteDeliveryAsync(MeetingKey key, string recipient, DeliveryOutcome status, CancellationToken ct); Task<int> DeleteExpiredAsync(DateTimeOffset now, CancellationToken ct); }
public interface IMinutesSender { Task<DeliveryOutcome> SendAsync(MeetingSession session, MeetingMinutes minutes, string recipient, CancellationToken ct); }
