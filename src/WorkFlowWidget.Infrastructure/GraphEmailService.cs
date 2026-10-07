using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using WorkFlowWidget.Core;

namespace WorkFlowWidget.Infrastructure;

public sealed class GraphEmailService : IEmailService
{
    private readonly AuthorizedApi api;
    public GraphEmailService(HttpClient client, IAccessTokenProvider tokens) => api = new AuthorizedApi(client, tokens, TokenResource.MicrosoftGraph, "graph.microsoft.com");

    public async Task<IReadOnlyList<InboxEmail>> GetUnreadAsync(CancellationToken cancellationToken)
    {
        string? next = "https://graph.microsoft.com/v1.0/me/mailFolders/inbox/messages?$filter=isRead%20eq%20false&$select=id,subject,from,receivedDateTime,conversationId,webLink&$top=100";
        var emails = new Dictionary<string, InboxEmail>();
        var visited = new HashSet<string>();
        while (next is not null)
        {
            if (!visited.Add(next)) throw new InvalidOperationException("Graph repeated a pagination link. Refresh to try again.");
            using var request = CreateRequest(HttpMethod.Get, next);
            using var response = await api.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            foreach (var item in document.RootElement.GetProperty("value").EnumerateArray())
            {
                var id = item.GetProperty("id").GetString() ?? throw new InvalidOperationException("Email ID missing.");
                var sender = item.TryGetProperty("from", out var from) && from.ValueKind == JsonValueKind.Object
                    && from.TryGetProperty("emailAddress", out var address)
                    ? Text(address, "name") ?? Text(address, "address") ?? "Unknown sender" : "Unknown sender";
                emails[id] = new InboxEmail(id, Text(item, "subject") ?? "(No subject)", sender,
                    item.GetProperty("receivedDateTime").GetDateTimeOffset(), Text(item, "conversationId"), Text(item, "webLink"));
            }
            next = Text(document.RootElement, "@odata.nextLink");
        }
        return emails.Values.OrderByDescending(email => email.ReceivedAt).ToArray();
    }

    public async Task SetCategoryAsync(string messageId, EmailCategory category, CancellationToken cancellationToken)
    {
        var url = $"https://graph.microsoft.com/v1.0/me/messages/{Uri.EscapeDataString(messageId)}";
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var get = CreateRequest(HttpMethod.Get, url + "?$select=categories");
            using var existing = await api.SendAsync(get, cancellationToken);
            existing.EnsureSuccessStatusCode();
            using var document = await JsonDocument.ParseAsync(await existing.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            var categories = document.RootElement.GetProperty("categories").EnumerateArray().Select(value => value.GetString()!)
                .Where(value => !Enum.GetValues<EmailCategory>().Any(c => c.Label() == value)).Append(category.Label()).Distinct().ToArray();
            using var patch = CreateRequest(HttpMethod.Patch, url);
            patch.Content = JsonContent.Create(new { categories });
            var etag = Text(document.RootElement, "@odata.etag");
            if (etag is not null) patch.Headers.TryAddWithoutValidation("If-Match", etag);
            using var updated = await api.SendAsync(patch, cancellationToken);
            if (updated.StatusCode == HttpStatusCode.PreconditionFailed && attempt == 0) continue;
            updated.EnsureSuccessStatusCode();
            return;
        }
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string url)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.TryAddWithoutValidation("Prefer", "IdType=\"ImmutableId\"");
        return request;
    }
    private static string? Text(JsonElement element, string name) => element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String ? property.GetString() : null;
}
