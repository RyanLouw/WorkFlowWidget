using System.Net;
using System.Text;
using WorkFlowWidget.Core;

namespace WorkFlowWidget.Core.Tests;

internal sealed class TestTokens : IAccessTokenProvider
{
    public List<TokenResource> Resources { get; } = [];
    public Task<string> GetTokenAsync(TokenResource resource, CancellationToken cancellationToken)
    {
        Resources.Add(resource);
        return Task.FromResult(resource == TokenResource.MicrosoftGraph ? "graph-token" : "devops-token");
    }
}

internal sealed class ApiHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request);
    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };
}
