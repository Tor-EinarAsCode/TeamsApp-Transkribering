using System.Security.Claims;
using Azure.Core;
using Azure.Identity;
using Microsoft.Agents.Hosting.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using TeamsAgent.Api;
using TeamsAgent.Core;
using TeamsAgent.Persistence;
using TeamsAgent.Worker;
var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, WebRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot") });
var config = builder.Configuration;
var tenant = Guid.Parse(config["TenantId"] ?? throw new InvalidOperationException("TenantId mangler."));
var clientId = config["ClientId"] ?? throw new InvalidOperationException("ClientId mangler.");
// Set auth settings from the same single-tenant identity. Secrets may be Key Vault references.
config["TokenValidation:Audiences:0"] = clientId; config["TokenValidation:TenantId"] = tenant.ToString();
config["Connections:ServiceConnection:Settings:AuthorityEndpoint"] = $"https://login.microsoftonline.com/{tenant}";
config["Connections:ServiceConnection:Settings:ClientId"] = clientId;
config["Connections:ServiceConnection:Settings:ClientSecret"] = config["ClientSecret"];
builder.AddAgentDefaults().AddAgent<TeamsModeratorAgent>().AddAgentAuthorization(b =>
{
    b.Services.AddAuthentication("BotConnector").AddJwtBearer("BotConnector", o => { o.MetadataAddress = "https://login.botframework.com/v1/.well-known/openidconfiguration"; o.Audience = clientId; o.TokenValidationParameters.ValidIssuer = "https://api.botframework.com"; o.MapInboundClaims = false; });
}, forceEnable: true);
builder.Services.AddAuthentication().AddJwtBearer("EntraUser", o => { o.Authority = $"https://login.microsoftonline.com/{tenant}/v2.0"; o.Audience = clientId; o.MapInboundClaims = false; o.TokenValidationParameters.ValidAudiences = [clientId, $"api://{clientId}"]; });
builder.Services.AddAuthorization(o => o.AddPolicy("MeetingUser", p => p.AddAuthenticationSchemes("EntraUser").RequireAuthenticatedUser().RequireAssertion(c => c.User.FindFirstValue("tid") == tenant.ToString() && c.User.FindFirstValue("scp")?.Split(' ').Contains("access_as_user") == true)));
builder.Services.AddSingleton<TokenCredential>(_ => new ClientSecretCredential(tenant.ToString(), clientId, config["ClientSecret"] ?? throw new InvalidOperationException("ClientSecret mangler.")));
builder.Services.AddSingleton(_ => new SqlStore(config.GetConnectionString("Sql") ?? throw new InvalidOperationException("SQL mangler."), tenant));
builder.Services.AddSingleton<IMeetingStore>(s => s.GetRequiredService<SqlStore>()); builder.Services.AddSingleton<ITranscriptStore>(s => s.GetRequiredService<SqlStore>()); builder.Services.AddSingleton<IContentStore>(s => s.GetRequiredService<SqlStore>());
builder.Services.AddHttpClient(); builder.Services.AddSingleton(s => new GraphClient(s.GetRequiredService<IHttpClientFactory>().CreateClient(), s.GetRequiredService<TokenCredential>(), tenant));
builder.Services.AddSingleton(s => new AzureAiClient(s.GetRequiredService<IHttpClientFactory>().CreateClient(), new Uri(config["OpenAI:Endpoint"]!), config["OpenAI:Deployment"]!, config["OpenAI:ApiKey"]!));
builder.Services.AddSingleton<IMinutesGenerator>(s => s.GetRequiredService<AzureAiClient>()); builder.Services.AddSingleton(s => new Moderator(tenant, s.GetRequiredService<AzureAiClient>().ModerateAsync));
builder.Services.AddSingleton<IMeetingConnector>(s => new RemoteMeetingConnector(s.GetRequiredService<IHttpClientFactory>().CreateClient(), s.GetRequiredService<TokenCredential>(), new Uri(config["Media:Endpoint"]!), config["Media:Audience"]!));
builder.Services.AddSingleton(s => new MeetingCoordinator(s.GetRequiredService<IMeetingStore>(), s.GetRequiredService<IMeetingConnector>(), tenant));
builder.Services.AddTransient<MinutesSender>(); builder.Services.AddTransient<IMinutesSender>(s => s.GetRequiredService<MinutesSender>());
builder.Services.AddTransient(s => new FinalizeMeetingJob(s.GetRequiredService<IMeetingStore>(), s.GetRequiredService<ITranscriptStore>(), s.GetRequiredService<IContentStore>(), s.GetRequiredService<IMinutesGenerator>(), tenant));
builder.Services.AddTransient(s => new DeliverMinutesJob(s.GetRequiredService<IMeetingStore>(), s.GetRequiredService<IContentStore>(), s.GetRequiredService<IMinutesSender>(), tenant));
builder.Services.AddHostedService<ProcessingWorker>();
var app = builder.Build();
if (args.Contains("--init-db")) { await app.Services.GetRequiredService<SqlStore>().InitializeAsync(default); return; }
app.Use(async (ctx, next) => { try { await next(); } catch (Exception e) when (e is UnauthorizedAccessException or ArgumentException or KeyNotFoundException or InvalidOperationException) { ctx.Response.StatusCode = e switch { UnauthorizedAccessException => 403, ArgumentException => 400, KeyNotFoundException => 404, _ => 409 }; await ctx.Response.WriteAsJsonAsync(new { error = ctx.Response.StatusCode == 403 ? "Ingen tilgang." : "Operasjonen kunne ikke utføres. Kontroller oppsett og møtestatus." }); } });
app.UseAgents(); app.MapDefaultAgentEndpoints();
app.UseDefaultFiles(); app.UseStaticFiles(); app.MapGet("/health", () => new { status = "running", mediaAcceptance = "requires-live-tenant-test" });
app.MapPost("/api/meetings/start", async (StartRequest req, ClaimsPrincipal user, GraphClient graph, MeetingCoordinator meetings, CancellationToken ct) => { var caller = Guid.Parse(user.FindFirstValue("oid")!); var resolved = await graph.ResolveMeetingAsync(caller, req.JoinUrl, ct); var s = await meetings.StartAsync(resolved.Key, caller, resolved.Join, ct); return Results.Ok(new { meetingId = s.Key.MeetingId, state = s.State.ToString() }); }).RequireAuthorization("MeetingUser");
app.MapPost("/api/meetings/{id}/stop", async (string id, ClaimsPrincipal user, MeetingCoordinator meetings, CancellationToken ct) => { await meetings.StopAsync(new(tenant, id), Guid.Parse(user.FindFirstValue("oid")!), ct); return Results.Ok(); }).RequireAuthorization("MeetingUser");
app.MapGet("/api/meetings/{id}", async (string id, ClaimsPrincipal user, SqlStore db, CancellationToken ct) => { var s = await db.GetAsync(new(tenant, id), ct); if (s is null) return Results.NotFound(); if (s.OrganizerId != Guid.Parse(user.FindFirstValue("oid")!)) return Results.Forbid(); return Results.Ok(new { state = s.State.ToString(), incomplete = s.Incomplete }); }).RequireAuthorization("MeetingUser");

app.Run();
record StartRequest(string JoinUrl);
