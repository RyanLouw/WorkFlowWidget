using System.ComponentModel;
using WorkFlowWidget.Core;

namespace WorkFlowWidget.Desktop;

public sealed class LoginViewModel : INotifyPropertyChanged
{
    private readonly IAuthenticationService authentication;
    private SignedInUser? user;
    private bool busy;
    private string? error;
    public LoginViewModel(IAuthenticationService authentication) => this.authentication = authentication;
    public string Status => busy ? "Connecting…" : user is null ? "Not signed in" : "Signed in · verified";
    public string AccountDetails => user is null ? "Your account will be verified with Microsoft." : $"{user.DisplayName}\n{user.Email}";
    public string ActionLabel => user is null ? "Sign in with Microsoft" : "Sign out";
    public bool CanAct => !busy;
    public string? Error => error;
    public event PropertyChangedEventHandler? PropertyChanged;

    public Task RestoreAsync(CancellationToken cancellationToken) => ExecuteAsync(async () =>
        user = await authentication.RestoreSessionAsync(cancellationToken), "Could not restore your session. Check your connection and sign in again.");

    public Task AuthenticateAsync(CancellationToken cancellationToken) => ExecuteAsync(async () =>
    {
        if (user is null) user = await authentication.SignInAsync(cancellationToken);
        else
        {
            await authentication.SignOutAsync(cancellationToken);
            user = null;
        }
    }, user is null
        ? "Could not verify your account. Check your connection and app registration, then try again."
        : "Could not sign out. Please try again.");

    private async Task ExecuteAsync(Func<Task> action, string failureMessage)
    {
        if (busy) return;
        busy = true;
        error = null;
        Notify();
        try { await action(); }
        catch (OperationCanceledException) { error = "Sign-in cancelled. You can try again."; }
        catch (Exception) { error = failureMessage; }
        finally { busy = false; Notify(); }
    }

    private void Notify() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
}
