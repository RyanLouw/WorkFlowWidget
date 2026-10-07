namespace WorkFlowWidget.Core;

public sealed record SignedInUser(string Id, string DisplayName, string? Email);

public interface IAuthenticationService
{
    Task<SignedInUser?> RestoreSessionAsync(CancellationToken cancellationToken);
    Task<SignedInUser> SignInAsync(CancellationToken cancellationToken);
    Task SignOutAsync(CancellationToken cancellationToken);
}
