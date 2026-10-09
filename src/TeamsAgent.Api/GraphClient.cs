using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using TeamsAgent.Core;
namespace TeamsAgent.Api;
public sealed class GraphClient(HttpClient http, TokenCredential credential, Guid tenant)
{
    public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        request.Headers.Authorization = new("Bearer", (await credential.GetTokenAsync(new TokenRequestContext(["https://graph.microsoft.com/.default"]), ct)).Token);
        return await http.SendAsync(request, ct);
    }
    public async Task<(MeetingKey Key, MeetingJoinDescriptor Join)> ResolveMeetingAsync(Guid organizer, string joinUrl, CancellationToken ct)
    {
        if (!Uri.TryCreate(joinUrl, UriKind.Absolute, out var url) || url.Scheme != "https" || url.Host != "teams.microsoft.com") throw new ArgumentException("Ugyldig Teams-møtelenke.");
        var filter = Uri.EscapeDataString("JoinWebUrl eq '" + joinUrl.Replace("'", "''") + "'");
        using var req = new HttpRequestMessage(HttpMethod.Get, $"https://graph.microsoft.com/v1.0/users/{organizer}/onlineMeetings?$filter={filter}"); using var response = await SendAsync(req, ct); response.EnsureSuccessStatusCode();
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var values = doc.RootElement.GetProperty("value"); if (values.GetArrayLength() != 1) throw new UnauthorizedAccessException("Møtet må være organisert av deg."); var m = values[0];
        var actual = Guid.Parse(m.GetProperty("participants").GetProperty("organizer").GetProperty("identity").GetProperty("user").GetProperty("id").GetString()!); if (actual != organizer) throw new UnauthorizedAccessException();
        return (new(tenant, m.GetProperty("id").GetString()!), new(joinUrl, m.GetProperty("chatInfo").GetProperty("threadId").GetString()!, actual));
    }
    public async Task<string?> VerifyInternalEmailAsync(string id, CancellationToken ct)
    {
        if (!Guid.TryParse(id, out var oid)) return null;
        using var req = new HttpRequestMessage(HttpMethod.Get, $"https://graph.microsoft.com/v1.0/users/{oid}?$select=mail,userPrincipalName,userType"); using var response = await SendAsync(req, ct); if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null; response.EnsureSuccessStatusCode();
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct); var u = doc.RootElement;
        if (u.GetProperty("userType").GetString() != "Member") return null;
        return u.TryGetProperty("mail", out var mail) && mail.ValueKind == JsonValueKind.String ? mail.GetString() : u.GetProperty("userPrincipalName").GetString();
    }
}
public sealed class RemoteMeetingConnector(HttpClient http, TokenCredential credential, Uri endpoint, string audience) : IMeetingConnector
{
    async Task<HttpResponseMessage> Send(string route, MeetingSession s, CancellationToken ct) { if (endpoint.Scheme != "https") throw new InvalidOperationException("Medievert krever HTTPS."); using var req = new HttpRequestMessage(HttpMethod.Post, new Uri(endpoint, route)); req.Headers.Authorization = new("Bearer", (await credential.GetTokenAsync(new TokenRequestContext([audience + "/.default"]), ct)).Token); req.Content = JsonContent.Create(s); return await http.SendAsync(req, ct); }
    public async Task<string> JoinAsync(MeetingSession s, CancellationToken ct) { using var r = await Send("/control/join", s, ct); r.EnsureSuccessStatusCode(); var d = await r.Content.ReadFromJsonAsync<CallReply>(cancellationToken: ct); return d?.CallId ?? throw new InvalidOperationException(); }
    public async Task StopAsync(MeetingSession s, CancellationToken ct) { using var r = await Send("/control/stop", s, ct); r.EnsureSuccessStatusCode(); }
    record CallReply(string CallId);
}
