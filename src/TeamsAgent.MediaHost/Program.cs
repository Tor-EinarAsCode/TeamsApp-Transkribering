using System.Security.Claims;
using Microsoft.Graph.Communications.Client;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using TeamsAgent.Core;
using TeamsAgent.Persistence;
using TeamsAgent.MediaHost;
var builder = WebApplication.CreateBuilder(args); var cfg = builder.Configuration; var tenant = Guid.Parse(cfg["TenantId"]!);
builder.Services.AddSingleton(_ => new SqlStore(cfg.GetConnectionString("Sql")!, tenant)); builder.Services.AddSingleton<GraphMediaConnector>();
builder.Services.AddAuthentication("Control").AddJwtBearer("Control", o => { o.Authority = $"https://login.microsoftonline.com/{tenant}/v2.0"; o.Audience = cfg["Media:Audience"]; o.TokenValidationParameters.ValidAudiences = [cfg["Media:Audience"]!, cfg["Media:Audience"]!.Replace("api://", "")]; o.MapInboundClaims = false; });
builder.Services.AddAuthorization(o => o.AddPolicy("MediaControl", p => p.RequireAuthenticatedUser().RequireClaim("tid", tenant.ToString()).RequireClaim("roles", "Media.Control").RequireAssertion(c => c.User.FindFirstValue("azp") == cfg["ControlClientId"])));
var app = builder.Build(); app.UseAuthentication(); app.UseAuthorization();
app.MapPost("/control/probe", async (MeetingSession s, GraphMediaConnector media, CancellationToken ct) => { TenantGuard.Require(tenant, s.Key.TenantId); return Results.Ok(await new MediaProbe(media.Client, media.Recording).RunAsync(s, ct)); }).RequireAuthorization("MediaControl");
app.MapPost("/control/join", async (MeetingSession s, GraphMediaConnector media, CancellationToken ct) => { TenantGuard.Require(tenant, s.Key.TenantId); return Results.Ok(new { callId = await media.JoinAsync(s, ct) }); }).RequireAuthorization("MediaControl");
app.MapPost("/control/stop", async (MeetingSession s, GraphMediaConnector media, CancellationToken ct) => { TenantGuard.Require(tenant, s.Key.TenantId); await media.StopAsync(s, ct); return Results.Ok(); }).RequireAuthorization("MediaControl");
app.MapPost("/graph/callback", async (HttpRequest req, GraphMediaConnector media, CancellationToken ct) =>
{
    using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(cfg["Media:NotificationUrl"]!)); message.Content = new StreamContent(req.Body); foreach (var header in req.Headers) if (!message.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray())) message.Content.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
    if (!(await media.Authentication.ValidateInboundRequestAsync(message)).IsValid) return Results.Unauthorized();
    using var response = await media.Client.ProcessNotificationAsync(message); return Results.Content(await response.Content.ReadAsStringAsync(ct), "application/json", statusCode: (int)response.StatusCode);
});
app.MapGet("/health", () => new { status = "running", recordingPolicyVerified = cfg.GetValue<bool>("Media:RecordingPolicyVerified") }); app.Run();
