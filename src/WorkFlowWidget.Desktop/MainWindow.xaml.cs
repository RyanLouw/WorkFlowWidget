using System.Net.Http;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using WorkFlowWidget.Infrastructure;

namespace WorkFlowWidget.Desktop;

public partial class MainWindow : Window
{
    private readonly CancellationTokenSource lifetime = new();
    private readonly HttpClient httpClient = new() { Timeout = TimeSpan.FromSeconds(30) };
    private LoginViewModel? viewModel;
    public MainWindow() => InitializeComponent();

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var authentication = new MicrosoftAuthenticationService(AuthenticationSettings.Load(), httpClient,
                () => new WindowInteropHelper(this).Handle);
            viewModel = new LoginViewModel(authentication);
            DataContext = viewModel;
            await viewModel.RestoreAsync(lifetime.Token);
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.Text.Json.JsonException or System.IO.IOException or UnauthorizedAccessException)
        {
            DataContext = new { Status = "Setup required", AccountDetails = exception.Message,
                ActionLabel = "Restart after configuration", CanAct = false, Error = "See README for Microsoft sign-in setup." };
        }
        catch (Exception)
        {
            DataContext = new { Status = "Sign-in unavailable", AccountDetails = "Microsoft authentication could not be initialized.",
                ActionLabel = "Restart to try again", CanAct = false, Error = "Check app registration and Windows broker availability. See README." };
        }
    }

    private async void Authenticate(object sender, RoutedEventArgs e)
    {
        if (viewModel is not null) await viewModel.AuthenticateAsync(lifetime.Token);
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
