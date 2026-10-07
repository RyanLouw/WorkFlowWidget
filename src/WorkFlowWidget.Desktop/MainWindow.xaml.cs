using System.Net.Http;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Controls;
using System.Diagnostics;
using WorkFlowWidget.Core;
using WorkFlowWidget.Infrastructure;

namespace WorkFlowWidget.Desktop;

public partial class MainWindow : Window
{
    private readonly CancellationTokenSource lifetime = new();
    private readonly HttpClient httpClient = new() { Timeout = TimeSpan.FromSeconds(30) };
    private LoginViewModel? viewModel;
    private DashboardViewModel? dashboard;
    public MainWindow() => InitializeComponent();

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var authentication = new MicrosoftAuthenticationService(AuthenticationSettings.Load(), httpClient,
                () => new WindowInteropHelper(this).Handle);
            viewModel = new LoginViewModel(authentication);
            dashboard = new DashboardViewModel(viewModel,
                new AzureDevOpsTicketService(httpClient, authentication, WorkSettings.LoadDevOps()),
                new GraphEmailService(httpClient, authentication), new SqliteTriageStore(SqliteTriageStore.DefaultPath));
            DataContext = dashboard;
            await viewModel.RestoreAsync(lifetime.Token);
            await dashboard.SessionChangedAsync(lifetime.Token);
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.Text.Json.JsonException or System.IO.IOException or UnauthorizedAccessException)
        {
            DataContext = new { Login = new { Status = "Setup required", AccountDetails = exception.Message,
                ActionLabel = "Restart after configuration", Error = "See README for Microsoft sign-in setup." }, CanAuthenticate = false, CanUse = false };
        }
        catch (Exception)
        {
            DataContext = new { Login = new { Status = "Sign-in unavailable", AccountDetails = "Microsoft authentication could not be initialized.",
                ActionLabel = "Restart to try again", Error = "Check app registration and Windows broker availability. See README." }, CanAuthenticate = false, CanUse = false };
        }
    }

    private async void Authenticate(object sender, RoutedEventArgs e)
    {
        if (viewModel is null || dashboard is null || !dashboard.CanAuthenticate) return;
        var previousUser = viewModel.User;
        await viewModel.AuthenticateAsync(lifetime.Token);
        if (previousUser != viewModel.User) await dashboard.SessionChangedAsync(lifetime.Token);
    }
    private async void RefreshWork(object sender, RoutedEventArgs e)
    {
        if (dashboard is not null) await dashboard.RefreshAsync(lifetime.Token);
    }
    private async Task Categorize(object sender, EmailCategory category)
    {
        if (dashboard is not null && sender is Button { Tag: InboxEmail email })
            await dashboard.CategorizeAsync(email, category, lifetime.Token);
    }
    private async void CategorizeBug(object sender, RoutedEventArgs e) => await Categorize(sender, EmailCategory.NewBug);
    private async void CategorizeTicket(object sender, RoutedEventArgs e) => await Categorize(sender, EmailCategory.NewTicket);
    private async void CategorizeHelpdesk(object sender, RoutedEventArgs e) => await Categorize(sender, EmailCategory.HelpdeskQuery);
    private async void RetrySync(object sender, RoutedEventArgs e)
    {
        if (dashboard is not null && sender is Button { Tag: TrackedEmail email }) await dashboard.RetrySyncAsync(email, lifetime.Token);
    }
    private async void MarkReplied(object sender, RoutedEventArgs e)
    {
        if (dashboard is not null && sender is Button { Tag: TrackedEmail email }) await dashboard.SetRepliedAsync(email, true, lifetime.Token);
    }
    private async void NeedsReply(object sender, RoutedEventArgs e)
    {
        if (dashboard is not null && sender is Button { Tag: TrackedEmail email }) await dashboard.SetRepliedAsync(email, false, lifetime.Token);
    }
    private async void CloseTicket(object sender, RoutedEventArgs e)
    {
        if (dashboard is not null && dashboard.CanUse && sender is Button { Tag: WorkTicket ticket }
            && MessageBox.Show(this, $"Mark #{ticket.Id} — {ticket.Title} as completed in {ticket.Project}?", "Close ticket",
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            await dashboard.CloseTicketAsync(ticket, lifetime.Token);
    }
    private void OpenTicket(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: WorkTicket ticket }) OpenLink(ticket.WebUrl);
    }
    private void OpenInboxEmail(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: InboxEmail email }) OpenLink(email.WebUrl);
    }
    private void OpenTrackedEmail(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: TrackedEmail email }) OpenLink(email.Email.WebUrl);
    }
    private void OpenLink(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !uri.IsDefaultPort ||
            !string.IsNullOrEmpty(uri.UserInfo) || uri.Host is not ("dev.azure.com" or "outlook.office.com" or "outlook.office365.com" or "outlook.live.com"))
        {
            MessageBox.Show(this, "This item has no supported Microsoft web link.");
            return;
        }
        try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception) { MessageBox.Show(this, "Could not open your browser."); }
    }
    private void DragWindow(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && e.OriginalSource is not System.Windows.Controls.Button) DragMove();
    }
    private void CloseWindow(object sender, RoutedEventArgs e) => Close();
    private void OnClosed(object? sender, EventArgs e)
    {
        lifetime.Cancel();
        httpClient.Dispose();
    }
}
