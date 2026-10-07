using System.Net.Http.Headers;
using System.Net.Http.Json;
using WorkFlowWidget.Core;

namespace WorkFlowWidget.Infrastructure;

public sealed class GraphAccountVerifier(HttpClient httpClient)
{
    public async Task<SignedInUser> VerifyAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            "https://graph.microsoft.com/v1.0/me?$select=id,displayName,mail,userPrincipalName");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var profile = await response.Content.ReadFromJsonAsync<GraphProfile>(cancellationToken);
        if (profile is null || string.IsNullOrWhiteSpace(profile.Id) || string.IsNullOrWhiteSpace(profile.DisplayName))
            throw new InvalidOperationException("Microsoft Graph did not return a valid account profile.");
        return new SignedInUser(profile.Id, profile.DisplayName, profile.Mail ?? profile.UserPrincipalName);
    }

    private sealed record GraphProfile(string Id, string DisplayName, string? Mail, string? UserPrincipalName);
}
