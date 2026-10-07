using System.Net;
using System.Text;
using WorkFlowWidget.Infrastructure;
using Xunit;

namespace WorkFlowWidget.Core.Tests;

public sealed class GraphAccountVerifierTests
{
    [Fact]
    public async Task VerifiesGraphProfileWithBearerTokenAndFallsBackToPrincipalName()
    {
        using var client = new HttpClient(new Handler(request =>
        {
            Assert.Equal("graph.microsoft.com", request.RequestUri!.Host);
            Assert.Equal("/v1.0/me", request.RequestUri.AbsolutePath);
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            Assert.Equal("test-token", request.Headers.Authorization.Parameter);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"id\":\"account-id\",\"displayName\":\"Taylor\",\"mail\":null,\"userPrincipalName\":\"taylor@example.com\"}", Encoding.UTF8, "application/json")
            };
        }));
        var user = await new GraphAccountVerifier(client).VerifyAsync("test-token", CancellationToken.None);
        Assert.Equal("account-id", user.Id);
        Assert.Equal("taylor@example.com", user.Email);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task RejectedTokenCannotProduceVerifiedUser(HttpStatusCode status)
    {
        using var client = new HttpClient(new Handler(_ => new HttpResponseMessage(status)));
        await Assert.ThrowsAsync<HttpRequestException>(() => new GraphAccountVerifier(client).VerifyAsync("test-token", CancellationToken.None));
    }

    [Fact]
    public async Task InvalidProfileCannotProduceVerifiedUser()
    {
        using var client = new HttpClient(new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new GraphAccountVerifier(client).VerifyAsync("test-token", CancellationToken.None));
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }
}
