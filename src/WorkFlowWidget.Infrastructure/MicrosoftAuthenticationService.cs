using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Broker;
using System.Runtime.Versioning;
using WorkFlowWidget.Core;

namespace WorkFlowWidget.Infrastructure;

[SupportedOSPlatform("windows")]
public sealed class MicrosoftAuthenticationService : IAuthenticationService, IAccessTokenProvider
{
    private static readonly string[] Scopes = ["https://graph.microsoft.com/User.Read", "https://graph.microsoft.com/Mail.ReadWrite"];
    private static readonly string[] DevOpsScopes = ["499b84ac-1321-427f-aa17-267ca6975798/.default"];
    private IAccount? activeAccount;
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
            var user = await verifier.VerifyAsync(result.AccessToken, cancellationToken);
            activeAccount = result.Account;
            return user;
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
        var user = await verifier.VerifyAsync(result.AccessToken, cancellationToken);
        activeAccount = result.Account;
        return user;
    }

    public async Task SignOutAsync(CancellationToken cancellationToken)
    {
        foreach (var account in await application.GetAccountsAsync())
        {
            cancellationToken.ThrowIfCancellationRequested();
            await application.RemoveAsync(account);
        }
        activeAccount = null;
    }

    public async Task<string> GetTokenAsync(TokenResource resource, CancellationToken cancellationToken)
    {
        var account = activeAccount ?? throw new InvalidOperationException("Sign in before loading work data.");
        var scopes = resource == TokenResource.MicrosoftGraph ? Scopes : DevOpsScopes;
        AuthenticationResult result;
        try
        {
            result = await application.AcquireTokenSilent(scopes, account).ExecuteAsync(cancellationToken);
        }
        catch (MsalUiRequiredException)
        {
            result = await application.AcquireTokenInteractive(scopes).WithAccount(account)
                .WithParentActivityOrWindow(windowHandle()).ExecuteAsync(cancellationToken);
        }
        if (result.Account.HomeAccountId.Identifier != account.HomeAccountId.Identifier)
            throw new InvalidOperationException("Use the same Microsoft account for both email and tickets.");
        return result.AccessToken;
    }
}
