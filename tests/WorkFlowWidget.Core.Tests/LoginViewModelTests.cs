using WorkFlowWidget.Core;
using WorkFlowWidget.Desktop;
using Xunit;

namespace WorkFlowWidget.Core.Tests;

public sealed class LoginViewModelTests
{
    [Fact]
    public async Task VerifiedSignInShowsAccountAndSignOutClearsIt()
    {
        var service = new FakeAuthentication();
        var model = new LoginViewModel(service);
        await model.AuthenticateAsync(CancellationToken.None);
        Assert.Equal("Signed in · verified", model.Status);
        Assert.Contains("Taylor", model.AccountDetails);
        Assert.Equal("Sign out", model.ActionLabel);
        await model.AuthenticateAsync(CancellationToken.None);
        Assert.Equal("Not signed in", model.Status);
        Assert.DoesNotContain("Taylor", model.AccountDetails);
        Assert.True(service.SignedOut);
    }

    [Fact]
    public async Task FailedVerificationDoesNotClaimSignedIn()
    {
        var model = new LoginViewModel(new FakeAuthentication { Fail = true });
        await model.AuthenticateAsync(CancellationToken.None);
        Assert.Equal("Not signed in", model.Status);
        Assert.NotNull(model.Error);
        Assert.True(model.CanAct);
    }

    [Fact]
    public async Task RestoreWithoutSessionStaysSignedOut()
    {
        var model = new LoginViewModel(new FakeAuthentication());
        await model.RestoreAsync(CancellationToken.None);
        Assert.Equal("Not signed in", model.Status);
        Assert.Null(model.Error);
    }

    [Fact]
    public async Task FailedSignOutRetainsVerifiedAccount()
    {
        var service = new FakeAuthentication();
        var model = new LoginViewModel(service);
        await model.AuthenticateAsync(CancellationToken.None);
        service.Fail = true;
        await model.AuthenticateAsync(CancellationToken.None);
        Assert.Equal("Signed in · verified", model.Status);
        Assert.NotNull(model.Error);
    }

    private sealed class FakeAuthentication : IAuthenticationService
    {
        public bool Fail { get; set; }
        public bool SignedOut { get; private set; }
        public Task<SignedInUser?> RestoreSessionAsync(CancellationToken cancellationToken) => Task.FromResult<SignedInUser?>(null);
        public Task<SignedInUser> SignInAsync(CancellationToken cancellationToken) => Fail
            ? Task.FromException<SignedInUser>(new HttpRequestException("Verification failed"))
            : Task.FromResult(new SignedInUser("1", "Taylor", "taylor@example.com"));
        public Task SignOutAsync(CancellationToken cancellationToken)
        {
            if (Fail) throw new InvalidOperationException("Sign-out failed");
            SignedOut = true;
            return Task.CompletedTask;
        }
    }
}
