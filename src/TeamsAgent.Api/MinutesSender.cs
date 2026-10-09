using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Agents.Authentication;
using Microsoft.Agents.Builder;
using Microsoft.Agents.Core.Models;
using TeamsAgent.Core;
using TeamsAgent.Persistence;
namespace TeamsAgent.Api;
public sealed class MinutesSender(GraphClient graph, IChannelAdapter adapter, SqlStore store, IConfiguration config) : IMinutesSender
{
    public async Task<DeliveryOutcome> SendAsync(MeetingSession session, MeetingMinutes minutes, string recipient, CancellationToken ct)
    {
        var text = Render(minutes);
        if (recipient == "@meeting-chat")
        {
            var saved = await store.GetConversationAsync(session.Join.ThreadId, ct); if (saved is null) return DeliveryOutcome.Failed;
            var reference = JsonSerializer.Deserialize<ConversationReference>(saved) ?? throw new InvalidOperationException();
            await adapter.ContinueConversationAsync(AgentClaims.CreateIdentity(config["ClientId"]!), reference, async (context, token) => { await context.SendActivityAsync(MessageFactory.Text(text), token); }, ct);
            return DeliveryOutcome.Sent;
        }
        var allowed = RecipientResolver.Resolve(session.Key.TenantId, session.Attendees).Emails; if (!allowed.Contains(recipient, StringComparer.OrdinalIgnoreCase)) throw new UnauthorizedAccessException();
        var mailbox = config["Mail:Sender"] ?? throw new InvalidOperationException("Avsenderkonto mangler.");
        using var req = new HttpRequestMessage(HttpMethod.Post, $"https://graph.microsoft.com/v1.0/users/{Uri.EscapeDataString(mailbox)}/sendMail");
        req.Content = JsonContent.Create(new { message = new { subject = "Møtereferat – Teams", body = new { contentType = "Text", content = text }, toRecipients = new[] { new { emailAddress = new { address = recipient } } } }, saveToSentItems = true });
        using var response = await graph.SendAsync(req, ct);
        // Graph acceptance does not prove mailbox delivery; Sent means accepted for sending.
        return response.IsSuccessStatusCode ? DeliveryOutcome.Sent : DeliveryOutcome.Failed;
    }
    public async Task NotifyOrganizerAsync(MeetingSession s, DeliveryReport report, CancellationToken ct)
    {
        var organizer = await graph.VerifyInternalEmailAsync(s.OrganizerId.ToString(), ct); if (organizer is null) return;
        var text = "Leveringsstatus for møtereferat. Sendt betyr akseptert for sending, ikke bekreftet lest eller levert til innboks.\n" + string.Join("\n", report.EmailOutcomes.Select(x => $"{x.Key}: {x.Value}")) + "\nMøtechat: " + report.ChatOutcome + "\n" + string.Join("\n", report.Exclusions.Select(x => $"{x.ParticipantId}: {x.Reason}"));
        using var req = new HttpRequestMessage(HttpMethod.Post, $"https://graph.microsoft.com/v1.0/users/{Uri.EscapeDataString(config["Mail:Sender"]!)}/sendMail"); req.Content = JsonContent.Create(new { message = new { subject = "Leveringsstatus – møtereferat", body = new { contentType = "Text", content = text }, toRecipients = new[] { new { emailAddress = new { address = organizer } } } } }); using var r = await graph.SendAsync(req, ct); r.EnsureSuccessStatusCode();
    }
    public async Task NotifyProblemAsync(MeetingSession s, CancellationToken ct) { var organizer = await graph.VerifyInternalEmailAsync(s.OrganizerId.ToString(), ct); if (organizer is null) throw new InvalidOperationException("Arrangøradresse mangler."); using var req = new HttpRequestMessage(HttpMethod.Post, $"https://graph.microsoft.com/v1.0/users/{Uri.EscapeDataString(config["Mail:Sender"]!)}/sendMail"); req.Content = JsonContent.Create(new { message = new { subject = "Møteagenten trenger oppfølging", body = new { contentType = "Text", content = "Møteagenten kunne ikke fullføre referatet eller leveringen automatisk. Tilgjengelig innhold beholdes innenfor appens 30-dagers lagring. Kontakt IT-ansvarlig for å kontrollere møtestatus og leveringsrapport. Ingen ukjent levering sendes på nytt automatisk." }, toRecipients = new[] { new { emailAddress = new { address = organizer } } } } }); using var r = await graph.SendAsync(req, ct); r.EnsureSuccessStatusCode(); }
    static string Render(MeetingMinutes m)
    {
        var b = new StringBuilder("AI-generert møtereferat\n"); if (m.Incomplete) b.AppendLine("Ufullstendig: bare tilgjengelig tale er brukt."); b.AppendLine("Fanget periode: " + m.CapturedPeriod).AppendLine().AppendLine(m.Summary).AppendLine().AppendLine("Beslutninger:"); foreach (var d in m.Decisions) b.AppendLine("• " + d); b.AppendLine("Oppgaver:"); foreach (var a in m.Actions) b.AppendLine($"• {a.Task} – ansvarlig: {a.Owner}; frist: {a.Deadline}"); return b.ToString();
    }
}
