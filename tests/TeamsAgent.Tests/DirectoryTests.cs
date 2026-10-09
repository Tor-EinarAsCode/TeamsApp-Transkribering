using Azure.Core;
using System.Net;
using TeamsAgent.Api;
using Xunit;
namespace TeamsAgent.Tests;
public class DirectoryTests
{
    [Fact] public async Task TemporaryDirectoryFailureDoesNotExcludeAttendee() { var graph = new GraphClient(new(new FakeHttp(HttpStatusCode.TooManyRequests)), new Credential(), Guid.NewGuid()); await Assert.ThrowsAsync<HttpRequestException>(() => graph.VerifyInternalEmailAsync(Guid.NewGuid().ToString(), default)); }
    [Fact] public async Task MissingDirectoryIdentityIsUnverifiable() { var graph = new GraphClient(new(new FakeHttp(HttpStatusCode.NotFound)), new Credential(), Guid.NewGuid()); Assert.Null(await graph.VerifyInternalEmailAsync(Guid.NewGuid().ToString(), default)); }
    sealed class FakeHttp(HttpStatusCode status) : HttpMessageHandler { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct) => Task.FromResult(new HttpResponseMessage(status)); }
    sealed class Credential : TokenCredential { public override AccessToken GetToken(TokenRequestContext c, CancellationToken ct) => new("test", DateTimeOffset.UtcNow.AddHours(1)); public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext c, CancellationToken ct) => ValueTask.FromResult(GetToken(c, ct)); }
}
