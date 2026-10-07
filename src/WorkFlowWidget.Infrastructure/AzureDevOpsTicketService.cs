using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using WorkFlowWidget.Core;

namespace WorkFlowWidget.Infrastructure;

public sealed class AzureDevOpsTicketService : ITicketService
{
    private readonly AuthorizedApi api;
    private readonly AzureDevOpsSettings settings;
    private readonly string root;
    public AzureDevOpsTicketService(HttpClient client, IAccessTokenProvider tokens, AzureDevOpsSettings settings)
    {
        api = new AuthorizedApi(client, tokens, TokenResource.AzureDevOps, "dev.azure.com");
        this.settings = settings;
        root = "https://dev.azure.com/" + Uri.EscapeDataString(settings.Organization ?? "");
    }

    public async Task<IReadOnlyList<ProjectTickets>> GetAssignedActiveAsync(CancellationToken cancellationToken)
    {
        ValidateOrganization();
        var projects = settings.Projects.Length == 0 ? await GetProjectsAsync(cancellationToken)
            : settings.Projects.Where(project => !string.IsNullOrWhiteSpace(project)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var groups = new List<ProjectTickets>();
        foreach (var project in projects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { groups.Add(new ProjectTickets(project, await GetProjectTicketsAsync(project, cancellationToken))); }
            catch (OperationCanceledException) { throw; }
            catch (Exception) { groups.Add(new ProjectTickets(project, [], "Could not load this project. Check project access and retry.")); }
        }
        return groups;
    }

    private async Task<string[]> GetProjectsAsync(CancellationToken cancellationToken)
    {
        var projects = new List<string>();
        string? continuation = null;
        var visited = new HashSet<string>();
        do
        {
            var url = root + "/_apis/projects?api-version=7.1&$top=100";
            if (continuation is not null) url += "&continuationToken=" + Uri.EscapeDataString(continuation);
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = await api.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            using var document = await ReadAsync(response, cancellationToken);
            projects.AddRange(document.RootElement.GetProperty("value").EnumerateArray().Select(project => project.GetProperty("name").GetString()!));
            continuation = response.Headers.TryGetValues("x-ms-continuationtoken", out var values) ? values.FirstOrDefault() : null;
            if (string.IsNullOrEmpty(continuation)) continuation = null;
            if (continuation is not null && !visited.Add(continuation))
                throw new InvalidOperationException("Azure DevOps repeated a project page.");
        } while (continuation is not null);
        return projects.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private async Task<IReadOnlyList<WorkTicket>> GetProjectTicketsAsync(string project, CancellationToken cancellationToken)
    {
        var projectRoot = root + "/" + Uri.EscapeDataString(project);
        using var query = new HttpRequestMessage(HttpMethod.Post, projectRoot + "/_apis/wit/wiql?api-version=7.1")
        {
            Content = JsonContent.Create(new { query = "SELECT [System.Id] FROM WorkItems WHERE [System.TeamProject] = @project AND [System.AssignedTo] = @Me ORDER BY [System.ChangedDate] DESC" })
        };
        using var queryResponse = await api.SendAsync(query, cancellationToken);
        queryResponse.EnsureSuccessStatusCode();
        using var queryDocument = await ReadAsync(queryResponse, cancellationToken);
        var ids = queryDocument.RootElement.GetProperty("workItems").EnumerateArray().Select(item => item.GetProperty("id").GetInt32()).ToArray();
        var tickets = new List<WorkTicket>();
        var stateDefinitions = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        // Azure DevOps accepts at most 200 work items per batch.
        foreach (var batch in ids.Chunk(200))
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, projectRoot + "/_apis/wit/workitemsbatch?api-version=7.1")
            {
                Content = JsonContent.Create(new { ids = batch, fields = new[] { "System.Title", "System.State", "System.WorkItemType", "System.TeamProject" } })
            };
            using var response = await api.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            using var document = await ReadAsync(response, cancellationToken);
            foreach (var item in document.RootElement.GetProperty("value").EnumerateArray())
            {
                var fields = item.GetProperty("fields");
                var type = fields.GetProperty("System.WorkItemType").GetString()!;
                if (!stateDefinitions.TryGetValue(type, out var states))
                {
                    states = await GetStatesAsync(project, type, cancellationToken);
                    stateDefinitions[type] = states;
                }
                var state = fields.GetProperty("System.State").GetString()!;
                if (!states.TryGetValue(state, out var category)) throw new InvalidOperationException("Work item state is not defined by this project.");
                if (category.Equals("Completed", StringComparison.OrdinalIgnoreCase) || category.Equals("Removed", StringComparison.OrdinalIgnoreCase)) continue;
                var id = item.GetProperty("id").GetInt32();
                tickets.Add(new WorkTicket(id, project, fields.GetProperty("System.Title").GetString()!, state, type,
                    projectRoot + "/_workitems/edit/" + id, item.GetProperty("rev").GetInt32()));
            }
        }
        return tickets;
    }

    public async Task CloseAsync(WorkTicket ticket, CancellationToken cancellationToken)
    {
        ValidateOrganization();
        var states = await GetStatesAsync(ticket.Project, ticket.WorkItemType, cancellationToken);
        var completed = states.Where(pair => pair.Value.Equals("Completed", StringComparison.OrdinalIgnoreCase)).Select(pair => pair.Key).ToArray();
        var target = completed.FirstOrDefault(state => state.Equals("Closed", StringComparison.OrdinalIgnoreCase))
            ?? completed.FirstOrDefault(state => state.Equals("Done", StringComparison.OrdinalIgnoreCase)) ?? completed.FirstOrDefault()
            ?? throw new InvalidOperationException("This work item type has no completed state.");
        var operations = new object[]
        {
            new { op = "test", path = "/rev", value = (object)ticket.Revision },
            new { op = "add", path = "/fields/System.State", value = (object)target }
        };
        using var request = new HttpRequestMessage(HttpMethod.Patch,
            root + "/" + Uri.EscapeDataString(ticket.Project) + "/_apis/wit/workitems/" + ticket.Id + "?api-version=7.1")
        {
            Content = new StringContent(JsonSerializer.Serialize(operations), Encoding.UTF8, "application/json-patch+json")
        };
        using var response = await api.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private async Task<Dictionary<string, string>> GetStatesAsync(string project, string type, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, root + "/" + Uri.EscapeDataString(project)
            + "/_apis/wit/workitemtypes/" + Uri.EscapeDataString(type) + "/states?api-version=7.1");
        using var response = await api.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = await ReadAsync(response, cancellationToken);
        return document.RootElement.GetProperty("value").EnumerateArray().ToDictionary(
            state => state.GetProperty("name").GetString()!, state => state.GetProperty("category").GetString()!, StringComparer.OrdinalIgnoreCase);
    }

    private void ValidateOrganization()
    {
        if (string.IsNullOrWhiteSpace(settings.Organization) || settings.Organization.StartsWith("YOUR-", StringComparison.OrdinalIgnoreCase)
            || !Regex.IsMatch(settings.Organization, "^[a-zA-Z0-9][a-zA-Z0-9-]*$"))
            throw new InvalidOperationException("Set AzureDevOps.Organization in appsettings.json to your organization name (not its URL).");
    }
    private static async Task<JsonDocument> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
}
