using System.Net;
using System.Text.Json;
using WorkFlowWidget.Core;
using WorkFlowWidget.Infrastructure;
using Xunit;

namespace WorkFlowWidget.Core.Tests;

public sealed class AzureDevOpsTicketServiceTests
{
    [Fact]
    public async Task DiscoversEveryProjectBatchesIdsAndFiltersCustomCompletedAndRemovedStates()
    {
        var projectCalls = 0;
        var batchSizes = new List<int>();
        var tokens = new TestTokens();
        using var client = new HttpClient(new ApiHandler(async request =>
        {
            Assert.Equal("devops-token", request.Headers.Authorization!.Parameter);
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/_apis/projects"))
            {
                projectCalls++;
                var response = ApiHandler.Json(projectCalls == 1 ? "{\"value\":[{\"name\":\"Portal\"}]}" : "{\"value\":[{\"name\":\"Tools\"}]}");
                if (projectCalls == 1) response.Headers.Add("x-ms-continuationtoken", "next-project");
                else Assert.Contains("continuationToken=next-project", request.RequestUri.Query);
                return response;
            }
            if (path.EndsWith("/wiql"))
            {
                var query = await request.Content!.ReadAsStringAsync();
                Assert.Contains("@Me", query);
                Assert.Contains("@project", query);
                return ApiHandler.Json(JsonSerializer.Serialize(new { workItems = path.Contains("/Portal/") ? Enumerable.Range(1, 201).Select(id => new { id }).ToArray() : [] }));
            }
            if (path.EndsWith("/workitemsbatch"))
            {
                using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
                var ids = payload.RootElement.GetProperty("ids").EnumerateArray().Select(id => id.GetInt32()).ToArray();
                batchSizes.Add(ids.Length);
                return ApiHandler.Json(JsonSerializer.Serialize(new
                {
                    value = ids.Select(id => new
                    {
                        id, rev = 3,
                        fields = new Dictionary<string, string>
                        {
                            ["System.Title"] = "Ticket " + id, ["System.WorkItemType"] = "Bug", ["System.TeamProject"] = "Portal",
                            ["System.State"] = id == 1 ? "Shipped" : id == 2 ? "Abandoned" : "Investigating"
                        }
                    })
                }));
            }
            Assert.EndsWith("/workitemtypes/Bug/states", path);
            return ApiHandler.Json("{\"value\":[{\"name\":\"Investigating\",\"category\":\"InProgress\"},{\"name\":\"Shipped\",\"category\":\"Completed\"},{\"name\":\"Abandoned\",\"category\":\"Removed\"}]}");
        }));
        var groups = await new AzureDevOpsTicketService(client, tokens, new AzureDevOpsSettings("company", [])).GetAssignedActiveAsync(CancellationToken.None);
        Assert.Equal(2, groups.Count);
        Assert.Equal(new[] { 200, 1 }, batchSizes);
        var portal = groups.Single(group => group.Project == "Portal");
        Assert.Equal(199, portal.Tickets.Count);
        Assert.DoesNotContain(portal.Tickets, ticket => ticket.Id is 1 or 2);
        Assert.Empty(groups.Single(group => group.Project == "Tools").Tickets);
        Assert.All(groups, group => Assert.Null(group.Error));
        Assert.All(tokens.Resources, resource => Assert.Equal(TokenResource.AzureDevOps, resource));
    }

    [Fact]
    public async Task ProjectAccessFailureDoesNotHideOtherProjects()
    {
        using var client = new HttpClient(new ApiHandler(request => Task.FromResult(request.RequestUri!.AbsolutePath.Contains("/Denied/")
            ? new HttpResponseMessage(HttpStatusCode.Forbidden) : ApiHandler.Json("{\"workItems\":[]}"))));
        var groups = await new AzureDevOpsTicketService(client, new TestTokens(), new AzureDevOpsSettings("company", ["Denied", "Allowed"])).GetAssignedActiveAsync(CancellationToken.None);
        Assert.NotNull(groups[0].Error);
        Assert.Null(groups[1].Error);
    }

    [Fact]
    public async Task CloseUsesProjectsCompletedStateAndTestsRevisionBeforeWriting()
    {
        var patched = false;
        using var client = new HttpClient(new ApiHandler(async request =>
        {
            if (request.Method == HttpMethod.Get)
                return ApiHandler.Json("{\"value\":[{\"name\":\"Shipped\",\"category\":\"Completed\"}]}");
            Assert.Equal(HttpMethod.Patch, request.Method);
            Assert.Equal("application/json-patch+json", request.Content!.Headers.ContentType!.MediaType);
            using var payload = JsonDocument.Parse(await request.Content.ReadAsStringAsync());
            Assert.Equal("test", payload.RootElement[0].GetProperty("op").GetString());
            Assert.Equal("/rev", payload.RootElement[0].GetProperty("path").GetString());
            Assert.Equal(7, payload.RootElement[0].GetProperty("value").GetInt32());
            Assert.Equal("Shipped", payload.RootElement[1].GetProperty("value").GetString());
            patched = true;
            return ApiHandler.Json("{}");
        }));
        await new AzureDevOpsTicketService(client, new TestTokens(), new AzureDevOpsSettings("company", ["Portal"]))
            .CloseAsync(new WorkTicket(12, "Portal", "Title", "Active", "Bug", "https://dev.azure.com/company/Portal/_workitems/edit/12", 7), CancellationToken.None);
        Assert.True(patched);
    }
}
