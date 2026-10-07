using System.Net.Http.Headers;
using WorkFlowWidget.Core;

namespace WorkFlowWidget.Infrastructure;

internal sealed class AuthorizedApi(HttpClient client, IAccessTokenProvider tokens, TokenResource resource, string host)
{
    public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri is null || request.RequestUri.Scheme != "https" ||
            !request.RequestUri.Host.Equals(host, StringComparison.OrdinalIgnoreCase) || !request.RequestUri.IsDefaultPort ||
            !string.IsNullOrEmpty(request.RequestUri.UserInfo))
            throw new InvalidOperationException("The service returned an unexpected API destination.");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await tokens.GetTokenAsync(resource, cancellationToken));
        var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.PreconditionFailed)
        {
            var status = response.StatusCode;
            response.Dispose();
            throw new HttpRequestException($"{host} returned HTTP {(int)status}. Check permissions and configuration.", null, status);
        }
        return response;
    }
}
