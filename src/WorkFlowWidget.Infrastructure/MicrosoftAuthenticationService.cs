using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Broker;
using WorkFlowWidget.Core;

namespace WorkFlowWidget.Infrastructure;

public sealed class MicrosoftAuthenticationService : IAuthenticationService
{
    private static readonly string[] Scopes = ["https://graph.microsoft.com/User.Read"];
    private readonly IPublicClientApplication application;
    private readonly GraphAccountVerifier verifier;
    private readonly Func<IntPtr> windowHandle;

    public MicrosoftAuthenticationService(AuthenticationSettings settings, HttpClient httpClient, Func<IntPtr> windowHandle)
    {
        verifier = new GraphAccountVerifier(httpClient);
        this.windowHandle = windowHandle;
        application = PublicClientApplicationBuilder.Create(settings.ClientId)
            .WithAuthority(AzureCloudInstance.AzurePublic, settings.TenantId)
            .WithDefaultRedirectUri()
            .WithBroker(new BrokerOptions(BrokerOptions.OperatingSystems.Windows))
            .Build();
    }

    public async Task<SignedInUser?> RestoreSessionAsync(CancellationToken cancellationToken)
    {
        var accounts = (await application.GetAccountsAsync()).ToArray();
        // Never silently choose between multiple accounts.
        if (accounts.Length != 1) return null;
        try
        {
            var result = await application.AcquireTokenSilent(Scopes, accounts[0]).ExecuteAsync(cancellationToken);
            return await verifier.VerifyAsync(result.AccessToken, cancellationToken);
        }
        catch (MsalUiRequiredException)
        {
            return null;
        }
    }

    public async Task<SignedInUser> SignInAsync(CancellationToken cancellationToken)
    {
        var result = await application.AcquireTokenInteractive(Scopes)
            .WithParentActivityOrWindow(windowHandle())
            .WithPrompt(Prompt.SelectAccount)
            .ExecuteAsync(cancellationToken);
        return await verifier.VerifyAsync(result.AccessToken, cancellationToken);
    }

    public async Task SignOutAsync(CancellationToken cancellationToken)
    {
        foreach (var account in await application.GetAccountsAsync())
        {
            cancellationToken.ThrowIfCancellationRequested();
            await application.RemoveAsync(account);
        }
    }

}
