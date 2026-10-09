using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using TeamsAgent.Core;
namespace TeamsAgent.Worker;
public sealed class GraphRecordingTransport(HttpClient http, Func<CancellationToken, Task<string>> token) : IRecordingStatusTransport
{
    public async Task<RecordingOperation> RequestAsync(string callId, bool recording, CancellationToken ct)
    {
        var prefix = $"https://graph.microsoft.com/v1.0/communications/calls/{Uri.EscapeDataString(callId)}";
        using var request = new HttpRequestMessage(HttpMethod.Post, prefix + "/updateRecordingStatus"); request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await token(ct)); request.Content = JsonContent.Create(new { clientContext = Guid.NewGuid().ToString(), status = recording ? "recording" : "notRecording" });
        using var response = await http.SendAsync(request, ct); response.EnsureSuccessStatusCode(); using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var status = doc.RootElement.TryGetProperty("status", out var s) ? s.GetString() ?? "unknown" : "unknown";
        var location = response.Headers.Location?.ToString(); if (location is null && doc.RootElement.TryGetProperty("id", out var id)) location = prefix + "/operations/" + Uri.EscapeDataString(id.GetString()!);
        if (location is not null) { var uri = new Uri(new Uri(prefix), location); if (uri.Scheme != "https" || uri.Host != "graph.microsoft.com" || !uri.AbsoluteUri.StartsWith(prefix + "/operations/", StringComparison.Ordinal)) throw new UnauthorizedAccessException("Uventet opptaksoperasjon."); location = uri.AbsoluteUri; }
        return new(location ?? "", status);
    }
    public async Task<RecordingOperation> PollAsync(RecordingOperation op, CancellationToken ct) { var uri = new Uri(op.Location); if (uri.Scheme != "https" || uri.Host != "graph.microsoft.com") throw new UnauthorizedAccessException(); using var request = new HttpRequestMessage(HttpMethod.Get, uri); request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await token(ct)); using var response = await http.SendAsync(request, ct); response.EnsureSuccessStatusCode(); using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct); return op with { Status = doc.RootElement.GetProperty("status").GetString() ?? "unknown" }; }
}
