using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using Microsoft.Graph.Communications.Client.Authentication;
using Microsoft.Identity.Client;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
namespace TeamsAgent.MediaHost;
public sealed class GraphAuthentication : IRequestAuthenticationProvider, ITokenProvider
{
    readonly string tenant; readonly string appId; readonly IConfidentialClientApplication app;
    readonly ConfigurationManager<OpenIdConnectConfiguration> oidc = new("https://api.aps.skype.com/v1/.well-known/OpenIdConfiguration", new OpenIdConnectConfigurationRetriever());
    public GraphAuthentication(string tenant, string appId, string secret) { this.tenant = tenant; this.appId = appId; app = ConfidentialClientApplicationBuilder.Create(appId).WithAuthority($"https://login.microsoftonline.com/{tenant}").WithClientSecret(secret).Build(); }
    public async Task<string> AcquireTokenAsync(string requestedTenant) { if (!string.IsNullOrEmpty(requestedTenant) && requestedTenant != tenant) throw new UnauthorizedAccessException(); return (await app.AcquireTokenForClient(["https://graph.microsoft.com/.default"]).ExecuteAsync()).AccessToken; }
    public async Task AuthenticateOutboundRequestAsync(HttpRequestMessage request, string requestedTenant) { if (!string.IsNullOrEmpty(requestedTenant) && requestedTenant != tenant) throw new UnauthorizedAccessException(); var token = await app.AcquireTokenForClient(["https://graph.microsoft.com/.default"]).ExecuteAsync(); request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken); }
    public async Task<RequestValidationResult> ValidateInboundRequestAsync(HttpRequestMessage request)
    {
        if (request.Headers.Authorization?.Scheme != "Bearer" || string.IsNullOrEmpty(request.Headers.Authorization.Parameter)) return new() { IsValid = false };
        try { var keys = await oidc.GetConfigurationAsync(default); var handler = new JwtSecurityTokenHandler { MapInboundClaims = false }; var claims = handler.ValidateToken(request.Headers.Authorization.Parameter, new TokenValidationParameters { ValidIssuers = ["https://graph.microsoft.com", "https://api.botframework.com"], ValidAudience = appId, IssuerSigningKeys = keys.SigningKeys, ValidateIssuerSigningKey = true, RequireSignedTokens = true, ValidateLifetime = true, ClockSkew = TimeSpan.FromMinutes(1) }, out _); var actual = claims.FindFirst("tid")?.Value ?? claims.FindFirst("http://schemas.microsoft.com/identity/claims/tenantid")?.Value; return new() { IsValid = actual == tenant, TenantId = actual }; } catch (SecurityTokenException) { return new() { IsValid = false }; }
    }
}
