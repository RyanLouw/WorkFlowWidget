using System.Net;
using System.Text.Json;
using WorkFlowWidget.Core;
using WorkFlowWidget.Infrastructure;
using Xunit;

namespace WorkFlowWidget.Core.Tests;

public sealed class GraphEmailServiceTests
{
    [Fact]
    public async Task ReadsEveryUnreadPageUsingImmutableIdsAndGraphToken()
    {
        var calls = 0;
        var tokens = new TestTokens();
        using var client = new HttpClient(new ApiHandler(request =>
        {
            Assert.Equal("graph-token", request.Headers.Authorization!.Parameter);
            Assert.Contains("ImmutableId", request.Headers.GetValues("Prefer").Single());
            calls++;
            return Task.FromResult(ApiHandler.Json(calls == 1
                ? "{\"value\":[{\"id\":\"a\",\"subject\":\"First\",\"receivedDateTime\":\"2026-10-07T08:00:00Z\"}],\"@odata.nextLink\":\"https://graph.microsoft.com/v1.0/me/mailFolders/inbox/messages?$skiptoken=next\"}"
                : "{\"value\":[{\"id\":\"b\",\"subject\":\"Second\",\"receivedDateTime\":\"2026-10-07T09:00:00Z\"}]}"));
        }));
        var messages = await new GraphEmailService(client, tokens).GetUnreadAsync(CancellationToken.None);
        Assert.Equal(new[] { "b", "a" }, messages.Select(email => email.Id));
        Assert.Equal(2, calls);
        Assert.All(tokens.Resources, resource => Assert.Equal(TokenResource.MicrosoftGraph, resource));
    }

    [Fact]
    public async Task DoesNotSendTokensToUnexpectedPaginationHost()
    {
        var calls = 0;
        var tokens = new TestTokens();
        using var client = new HttpClient(new ApiHandler(_ =>
        {
            calls++;
            return Task.FromResult(ApiHandler.Json("{\"value\":[],\"@odata.nextLink\":\"https://example.com/steal\"}"));
        }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new GraphEmailService(client, tokens).GetUnreadAsync(CancellationToken.None));
        Assert.Equal(1, calls);
        Assert.Single(tokens.Resources);
    }

    [Fact]
    public async Task CategoryPatchPreservesOtherCategoriesAndRetriesConcurrentChange()
    {
        var calls = 0;
        using var client = new HttpClient(new ApiHandler(async request =>
        {
            calls++;
            if (request.Method == HttpMethod.Get)
                return ApiHandler.Json(calls == 1
                    ? "{\"categories\":[\"Important\",\"New bug\"],\"@odata.etag\":\"etag1\"}"
                    : "{\"categories\":[\"Important\",\"Finance\",\"New bug\"],\"@odata.etag\":\"etag2\"}");
            Assert.Equal(HttpMethod.Patch, request.Method);
            using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            var categories = payload.RootElement.GetProperty("categories").EnumerateArray().Select(value => value.GetString()).ToArray();
            Assert.Contains("Important", categories);
            Assert.Contains("Helpdesk query", categories);
            Assert.DoesNotContain("New bug", categories);
            if (calls == 2)
            {
                Assert.Equal("etag1", request.Headers.GetValues("If-Match").Single());
                return new HttpResponseMessage(HttpStatusCode.PreconditionFailed);
            }
            Assert.Contains("Finance", categories);
            Assert.Equal("etag2", request.Headers.GetValues("If-Match").Single());
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));
        await new GraphEmailService(client, new TestTokens()).SetCategoryAsync("id/with+symbols", EmailCategory.HelpdeskQuery, CancellationToken.None);
        Assert.Equal(4, calls);
    }
}
